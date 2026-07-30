using System.Collections.Generic;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Contracts;

public interface IDeploymentPipeline
{
    IReadOnlyList<IDeploymentStep> Steps { get; }
    IEnumerable<DeployTaskId> ActiveTasks(DeploymentConfig config);
}
