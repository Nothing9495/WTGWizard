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
using WTGWizard.Shared.Services.DiskServices;
using WTGWizard.Shared.Services.Logger;
using static WTGWizard.Main.DeploymentCore.Models.DeploymentConstants;

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
            _pipeline.ActiveTasks(config).Select(id => new DeployTaskItem
            {
                Id = id.Value,
                Title = id.Value,
                Description = id.Value
            }));

        _logger.Debug("Orchestrator", "Pipeline: {Steps}",
            string.Join(" → ", _tasks.Select(t => t.Id)));
        LogDeploymentConfig();
    }

    public async Task<DeploymentResult> StartAsync(CancellationToken ct = default)
    {
        try
        {
            foreach (var step in _pipeline.Steps)
            {
                if (!step.ShouldRun(_currentConfig))
                {
                    _subject.OnNext(new(step.TaskId, DeployTaskStatus.Skipped, 0));
                    continue;
                }

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

                if (!result.IsSuccess)
                    return DeploymentResult.Failed(step.TaskId, result.ErrorMessage ?? "Unknown error");

                if (step.TaskId == DeployTaskId.Partition)
                    await ResolveDriveLettersAsync();
            }

            return DeploymentResult.Ok();
        }
        catch (OperationCanceledException) { return DeploymentResult.Cancelled(); }
        catch (Exception ex) { return DeploymentResult.Failed(null, ex.Message); }
    }

    public void Dispose()
    {
        _subject.OnCompleted();
        _subject.Dispose();
        _worker.Dispose();
        _tempFiles.Dispose();
    }

    private async Task ResolveDriveLettersAsync()
    {
        uint diskNum = (uint)_currentConfig.DiskSelectedId;

        if (_currentConfig.IsCleanInstall)
        {
            char esp = await _driveLetter.QueryActualDriveLetterAsync(diskNum, CleanInstallEspPartNum);
            char os = await _driveLetter.QueryActualDriveLetterAsync(diskNum, CleanInstallOsPartNum);
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
        _logger.Debug("DeployConfig", "[Image] Source={Src}", c.SrcImageFile);
        _logger.Debug("DeployConfig", "[Image] Index={Idx}, Arch={Arch}, Build={Build}",
            c.ImageSelectedIndex, c.ImageWindowsArch, c.ImageWinBuildNum);
        _logger.Debug("DeployConfig", "[Disk]  #{Id}, Clean={Clean}, Size={Size:N0} bytes",
            c.DiskSelectedId, c.IsCleanInstall, c.DiskSizeBytes);
        if (c.IsCleanInstall)
        {
            _logger.Debug("DeployConfig", "[Part] EFI={Efi}MB, OS={Os:F2}GB/{Max:F2}GB, Label={Lbl}",
                c.EfiPartSize, c.OsDriveSize, c.MaxOsDriveSize, c.OsDriveLabel);
            _logger.Debug("DeployConfig", "[Part] Reserved={Res}, RsvLabel={RLbl}, RsvFS={RFs}",
                c.EnableReservedVol, c.ReservedDriveLabel, c.ReservedDriveFs);
        }
        else
        {
            _logger.Debug("DeployConfig", "[Part] ESP=#{EspId}, OS=#{OsId}, Letter={Ltr}",
                c.EspVolumeId, c.OsDriveVolumeId, c.SelectedPartitionDriveLetter);
        }
        _logger.Debug("DeployConfig", "[Letter] ESP={Esp}:, OS={Os}:, NoDefault={N}, AutoRemove={A}",
            c.EspDriveLetter, c.OsDriveLetter, c.NoDefaultDriveLetter, c.AutoRemoveOsDriveLetter);
        _logger.Debug("DeployConfig", "[Driver] Enabled={E}, Path={P}, ForceUnsigned={F}",
            c.DriverIntegrationEnabled, c.DriversDirectoryPath ?? "(none)", c.ForceUnsignedDriver);
        _logger.Debug("DeployConfig", "[Ans]   Custom={C}, Path={P}, CleanBuiltin={B}",
            c.CustomAnsFileEnabled, c.AnsFilePath ?? "(none)", c.CleanImageAnsFile);
        _logger.Debug("DeployConfig", "[WTG]   SanPolicy={S}, NoEncrypt={E}",
            c.HideLocalDisks, c.PreventDeviceEncryption);
        _logger.Debug("DeployConfig", "[Boot]  BootEx={Ex}, Verbose={V}",
            c.EnableBootEx, c.EnableBootVerbose);
        _logger.Debug("DeployConfig", "════════════════════════════════════");
    }
}
