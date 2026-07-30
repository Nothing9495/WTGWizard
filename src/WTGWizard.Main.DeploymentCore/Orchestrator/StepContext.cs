using System;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Builders;
using WTGWizard.Main.DeploymentCore.Contracts;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Worker;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Main.DeploymentCore.Orchestrator;

public sealed class StepContext : IStepContext, IDisposable
{
    private readonly Subject<TaskUpdate> _subject = new();
    public DeploymentConfig Config { get; }
    public ILoggerService Logger { get; }
    public WorkerCommandFactory Commands { get; }
    public IObservable<TaskUpdate> Progress => _subject;
    private readonly IWorkerProcess _worker;
    private readonly TempFileManager _tempFiles;

    public StepContext(DeploymentConfig config, IWorkerProcess worker,
        WorkerCommandFactory commands, TempFileManager tempFiles, ILoggerService logger)
    {
        Config = config;
        _worker = worker;
        Commands = commands;
        _tempFiles = tempFiles;
        Logger = logger;
    }

    public void Publish(TaskUpdate update) => _subject.OnNext(update);

    public string SaveTempScript(string fileName, string content)
        => _tempFiles.SaveScript(fileName, content);

    public Task<WorkerExecutionResult> ExecuteWorkerAsync(
        WorkerCommand command, IProgress<double>? progress = null, CancellationToken ct = default)
        => _worker.ExecuteAsync(command, progress, ct);

    public void Dispose()
    {
        _subject.OnCompleted();
        _subject.Dispose();
    }
}
