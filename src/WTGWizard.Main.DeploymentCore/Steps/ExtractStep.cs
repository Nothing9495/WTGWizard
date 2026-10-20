using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Main.DeploymentCore.Worker;
using static WTGWizard.Main.DeploymentCore.Models.DeploymentConstants;

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

            // 设计上不设超时（TimeoutNone）：/Apply-Image 的耗时由映像大小与目标盘速度决定，
            // 固定上限可能导致在慢盘/大镜像上误杀一次实际成功的部署，由 Main 侧的「静默兜底」承担（心跳持续刷新，连续 30 分钟无任何消息才判死）。
            // 该无限等待成立的前提是 ProcessRunner 只等进程自身退出，不等 stdout EOF；
            // 否则 dism 派生的 DismHost 持句柄会把这条路径变成挂死
            var cmd = new WorkerCommand("dism", $"--args \"{EscapeArg(dismArgs)}\" --timeout {TimeoutNone}");
            var result = await ctx.ExecuteWorkerAsync(cmd, ct: ct);
            if (!result.Success)
                return StepResult.Fail(result.ErrorMessage ?? "Extract failed");
        }
        else
        {
            // 同样不设超时：ExtractCommand（wimlib）不接受 --timeout，展开耗时只与镜像大小和目标盘有关，
            // 活锁同样由 Main 侧静默兜底判定。
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
