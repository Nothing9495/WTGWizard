using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Worker;
using static WTGWizard.Main.DeploymentCore.Models.DeploymentConstants;

namespace WTGWizard.Main.DeploymentCore.Steps;

public sealed class DriverStep : Contracts.IDeploymentStep
{
    public DeployTaskId TaskId => DeployTaskId.Drivers;
    public bool ShouldRun(DeploymentConfig config)
        => config.DriverIntegrationEnabled && !string.IsNullOrWhiteSpace(config.DriversDirectoryPath);

    public async Task<StepResult> ExecuteAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Running, 0));

        if (string.IsNullOrWhiteSpace(ctx.Config.OsDriveLetter.ToString()) || ctx.Config.OsDriveLetter == '\0')
        {
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Fail("osApplyDir is not resolved — partition step may not have run");
        }

        if (!Directory.Exists(ctx.Config.DriversDirectoryPath))
        {
            ctx.Logger.Warn("Driver", "Driver directory not found: {Path}", ctx.Config.DriversDirectoryPath);
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Fail($"Driver directory not found: {ctx.Config.DriversDirectoryPath}");
        }

        string? driverArgs = CommandBuilder.BuildAddDriverArgs(
            $"{ctx.Config.OsDriveLetter}:\\", ctx.Config.DriversDirectoryPath, ctx.Config.ForceUnsignedDriver);
        if (driverArgs is null)
        {
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Completed, 100));
            return StepResult.Ok();
        }

        ctx.Logger.Debug("Driver", "Integrating drivers from: {Path}", ctx.Config.DriversDirectoryPath);

        var cmd = new WorkerCommand("dism", $"--args \"{EscapeArg(driverArgs)}\" --timeout {TimeoutDriverMs}");
        var result = await ctx.ExecuteWorkerAsync(cmd, ct: ct);

        if (!result.Success)
        {
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Fail(result.ErrorMessage ?? "Driver integration failed");
        }

        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Completed, 100));
        return StepResult.Ok();
    }

    private static string EscapeArg(string v) => v.Replace("\"", "\\\"");
}
