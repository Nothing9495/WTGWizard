using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Builders;
using WTGWizard.Main.DeploymentCore.Contracts;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Worker;
using WTGWizard.Main.Language;
using WTGWizard.Shared.Services.DiskServices;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Main.DeploymentCore.Orchestrator;

public sealed class DeploymentOrchestrator : IDeploymentOrchestrator
{
    private readonly IDeploymentPipeline _pipeline;
    private readonly IDriveLetterService _driveLetter;
    private readonly ILoggerService _logger;
    private readonly IWorkerProcess _worker;
    private readonly WorkerCommandFactory _commands;
    private readonly TempFileManager _tempFiles;
    private readonly ObservableCollection<DeployTaskItem> _tasks;
    private readonly Subject<TaskUpdate> _subject = new();
    private DeploymentConfig _currentConfig;

    public IObservable<TaskUpdate> Progress => _subject;
    public ObservableCollection<DeployTaskItem> Tasks => _tasks;

    /// <summary>目标磁盘编号，供性能监控使用。</summary>
    public uint DiskNumber
    {
        get
        {
            int id = _currentConfig.DiskSelectedId;
            if (id < 0)
                throw new InvalidOperationException($"Invalid disk number: {id}");
            return (uint)id;
        }
    }

    public DeploymentOrchestrator(
        IDeploymentPipeline pipeline, DeploymentConfig config,
        IDriveLetterService driveLetter, ILoggerService logger,
        IWorkerProcess worker, WorkerCommandFactory commands,
        TempFileManager tempFiles)
    {
        _pipeline = pipeline;
        _currentConfig = config;
        _driveLetter = driveLetter;
        _logger = logger;
        _worker = worker;
        _commands = commands;
        _tempFiles = tempFiles;
        _tasks = new ObservableCollection<DeployTaskItem>(
            _pipeline.Steps
                .Where(s => s.ShouldRun(config))
                .Select(s => new DeployTaskItem
                {
                    Id = s.TaskId.Value,
                    Title = Localization.GetString(s.TitleKey),
                    Description = Localization.GetString(
                        s.TaskId == DeployTaskId.RemoveDriveLetters && config.AutoRemoveOsDriveLetter
                            ? "Task.RemoveDriveLetters.Desc.EspOs"
                            : s.DescriptionKey)
                }));

        _logger.Debug("Orchestrator", "Pipeline: {Steps}",
            string.Join(" → ", _tasks.Select(t => t.Id)));
        LogDeploymentConfig();
    }

