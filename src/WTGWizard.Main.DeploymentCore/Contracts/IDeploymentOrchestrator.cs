using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Contracts;

public interface IDeploymentOrchestrator : IDisposable
{
    IObservable<TaskUpdate> Progress { get; }
    Task<DeploymentResult> StartAsync(CancellationToken ct = default);
}
