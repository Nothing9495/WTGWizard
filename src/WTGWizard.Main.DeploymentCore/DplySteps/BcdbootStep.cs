using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Main.DeploymentCore.WorkerCore;

namespace WTGWizard.Main.DeploymentCore.DplySteps;

/// <summary>
/// 步骤 6：BCDBoot 启动配置。
/// </summary>
public sealed class BcdbootStep : IDeploymentStep
{
    public string TaskId => "create-boot";
    public bool ShouldRun(DeploymentConfig config) => true;

    public async Task ExecuteAsync(StepContext ctx, string? osApplyDir, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(osApplyDir))
            throw new InvalidOperationException("osApplyDir is null — partition step may not have run");

        ctx.SetTaskStatus("create-boot", DeployTaskStatus.Running);

        string bcdArgs = CommandBuilder.BuildBcdbootArgs(
            osApplyDir, ctx.Config.EspDriveLetter,
            ctx.Config.EnableBootEx, ctx.Config.EnableBootVerbose);

        ctx.Logger.Debug("Bcdboot", "Args: {Args}", bcdArgs);

        var (cmd, args) = WorkerFactory.BuildBcdboot(bcdArgs, DeploymentConstants.TimeoutBcdbootMs);
        var result = await ctx.WorkerManager.ExecuteCommandAsync(cmd, args, ct: ct);

        if (!result.Success)
        {
            ctx.SetTaskStatus("create-boot", DeployTaskStatus.Failed);
            throw new InvalidOperationException($"BCDBoot failed: {result.ErrorMessage}");
        }

        ctx.SetTaskStatus("create-boot", DeployTaskStatus.Completed, 100);
    }
}
