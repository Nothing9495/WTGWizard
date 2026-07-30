using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Builders;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Main.DeploymentCore.WorkerCore;

namespace WTGWizard.Main.DeploymentCore.DplySteps;

/// <summary>
/// 步骤 5：应用 WTG 专有设置（SanPolicy + PreventDeviceEncryption 应答文件）。
/// </summary>
public sealed class ApplySettingsStep : IDeploymentStep
{
    public string TaskId => "apply-settings";
    public bool ShouldRun(DeploymentConfig config)
        => config.HideLocalDisks || config.PreventDeviceEncryption;

    public async Task ExecuteAsync(StepContext ctx, string? osApplyDir, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(osApplyDir))
            throw new InvalidOperationException("osApplyDir is null — partition step may not have run");

        ctx.SetTaskStatus("apply-settings", DeployTaskStatus.Running);

        string? unattendFile = AnswerFileGenerator.GenerateAndSave(ctx.Config);
        if (unattendFile is null)
        {
            ctx.Logger.Debug("ApplyWtg", "No WTG settings to apply");
            ctx.SetTaskStatus("apply-settings", DeployTaskStatus.Completed, 100);
            return;
        }

        string unattendArgs = CommandBuilder.BuildApplyUnattendArgs(osApplyDir, unattendFile);

        ctx.Logger.Debug("ApplyWtg", "Applying WTG settings: {Args}", unattendArgs);

        var (cmd, args) = WorkerFactory.BuildDism(unattendArgs, DeploymentConstants.TimeoutApplyWtgMs);
        var result = await ctx.WorkerManager.ExecuteCommandAsync(cmd, args, ct: ct);

        if (!result.Success)
        {
            ctx.SetTaskStatus("apply-settings", DeployTaskStatus.Failed);
            throw new InvalidOperationException($"Apply WTG settings failed: {result.ErrorMessage}");
        }

        ctx.SetTaskStatus("apply-settings", DeployTaskStatus.Completed, 100);
    }
}
