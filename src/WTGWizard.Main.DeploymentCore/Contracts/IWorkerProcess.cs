using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Contracts;

public interface IWorkerProcess
{
    Task<WorkerExecutionResult> ExecuteAsync(
        WorkerCommand command, IProgress<double>? progress = null, CancellationToken ct = default);
}
