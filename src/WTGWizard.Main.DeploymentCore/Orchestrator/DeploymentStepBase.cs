using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Orchestrator;

public abstract class DeploymentStepBase : Contracts.IDeploymentStep
{
    public abstract DeployTaskId TaskId { get; }
    public abstract bool ShouldRun(DeploymentConfig config);

    protected abstract Task<StepResult> ExecuteCoreAsync(Contracts.IStepContext ctx, CancellationToken ct);

    public async Task<StepResult> ExecuteAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Running, 0));
        try
        {
            var result = await ExecuteCoreAsync(ctx, ct);
            ctx.Publish(result.IsSuccess
                ? new TaskUpdate(TaskId, DeployTaskStatus.Completed, 100)
                : new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return result;
        }
        catch (Exception ex)
        {
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Fail(ex.Message);
        }
    }
}