    public async Task<DeploymentResult> StartAsync(CancellationToken ct = default)
    {
        _logger.Info("Orchestrator", "Deployment started, total tasks: {Count}", _tasks.Count);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            foreach (var step in _pipeline.Steps)
            {
                // 步骤间即时取消检查：Abort 后当前任务自然结束，下一循环立即响应；
                // 仅"下一任务"（当前循环 step）标记 Cancelled，剩余任务保持 Pending
                if (ct.IsCancellationRequested)
                {
                    var nextTask = _tasks.FirstOrDefault(t => t.Id == step.TaskId.Value);
                    if (nextTask is not null)
                        nextTask.Status = DeployTaskStatus.Cancelled;
                    throw new OperationCanceledException(ct);
                }

                if (!step.ShouldRun(_currentConfig))
                {
                    _logger.Debug("Orchestrator", "Task skipped: {TaskId}", step.TaskId.Value);
                    _subject.OnNext(new(step.TaskId, DeployTaskStatus.Skipped, 0));
                    continue;
                }

                _logger.Info("Orchestrator", "Task started: {TaskId}", step.TaskId.Value);

                using var ctx = new StepContext(_currentConfig, _worker,
                    _commands, _tempFiles, _logger);
                using var _ = ctx.Progress
                    .Subscribe(u =>
                    {
                        _subject.OnNext(u);
                        var task = _tasks.FirstOrDefault(t => t.Id == u.TaskId.Value);
                        if (task is not null)
                        {
                            task.Status = u.Status;
                            task.ProgressValue = u.Progress;
                        }
                    });

                var result = await step.ExecuteAsync(ctx, ct);

                if (!result.IsSuccess && !result.NonFatal)
                {
                    // 失败路径同样尊重取消：Abort 后任何失败结局一律归为 Cancelled
                    if (ct.IsCancellationRequested)
                        return DeploymentResult.Cancelled();

                    _logger.Error("Orchestrator", "Task failed: {TaskId} - {Error}", step.TaskId.Value,
                        result.ErrorMessage ?? "Unknown error");
                    return DeploymentResult.Failed(step.TaskId, result.ErrorMessage ?? "Unknown error");
                }

                _logger.Info("Orchestrator", "Task completed: {TaskId}", step.TaskId.Value);

                if (step.TaskId == DeployTaskId.CreateDiskLayout)
                    await ResolveDriveLettersAsync();
            }

            // 全部步骤自然执行完毕；若期间收到过取消请求，结局为 Cancelled
            if (ct.IsCancellationRequested)
                return DeploymentResult.Cancelled();

            _logger.Info("Orchestrator", "Deployment completed successfully in {Elapsed:F1}s.", sw.Elapsed.TotalSeconds);
            return DeploymentResult.Ok();
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Orchestrator", "Deployment cancelled after {Elapsed:F1}s.", sw.Elapsed.TotalSeconds);
            return DeploymentResult.Cancelled();
        }
        catch (Exception ex)
        {
            // Abort 场景的任何异常一律归为 Cancelled（取消语义优先于失败）
            if (ct.IsCancellationRequested)
                return DeploymentResult.Cancelled();

            _logger.Error("Orchestrator", "Deployment failed after {Elapsed:F1}s - ({Error}).", sw.Elapsed.TotalSeconds, ex.ToString());
            return DeploymentResult.Failed(null, ex.Message);
        }
    }

    public void Dispose()
    {
        _subject.OnCompleted();
        _subject.Dispose();
        _worker.Dispose();
        _tempFiles.Dispose();
    }

    /// <summary>
    /// 硬中断当前任务（仅关闭流程调用）— 委托给 WorkerProcess。
    /// </summary>
    public void ForceCancelCurrentTask() => _worker.ForceCancelCurrentTask();

    private async Task ResolveDriveLettersAsync()
    {
        uint diskNum = (uint)_currentConfig.DiskSelectedId;

        if (_currentConfig.IsCleanInstall)
        {
            char esp = await _driveLetter.QueryActualDriveLetterAsync(diskNum, DiskConstants.CleanInstallEspPartNum);
            char os = await _driveLetter.QueryActualDriveLetterAsync(diskNum, DiskConstants.CleanInstallOsPartNum);
            _currentConfig = _currentConfig with { EspDriveLetter = esp, OsDriveLetter = os };
        }
        else if (_currentConfig.SelectedPartitionDriveLetter is { } letter)
        {
            char esp = await _driveLetter.QueryActualDriveLetterAsync(diskNum, _currentConfig.EspVolumeId);
            _currentConfig = _currentConfig with { EspDriveLetter = esp, OsDriveLetter = letter[0] };
        }
        else
        {
            throw new InvalidOperationException("No target partition for drive letter resolution");
        }

        _logger.Debug("Orchestrator", "Drive letters resolved: ESP={Esp}, OS={Os}",
            _currentConfig.EspDriveLetter, _currentConfig.OsDriveLetter);
    }

    private void LogDeploymentConfig()
    {
        var c = _currentConfig;
        _logger.Debug("DeployConfig", "══════ Deployment Configuration ══════");

        _logger.Debug("DeployConfig", "[ImageSrc]  {Src}", c.SrcImageFile);
        _logger.Debug("DeployConfig", "[ImageInfo] Index={Idx}, Arch={Arch}, Build={Build}, Expand={Exp}GB",
            c.ImageSelectedIndex, c.ImageWindowsArch, c.ImageWinBuildNum, c.ImageExpandedSize);

        string diskSizeGib = (c.DiskSizeBytes / DiskConstants.BytesPerGiB).ToString("F1");
        _logger.Debug("DeployConfig", "[TargetDisk] Disk #{Id}, {Size}GiB", c.DiskSelectedId, diskSizeGib);

        if (c.IsCleanInstall)
        {
            _logger.Debug("DeployConfig", "[InstallType] Clean Install");
            _logger.Debug("DeployConfig", "[DiskLayout] ESP={Efi}MB, OS={Os:F2}/{Max:F2}GB, OS Label=\"{Lbl}\"",
                c.EfiPartSize, c.OsDriveSize, c.MaxOsDriveSize, c.OsDriveLabel);
            if (c.EnableReservedVol)
                _logger.Debug("DeployConfig", "[DiskLayout] Reserved Label=\"{Lbl}\", Reserved FS={Fs}",
                    c.ReservedDriveLabel, c.ReservedDriveFs);
        }
        else
        {
            _logger.Debug("DeployConfig", "[InstallType] Partition Install");
            _logger.Debug("DeployConfig", "[Partitions] ESP=#{Esp}, OS=#{Os}, OS Letter={L}",
                c.EspVolumeId, c.OsDriveVolumeId, c.SelectedPartitionDriveLetter ?? "?");
        }

        _logger.Debug("DeployConfig", "[DriveLetter] ESP={Esp}:, OS={Os}:",
            c.EspDriveLetter, c.OsDriveLetter);

        _logger.Debug("DeployConfig", "[OSDriveSettings] NoDefaultDriveLetter={N}, AutoRemoveDriveLetter={A}",
            c.NoDefaultDriveLetter, c.AutoRemoveOsDriveLetter);

        _logger.Debug("DeployConfig", "[DeployOpts] UseDismToDeploy={D}", c.UseDismToDeploy);

        _logger.Debug("DeployConfig", "[SysSettings] HideLocalDisks={H}, PreventDeviceEncryption={P}",
            c.HideLocalDisks, c.PreventDeviceEncryption);

        if (c.DriverIntegrationEnabled)
            _logger.Debug("DeployConfig", "[DrvInt] Drivers Path={P}, Allow Unsigned Drivers={F}",
                c.DriversDirectoryPath ?? "(none)", c.ForceUnsignedDriver);

        if (c.CustomAnsFileEnabled)
            _logger.Debug("DeployConfig", "[AnsFile] File Path={P}, Clean In-image Answer File={C}",
                c.AnsFilePath ?? "(none)", c.CleanImageAnsFile);

        _logger.Debug("DeployConfig", "[BcdBoot] Detailed Output={V}, BootEx={E}",
            c.EnableBootVerbose, c.EnableBootEx);

        _logger.Debug("DeployConfig", "════════════════════════════════════");
    }
}
