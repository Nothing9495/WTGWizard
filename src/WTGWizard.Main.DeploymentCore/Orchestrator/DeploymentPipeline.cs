using System.Collections.Generic;
using System.Linq;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Orchestrator;

public sealed class DeploymentPipeline : Contracts.IDeploymentPipeline
{
    private readonly List<Contracts.IDeploymentStep> _steps = new();

    public IReadOnlyList<Contracts.IDeploymentStep> Steps => _steps.AsReadOnly();

    public DeploymentPipeline AddStep<T>() where T : Contracts.IDeploymentStep, new()
    {
        _steps.Add(new T());
        return this;
    }

    public IEnumerable<DeployTaskId> ActiveTasks(DeploymentConfig config)
        => _steps.Where(s => s.ShouldRun(config)).Select(s => s.TaskId);
}
