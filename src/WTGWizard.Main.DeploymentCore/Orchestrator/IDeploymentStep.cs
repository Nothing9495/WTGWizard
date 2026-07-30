using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Orchestrator;

/// <summary>
/// 部署步骤接口 — 每个步骤独立实现，内聚自己的执行逻辑。
/// </summary>
public interface IDeploymentStep
{
    string TaskId { get; }
    bool ShouldRun(DeploymentConfig config);
    Task ExecuteAsync(StepContext ctx, string? osApplyDir, CancellationToken ct);
}
