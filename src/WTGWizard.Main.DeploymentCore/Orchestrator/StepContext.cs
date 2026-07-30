using System;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.WorkerCore;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Main.DeploymentCore.Orchestrator;

/// <summary>
/// 部署步骤共享上下文 — 封装配置、日志、进度事件。
/// 无 UI 依赖，通过事件模式通知外部消费者更新 UI。
/// </summary>
public sealed class StepContext
{
    public DeploymentConfig Config { get; }
    public ILoggerService Logger { get; }
    public WorkerProcessManager WorkerManager { get; }

    public event Action<string, DeployTaskStatus, double>? OnTaskStatusChanged;
    public event Action<string, double>? OnTaskProgressChanged;
    public event Action? OnCurrentTaskFailed;

    public StepContext(DeploymentConfig config, ILoggerService logger)
    {
        Config = config;
        Logger = logger;
        WorkerManager = new WorkerProcessManager(logger);
    }

    public void SetTaskStatus(string id, DeployTaskStatus status, double progress = 0)
        => OnTaskStatusChanged?.Invoke(id, status, progress);

    public void UpdateTaskProgress(string id, double value)
        => OnTaskProgressChanged?.Invoke(id, Math.Clamp(value, 0, DeploymentConstants.ProgressMax));

    public void MarkCurrentTaskFailed()
        => OnCurrentTaskFailed?.Invoke();
}
