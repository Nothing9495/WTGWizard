using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Main.DeploymentCore.Worker;
using static WTGWizard.Main.DeploymentCore.Models.DeploymentConstants;

namespace WTGWizard.Main.DeploymentCore.Steps;

public sealed class DriverStep : DeploymentStepBase
{
    public override DeployTaskId TaskId => DeployTaskId.IntegrateDrivers;
    public override string TitleKey => "Task.IntegrateDrivers.Title";
    public override string DescriptionKey => "Task.IntegrateDrivers.Desc";
    public override bool ShouldRun(DeploymentConfig config)
        => config.DriverIntegrationEnabled && !string.IsNullOrWhiteSpace(config.DriversDirectoryPath);

    protected override async Task<StepResult> ExecuteCoreAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.Config.OsDriveLetter.ToString()) || ctx.Config.OsDriveLetter == '\0')
            return StepResult.Fail("osApplyDir is not resolved — partition step may not have run");

        if (!Directory.Exists(ctx.Config.DriversDirectoryPath))
        {
            ctx.Logger.Warn("Driver", "Driver directory not found: {Path}", ctx.Config.DriversDirectoryPath);
            return StepResult.Fail($"Driver directory not found: {ctx.Config.DriversDirectoryPath}");
        }

        string? driverArgs = CommandBuilder.BuildAddDriverArgs(
            $"{ctx.Config.OsDriveLetter}:\\", ctx.Config.DriversDirectoryPath, ctx.Config.ForceUnsignedDriver);
        if (driverArgs is null)
            return StepResult.Ok();

        ctx.Logger.Debug("Driver", "Integrating drivers from: {Path}", ctx.Config.DriversDirectoryPath);

        var cmd = new WorkerCommand("dism", $"--args \"{EscapeArg(driverArgs)}\" --timeout {TimeoutDriverMs}");
        var result = await ctx.ExecuteWorkerAsync(cmd, ct: ct);

        if (!result.Success)
            return StepResult.Fail(result.ErrorMessage ?? "Driver integration failed");

        return StepResult.Ok();
    }

    private static string EscapeArg(string v) => v.Replace("\"", "\\\"");
}
