using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Main.DeploymentCore.Worker;
using static WTGWizard.Main.DeploymentCore.Models.DeploymentConstants;

namespace WTGWizard.Main.DeploymentCore.Steps;

public sealed class BcdbootStep : DeploymentStepBase
{
    public override DeployTaskId TaskId => DeployTaskId.CreateBootFiles;
    public override string TitleKey => "Task.CreateBootFiles.Title";
    public override string DescriptionKey => "Task.CreateBootFiles.Desc";
    public override bool ShouldRun(DeploymentConfig config) => true;

    protected override async Task<StepResult> ExecuteCoreAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.Config.OsDriveLetter.ToString()) || ctx.Config.OsDriveLetter == '\0')
            return StepResult.Fail("osApplyDir is not resolved — partition step may not have run");

        string bcdArgs = CommandBuilder.BuildBcdbootArgs($"{ctx.Config.OsDriveLetter}:\\",
            ctx.Config.EspDriveLetter, ctx.Config.EnableBootEx, ctx.Config.EnableBootVerbose);

        ctx.Logger.Debug("Bcdboot", "Args: {Args}", bcdArgs);

        var cmd = new WorkerCommand("bcdboot", $"--args \"{EscapeArg(bcdArgs)}\" --timeout {TimeoutBcdbootMs}");
        var result = await ctx.ExecuteWorkerAsync(cmd, ct: ct);

        if (!result.Success)
            return StepResult.Fail(result.ErrorMessage ?? "BCDBoot failed");

        return StepResult.Ok();
    }

    private static string EscapeArg(string v) => v.Replace("\"", "\\\"");
}
