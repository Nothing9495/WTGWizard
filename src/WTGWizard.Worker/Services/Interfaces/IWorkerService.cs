using System.Threading;
using System.Threading.Tasks;

namespace WTGWizard.Worker.Services.Interfaces;

public interface IWorkerService
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
