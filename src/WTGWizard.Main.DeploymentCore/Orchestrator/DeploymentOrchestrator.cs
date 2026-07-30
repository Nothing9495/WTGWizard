using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.DplySteps;
using WTGWizard.Shared.Services.DiskServices;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Main.DeploymentCore.Orchestrator;

/// <summary>
/// 部署编排器 — 按序执行步骤，更新任务状态供 TaskPage 展示。
/// </summary>
public sealed class DeploymentOrchestrator
{
    private readonly StepContext _ctx;
    private readonly IReadOnlyList<IDeploymentStep> _steps;
    private readonly IDriveLetterService _driveLetterService;
    private readonly ObservableCollection<DeployTaskItem> _tasks;

    public ObservableCollection<DeployTaskItem> Tasks => _tasks;

    public uint DiskNumber
    {
        get
        {
            int id = _ctx.Config.DiskSelectedId;
            if (id < 0)
                throw new InvalidOperationException($"无效的磁盘编号: {id}");
            return (uint)id;
        }
    }

    public DeploymentOrchestrator(
        DeploymentConfig config,
        IDriveLetterService driveLetterService,
        ILoggerService logger)
    {
        _driveLetterService = driveLetterService;
        _tasks = BuildTaskList(config);
        _ctx = new StepContext(config, logger);

        _ctx.OnTaskStatusChanged += (id, status, progress) =>
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task is null) return;
            task.ProgressValue = progress;
            task.Status = status;
        };

        _ctx.OnTaskProgressChanged += (id, value) =>
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task is not null)
                task.ProgressValue = value;
        };

        _ctx.OnCurrentTaskFailed += () =>
        {
            var task = _tasks.FirstOrDefault(t => t.Status == DeployTaskStatus.Running);
            if (task is not null)
                task.Status = DeployTaskStatus.Failed;
        };

        _steps = BuildSteps();
        LogDeploymentConfig();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Debug("Orchestrator", "Deployment started");

        string? osApplyDir = null;

        try
        {
            foreach (IDeploymentStep step in _steps)
            {
                if (!step.ShouldRun(_ctx.Config))
                {
                    _ctx.Logger.Debug("Orchestrator", "Step skipped: {TaskId}", step.TaskId);
                    continue;
                }

                await step.ExecuteAsync(_ctx, osApplyDir, ct);

                if (step.TaskId == "partition")
                {
                    osApplyDir = await ResolveDriveLettersAsync(ct);
                    if (string.IsNullOrEmpty(osApplyDir))
                    {
                        _ctx.Logger.Error("Orchestrator", "Drive letter resolution returned empty");
                        throw new InvalidOperationException("Drive letter resolution returned empty — partition step may have failed");
                    }
                }
            }

            _ctx.Logger.Debug("Orchestrator", "Deployment completed");
        }
        catch (OperationCanceledException)
        {
            _ctx.Logger.Warn("Orchestrator", "Deployment cancelled");
            _ctx.MarkCurrentTaskFailed();
        }
        catch (Exception ex)
        {
            _ctx.MarkCurrentTaskFailed();
            _ctx.Logger.Error("Orchestrator", "Deployment error: {Msg}", ex.Message);
            throw;
        }
    }

    private IReadOnlyList<IDeploymentStep> BuildSteps()
    {
        return new IDeploymentStep[]
        {
            new PartitionStep(),
            new ExtractStep(),
            new DriverStep(),
            new ImportAnsFileStep(),
            new ApplySettingsStep(),
            new BcdbootStep(),
            new CleanupStep(),
        };
    }

    private static ObservableCollection<DeployTaskItem> BuildTaskList(DeploymentConfig config)
    {
        var list = new ObservableCollection<DeployTaskItem>();

        foreach (IDeploymentStep step in AllSteps())
        {
            if (!step.ShouldRun(config)) continue;

            list.Add(new()
            {
                Id = step.TaskId,
                Title = step.TaskId,
                Description = step.TaskId,
            });
        }

        return list;
    }

    private static IEnumerable<IDeploymentStep> AllSteps()
    {
        yield return new PartitionStep();
        yield return new ExtractStep();
        yield return new DriverStep();
        yield return new ImportAnsFileStep();
        yield return new ApplySettingsStep();
        yield return new BcdbootStep();
        yield return new CleanupStep();
    }

    private async Task<string> ResolveDriveLettersAsync(CancellationToken ct)
    {
        DeploymentConfig config = _ctx.Config;
        uint diskNum = (uint)config.DiskSelectedId;

        if (config.IsCleanInstall)
        {
            config.EspDriveLetter = await _driveLetterService.QueryActualDriveLetterAsync(
                diskNum, DeploymentConstants.CleanInstallEspPartNum);

        char osLetter = await _driveLetterService.QueryActualDriveLetterAsync(
                diskNum, DeploymentConstants.CleanInstallOsPartNum);
            config.OsDriveLetter = osLetter;
            _ctx.Logger.Debug("Orchestrator", "Drive letters resolved: ESP={Esp}, OS={Os}", config.EspDriveLetter, osLetter);

            return $"{osLetter}:\\";
        }
        else if (config.SelectedPartitionDriveLetter is string partLetter)
        {
            config.EspDriveLetter = await _driveLetterService.QueryActualDriveLetterAsync(
                diskNum, config.EspVolumeId);

            config.OsDriveLetter = partLetter[0];
            _ctx.Logger.Debug("Orchestrator", "Drive letters resolved: ESP={Esp}, OS={Os}", config.EspDriveLetter, partLetter[0]);

            return $"{partLetter}:\\";
        }
        else
        {
            throw new InvalidOperationException("No target partition for drive letter resolution");
        }
    }

    private void LogDeploymentConfig()
    {
        var c = _ctx.Config;
        _ctx.Logger.Debug("DeployConfig", "══════ Deployment Configuration ══════");

        _ctx.Logger.Debug("DeployConfig", "[ImageInfo]  Source={Src}", c.SrcImageFile);
        _ctx.Logger.Debug("DeployConfig", "[ImageInfo]  Index={Idx}, Arch={Arch}, Build={Build}, Expanded Size={ExpandedSize}GB",
            c.ImageSelectedIndex, c.ImageWindowsArch, c.ImageWinBuildNum, c.ImageExpandedSize);

        _ctx.Logger.Debug("DeployConfig", "[DiskInfo]   Disk #{Id}, Size={Size:N0} bytes",
            c.DiskSelectedId, c.DiskSizeBytes);

        _ctx.Logger.Debug("DeployConfig", "[InstType]   Installation Type: {InstType}",
            c.IsCleanInstall ? "Clean Install" : "Partition Install");

        if (c.IsCleanInstall)
        {
            _ctx.Logger.Debug("DeployConfig", "[DiskLayout] ESP Size={Efi}MB, OS Size={Os:F2}GB/{Max:F2}GB, OS Label={Lbl}",
                c.EfiPartSize, c.OsDriveSize, c.MaxOsDriveSize, c.OsDriveLabel);
            if (c.EnableReservedVol)
            {
                _ctx.Logger.Debug("DeployConfig", "[DiskLayout] Reserved Label={RLbl}, Reserved FS={RFs}",
                    c.ReservedDriveLabel, c.ReservedDriveFs.ToUpperInvariant());
            }
        }
        else
        {
            _ctx.Logger.Debug("DeployConfig", "[PartInfo]   ESP=#{EspVolId}, OS=#{OsVolId}, OS Letter={Letter}",
                c.EspVolumeId, c.OsDriveVolumeId, c.SelectedPartitionDriveLetter);
        }

        _ctx.Logger.Debug("DeployConfig", "[PartInfo]   Drive letter assignment: ESP={Esp}:, OS={Os}:",
            c.EspDriveLetter, c.OsDriveLetter);

        _ctx.Logger.Debug("DeployConfig", "[PartInfo]   OS Drive Settings: NoDefaultDriveLetter={N}, RemoveOsDriveLetter={R}",
            c.NoDefaultDriveLetter, c.AutoRemoveOsDriveLetter);

        _ctx.Logger.Debug("DeployConfig", "[SystemSets] HideLocalDisks={H}, PreventDeviceEncryption={P}",
            c.HideLocalDisks, c.PreventDeviceEncryption);

        _ctx.Logger.Debug("DeployConfig", "[DplyOption] UseDismToDeploy={Dism}", c.UseDismToDeploy);

        if (c.DriverIntegrationEnabled)
        {
            _ctx.Logger.Debug("DeployConfig", "[DriverInt]  Drivers Path={P}, ForceUnsigned={F}",
                c.DriversDirectoryPath ?? "(none)", c.ForceUnsignedDriver);
        }

        if (c.CustomAnsFileEnabled)
        {
            _ctx.Logger.Debug("DeployConfig", "[AnsFile]    Answer File Path={P}, CleanImageAnsFile={C}",
                c.AnsFilePath ?? "(none)", c.CleanImageAnsFile);
        }

        _ctx.Logger.Debug("DeployConfig", "[BCDBoot]    Verbose Output={V}, BootEx={Ex}",
            c.EnableBootVerbose, c.EnableBootEx);

        _ctx.Logger.Debug("DeployConfig", "════════════════════════════════════");
    }
}
