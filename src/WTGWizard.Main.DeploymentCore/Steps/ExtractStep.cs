using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Main.DeploymentCore.Worker;

namespace WTGWizard.Main.DeploymentCore.Steps;

public sealed class ExtractStep : DeploymentStepBase
{
    public override DeployTaskId TaskId => DeployTaskId.ExtractImage;
    public override string TitleKey => "Task.ExtractImage.Title";
    public override string DescriptionKey => "Task.ExtractImage.Desc";
    public override bool ShouldRun(DeploymentConfig config) => true;

    protected override async Task<StepResult> ExecuteCoreAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.Config.OsDriveLetter.ToString()) || ctx.Config.OsDriveLetter == '\0')
            return StepResult.Fail("osApplyDir is not resolved — partition step may not have run");

        string osApplyDir = $"{ctx.Config.OsDriveLetter}:\\";
        ctx.Logger.Debug("Extract", "Extracting to: {Dir}", osApplyDir);

        if (ctx.Config.UseDismToDeploy)
        {
            string dismArgs = CommandBuilder.BuildApplyImageArgs(
                ctx.Config.SrcImageFile, ctx.Config.ImageSelectedIndex, osApplyDir);
            var cmd = new WorkerCommand("dism", $"--args \"{EscapeArg(dismArgs)}\"");
            var result = await ctx.ExecuteWorkerAsync(cmd, ct: ct);
            if (!result.Success)
                return StepResult.Fail(result.ErrorMessage ?? "Extract failed");
        }
        else
        {
            string osTarget = osApplyDir.TrimEnd('\\');
            var cmd = new WorkerCommand("extract",
                $"--wim \"{EscapeArg(ctx.Config.SrcImageFile)}\" --index {ctx.Config.ImageSelectedIndex} --target \"{EscapeArg(osTarget)}\"");
            var result = await ctx.ExecuteWorkerAsync(cmd,
                new Progress<double>(p => ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Running, p))), ct);
            if (!result.Success)
                return StepResult.Fail(result.ErrorMessage ?? "Extract failed");
        }

        return StepResult.Ok();
    }

    private static string EscapeArg(string v) => v.Replace("\"", "\\\"");
}
