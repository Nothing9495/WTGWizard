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
            // IsActive 在 StepContext.Dispose 时失效（覆盖成功/失败/超时/取消全部路径），
            // 用它把步骤结束后的迟到回调挡在 UI 线程队列之外。
            var progress = new GatedProgress<double>(
                new Progress<double>(p => ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Running, p))),
                () => ctx.IsActive);
            var result = await ctx.ExecuteWorkerAsync(cmd, progress, ct);
            if (!result.Success)
                return StepResult.Fail(result.ErrorMessage ?? "Extract failed");
        }

        return StepResult.Ok();
    }

    private static string EscapeArg(string v) => v.Replace("\"", "\\\"");
}
