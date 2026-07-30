using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Main.DeploymentCore.WorkerCore;

namespace WTGWizard.Main.DeploymentCore.DplySteps;

/// <summary>
/// 步骤 3：驱动集成（可选）。
/// </summary>
public sealed class DriverStep : IDeploymentStep
{
    public string TaskId => "drivers";
    public bool ShouldRun(DeploymentConfig config)
        => config.DriverIntegrationEnabled && !string.IsNullOrWhiteSpace(config.DriversDirectoryPath);

    public async Task ExecuteAsync(StepContext ctx, string? osApplyDir, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(osApplyDir))
            throw new InvalidOperationException("osApplyDir is null — partition step may not have run");

        ctx.SetTaskStatus("drivers", DeployTaskStatus.Running);

        if (!Directory.Exists(ctx.Config.DriversDirectoryPath))
        {
            ctx.Logger.Warn("Driver", "Driver directory not found: {Path}", ctx.Config.DriversDirectoryPath);
            ctx.SetTaskStatus("drivers", DeployTaskStatus.Failed);
            return;
        }

        string? driverArgs = CommandBuilder.BuildAddDriverArgs(
            osApplyDir, ctx.Config.DriversDirectoryPath, ctx.Config.ForceUnsignedDriver);

        if (driverArgs is null)
        {
            ctx.SetTaskStatus("drivers", DeployTaskStatus.Completed, 100);
            return;
        }

        ctx.Logger.Debug("Driver", "Integrating drivers from: {Path}", ctx.Config.DriversDirectoryPath);

        var (cmd, args) = WorkerFactory.BuildDism(driverArgs, DeploymentConstants.TimeoutDriverMs);
        var result = await ctx.WorkerManager.ExecuteCommandAsync(cmd, args, ct: ct);

        if (!result.Success)
        {
            ctx.SetTaskStatus("drivers", DeployTaskStatus.Failed);
            throw new InvalidOperationException($"Driver integration failed: {result.ErrorMessage}");
        }

        ctx.SetTaskStatus("drivers", DeployTaskStatus.Completed, 100);
    }
}
