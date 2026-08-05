using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Contracts;

public interface IWorkerProcess : IDisposable
{
    Task<WorkerExecutionResult> ExecuteAsync(
        WorkerCommand command, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 硬中断当前任务（仅关闭流程调用）— 请求 Worker 主动终止当前任务，
    /// 15s 未回报则强杀进程树。软取消（AbortButton/新部署覆盖）不应调用此方法。
    /// </summary>
    void ForceCancelCurrentTask();
}
