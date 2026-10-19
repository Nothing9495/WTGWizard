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

    /// <summary>
    /// 步骤是否仍在进行中。上下文的 Dispose 一旦执行即为 false。
    /// 进度回调应先用它短路，避免步骤结束后仍向 UI 线程队列投递无意义的续体。
    /// 它覆盖**所有**退出路径（成功 / 失败 / 取消 / 握手与终态超时），
    /// 而步骤自身的 CancellationToken 只在 Abort 时才取消，覆盖不到失败与超时路径。
    /// </summary>
    bool IsActive { get; }
    string SaveTempScript(string fileName, string content);
    Task<WorkerExecutionResult> ExecuteWorkerAsync(
        WorkerCommand command, IProgress<double>? progress = null, CancellationToken ct = default);
}
