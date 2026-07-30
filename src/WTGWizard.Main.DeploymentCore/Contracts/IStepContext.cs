using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Main.DeploymentCore.Contracts;

public interface IStepContext
{
    DeploymentConfig Config { get; }
    ILoggerService Logger { get; }
    void Publish(TaskUpdate update);
    string SaveTempScript(string fileName, string content);
    Task<WorkerExecutionResult> ExecuteWorkerAsync(
        WorkerCommand command, IProgress<double>? progress = null, CancellationToken ct = default);
}
