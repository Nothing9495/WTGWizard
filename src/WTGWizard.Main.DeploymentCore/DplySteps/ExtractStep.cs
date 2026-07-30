using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Main.DeploymentCore.WorkerCore;

namespace WTGWizard.Main.DeploymentCore.DplySteps;

/// <summary>
/// 步骤 2：WIM 映像提取。
/// </summary>
public sealed class ExtractStep : IDeploymentStep
{
    public string TaskId => "extract";
    public bool ShouldRun(DeploymentConfig config) => true;

    public async Task ExecuteAsync(StepContext ctx, string? osApplyDir, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(osApplyDir))
            throw new InvalidOperationException("osApplyDir is null — partition step may not have run");

        ctx.SetTaskStatus("extract", DeployTaskStatus.Running);

        ctx.Logger.Debug("Extract", "Extracting to: {Dir}", osApplyDir);

        if (ctx.Config.UseDismToDeploy)
        {
            string dismArgs = CommandBuilder.BuildApplyImageArgs(
                ctx.Config.SrcImageFile, ctx.Config.ImageSelectedIndex, osApplyDir);

            var (cmd, args) = WorkerFactory.BuildDism(dismArgs);
            var result = await ctx.WorkerManager.ExecuteCommandAsync(cmd, args, ct: ct);

            if (!result.Success)
            {
                ctx.SetTaskStatus("extract", DeployTaskStatus.Failed);
                throw new InvalidOperationException($"Extract failed: {result.ErrorMessage}");
            }
        }
        else
        {
            string osTarget = osApplyDir.TrimEnd('\\');
            var (cmd, args) = WorkerFactory.BuildExtract(
                ctx.Config.SrcImageFile, ctx.Config.ImageSelectedIndex, osTarget);

            var result = await ctx.WorkerManager.ExecuteCommandAsync(cmd, args,
                onProgress: p => ctx.UpdateTaskProgress("extract", p), ct: ct);

            if (!result.Success)
            {
                ctx.SetTaskStatus("extract", DeployTaskStatus.Failed);
                throw new InvalidOperationException($"Extract failed: {result.ErrorMessage}");
            }
        }

        ctx.SetTaskStatus("extract", DeployTaskStatus.Completed, 100);
    }
}
