using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Orchestrator;

/// <summary>
/// 步骤执行基类 — 任务终态（Completed/Failed/Cancelled）的唯一发布者。
/// 子类只发布 Running/进度，不发布终态，保证任务卡状态与部署语义一致。
/// </summary>
public abstract class DeploymentStepBase : Contracts.IDeploymentStep
{
    public abstract DeployTaskId TaskId { get; }
    public abstract string TitleKey { get; }
    public abstract string DescriptionKey { get; }
    public abstract bool ShouldRun(DeploymentConfig config);

    protected abstract Task<StepResult> ExecuteCoreAsync(Contracts.IStepContext ctx, CancellationToken ct);

    public async Task<StepResult> ExecuteAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Running, 0));
        try
        {
            var result = await ExecuteCoreAsync(ctx, ct);

            // 终态唯一发布：已完成优先（无论是否请求过取消），未完成 + 取消 → Cancelled
            if (result.IsSuccess)
            {
                ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Completed, 100));
            }
            else if (ct.IsCancellationRequested)
            {
                ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Cancelled, 0));
            }
            else if (result.NonFatal)
            {
                ctx.Logger.Warn("Step", "Non-fatal failure in {TaskId}: {Error}", TaskId.Value, result.ErrorMessage);
                ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Completed, 100));
            }
            else
            {
                ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            // 取消不属于任务失败：标记后重抛，由 Orchestrator 转为 Cancelled 结局
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Cancelled, 0));
            throw;
        }
        catch (Exception ex)
        {
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Fail(ex.Message);
        }
    }
}
