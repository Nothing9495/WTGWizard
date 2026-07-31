using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Worker;
using static WTGWizard.Main.DeploymentCore.Models.DeploymentConstants;

namespace WTGWizard.Main.DeploymentCore.Steps;

public sealed class BcdbootStep : Contracts.IDeploymentStep
{
    public DeployTaskId TaskId => DeployTaskId.CreateBootFiles;
    public string TitleKey => "Task.CreateBootFiles.Title";
    public string DescriptionKey => "Task.CreateBootFiles.Desc";
    public bool ShouldRun(DeploymentConfig config) => true;

    public async Task<StepResult> ExecuteAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Running, 0));

        if (string.IsNullOrWhiteSpace(ctx.Config.OsDriveLetter.ToString()) || ctx.Config.OsDriveLetter == '\0')
        {
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Fail("osApplyDir is not resolved — partition step may not have run");
        }

        string bcdArgs = CommandBuilder.BuildBcdbootArgs($"{ctx.Config.OsDriveLetter}:\\",
            ctx.Config.EspDriveLetter, ctx.Config.EnableBootEx, ctx.Config.EnableBootVerbose);

        ctx.Logger.Debug("Bcdboot", "Args: {Args}", bcdArgs);

        var cmd = new WorkerCommand("bcdboot", $"--args \"{EscapeArg(bcdArgs)}\" --timeout {TimeoutBcdbootMs}");
        var result = await ctx.ExecuteWorkerAsync(cmd, ct: ct);

        if (!result.Success)
        {
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Fail(result.ErrorMessage ?? "BCDBoot failed");
        }

        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Completed, 100));
        return StepResult.Ok();
    }

    private static string EscapeArg(string v) => v.Replace("\"", "\\\"");
}
