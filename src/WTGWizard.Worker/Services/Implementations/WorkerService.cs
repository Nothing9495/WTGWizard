using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Worker.Services.Interfaces;

namespace WTGWizard.Worker.Services.Implementations;

public class WorkerService : IWorkerService
{
    private readonly ILogger<WorkerService> _logger;

    public WorkerService(ILogger<WorkerService> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Worker service executing");

        await Task.Delay(1000, cancellationToken);

        _logger.LogInformation("Worker service completed");
    }
}
