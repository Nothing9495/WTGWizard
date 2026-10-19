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
    private bool _disposed;
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

    /// <summary>
    /// 发布进度更新。步骤已结束（Dispose 之后）时静默丢弃：
    /// Worker 被中断/超时终止后仍可能有迟到回调，不应让 Rx 已释放的 Subject 抛异常。
    /// Note: 静默丢弃可能会导致由此处代码引发的问题不可见。有待后续观察
    /// </summary>
    public void Publish(TaskUpdate update)
    {
        if (_disposed) return;
        _subject.OnNext(update);
    }

    public string SaveTempScript(string fileName, string content)
        => _tempFiles.SaveScript(fileName, content);

    public Task<WorkerExecutionResult> ExecuteWorkerAsync(
        WorkerCommand command, IProgress<double>? progress = null, CancellationToken ct = default)
        => _worker.ExecuteAsync(command, progress, ct);

    public void Dispose()
    {
        _disposed = true;
        _subject.OnCompleted();
        _subject.Dispose();
    }
}
