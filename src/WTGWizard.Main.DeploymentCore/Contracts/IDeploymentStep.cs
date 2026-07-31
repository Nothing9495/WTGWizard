using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Contracts;

public interface IDeploymentStep
{
    DeployTaskId TaskId { get; }
    string TitleKey { get; }
    string DescriptionKey { get; }
    bool ShouldRun(DeploymentConfig config);
    Task<StepResult> ExecuteAsync(IStepContext ctx, CancellationToken ct);
}
