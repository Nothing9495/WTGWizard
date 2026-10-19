using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Contracts;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Shared.Common;
using WTGWizard.Shared.Services;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Main.DeploymentCore.Worker;

public sealed class WorkerProcess : IWorkerProcess, IDisposable
{
    private readonly ILoggerService _logger;

    // 活动任务引用（供 ForceCancelCurrentTask 硬中断）
    private Process? _activeProcess;
    private PipeServer? _activeServer;
    private TaskCompletionSource<WorkerExecutionResult>? _activeTcs;
    private bool _cancelRequested;
    private bool _ended;

    // 最后一次收到 Worker 消息的时间戳（Environment.TickCount64）——终态静默兜底的计时基准
    private long _lastReportTimestamp;

    public WorkerProcess(ILoggerService logger) => _logger = logger;

    /// <summary>刷新「Worker 仍在说话」的时间戳。ExecuteAsync 串行调用，无需额外同步。</summary>
    private void MarkReportReceived() => Interlocked.Exchange(ref _lastReportTimestamp, Environment.TickCount64);

    public async Task<WorkerExecutionResult> ExecuteAsync(
        WorkerCommand command, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string exePath = FindExe();
        string pipeName = PipeProtocol.GeneratePipeName();
        string workerArgs = $"{command.Command} {command.Arguments} --pipe {pipeName}";
        if (WorkerSettings.EnableDebugOutput)
            workerArgs += " --debug";

        _logger.Debug("WorkerMgr", "Launching Worker: {Path} {Args}", exePath, workerArgs);

        using var pipeServer = new PipeServer(pipeName);
        var tcs = new TaskCompletionSource<WorkerExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _cancelRequested = false;
        _ended = false;
        MarkReportReceived();

        // 软取消（ct）：标记取消请求；执行阶段 Worker 自然跑完，结局由部署层判定
        using var cancelReg = ct.Register(() => _cancelRequested = true);

        bool Settle(WorkerExecutionResult result)
        {
            if (_ended) return false;
            _ended = true;
            tcs.TrySetResult(result);
            return true;
        }

        pipeServer.OnCompleted += (task, rc) =>
        {
            MarkReportReceived();
            _logger.Debug("WorkerMgr", "Worker completed: {Task} exit={ExitCode}", task, rc);
            // 防御：completed 语义仅 rc==0；非 0 按失败处理
            Settle(rc == 0
                ? WorkerExecutionResult.Ok(rc)
                : WorkerExecutionResult.Fail(rc, $"Worker reported completion with exit code {rc}"));
        };
        pipeServer.OnFailed += (task, rc, msg) =>
        {
            MarkReportReceived();
            _logger.Error("WorkerMgr", "Worker failed: {Task} exit={ExitCode} msg={Msg}", task, rc, msg);
            Settle(WorkerExecutionResult.Fail(rc, msg ?? $"Exit code: {rc}"));
        };
        pipeServer.OnCancelled += () =>
        {
            MarkReportReceived();
            _logger.Debug("WorkerMgr", "Worker reported cancellation");
            Settle(WorkerExecutionResult.Cancelled());
        };
        pipeServer.OnDisconnected += () =>
        {
            if (!tcs.Task.IsCompleted)
            {
                MarkReportReceived();
                // 取消请求后的断开视为取消（消除事件竞态）；否则为意外断开
                _logger.Warn("WorkerMgr", "Pipe disconnected (cancelRequested={Cancel})", _cancelRequested);
                Settle(_cancelRequested
                    ? WorkerExecutionResult.Cancelled()
                    : WorkerExecutionResult.Fail(-1, "Pipe disconnected unexpectedly"));
            }
        };
        pipeServer.OnProgress += (_, p) =>
        {
            MarkReportReceived();
            progress?.Report(p);
        };

        Process? process = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath, Arguments = workerArgs,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment.Remove("DOTNET_ROOT");
            psi.Environment.Remove("DOTNET_ROOT(x86)");

            process = Process.Start(psi);
            if (process is null)
            {
                _logger.Error("WorkerMgr", "Failed to start Worker process");
                return WorkerExecutionResult.Fail(-1, "Failed to start Worker process");
            }

            _activeProcess = process;
            _activeServer = pipeServer;
            _activeTcs = tcs;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) TerminalOutputBuffer.Shared.Append(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) TerminalOutputBuffer.Shared.Append($"[ERR] {e.Data}");
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 以下四处管道/进程簿记一律 ConfigureAwait(false)：UI 线程繁忙不应拖慢握手与终态等待。
            // 方法体内只触及日志、Process、TCS 与 TerminalOutputBuffer（均线程安全），
            // 不触碰任何 UI 对象；返回后的调用方（StepContext → Orchestrator）仍回到 UI 上下文。
            await pipeServer.WaitForConnectionAsync(PipeProtocol.ConnectTimeoutMs, cts.Token).ConfigureAwait(false);
            _logger.Debug("WorkerMgr", "Pipe connected, read loop started: {Pipe}", pipeName);

            // 三次握手：等待 Worker 回报 ack（15s 超时）
            await pipeServer.WaitHandshakeAsync(PipeProtocol.ConnectTimeoutMs, cts.Token).ConfigureAwait(false);
            _logger.Debug("WorkerMgr", "Handshake complete: {Pipe}", pipeName);

            // Worker 回报取消确认（task_cancelled 专用上报）→ 任务取消完成
            pipeServer.OnCancel += () => Settle(WorkerExecutionResult.Cancelled());

            var processExitTask = process.WaitForExitAsync();

            // 无限等待 Worker 回报（正常完成/失败/取消/断开）——软取消时当前任务自然跑完。
            // 叠加「静默兜底」：Worker 长时间不再发任何消息时判定终态不可达，转为失败而非无限挂起。
            var result = await AwaitReportAsync(tcs.Task).ConfigureAwait(false);

            if (!process.HasExited)
                await processExitTask.ConfigureAwait(false);

            // 兜底：消息全丢的极端场景下按进程退出码裁定（消息与进程退出双通道交叉验证）
            if (!_ended)
            {
                _logger.Warn("WorkerMgr", "No pipe message received; fallback to process exit code {ExitCode}", process.ExitCode);
                if (Settle(process.ExitCode == 0
                        ? WorkerExecutionResult.Ok(process.ExitCode)
                        : WorkerExecutionResult.Fail(process.ExitCode, $"Worker exited with code {process.ExitCode}")))
                {
                    result = process.ExitCode == 0
                        ? WorkerExecutionResult.Ok(process.ExitCode)
                        : WorkerExecutionResult.Fail(process.ExitCode, $"Worker exited with code {process.ExitCode}");
                }
            }

            _logger.Debug("WorkerMgr", "Process exited with code {ExitCode}", process.ExitCode);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("WorkerMgr", "Operation cancelled");
            KillProcessTree(process);
            return WorkerExecutionResult.Cancelled();
        }
        catch (TimeoutException ex)
        {
            // 连接 / 握手 / 终态静默兜底三种超时共用同一失败路径；异常自带可读原因
            _logger.Error("WorkerMgr", "Worker timeout: {Msg}", ex.ToString());
            KillProcessTree(process);
            return WorkerExecutionResult.Fail(-1, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Error("WorkerMgr", "Unexpected error: {Msg}", ex.ToString());
            KillProcessTree(process);
            return WorkerExecutionResult.Fail(-1, ex.Message);
        }
        finally
        {
            _activeProcess = null;
            _activeServer = null;
            _activeTcs = null;
            process?.Dispose();
            TerminalOutputBuffer.Shared.AppendBlankLine();
        }
    }

    /// <summary>
    /// 等待 Worker 回报终态，并施加「静默兜底」：若 Worker 连续 <see cref="PipeProtocol.ReportIdleTimeoutMs"/>
    /// 未再发送任何消息（管道写失败后 _connected=false、或进程假死），抛出 TimeoutException 走统一失败路径
    /// （杀进程树 + 记日志 + 返回失败），而不是让部署无限挂起到用户手动关闭窗口。
    /// 窗口由每一条来自 Worker 的消息刷新，因此长耗时任务（提取大镜像）不受影响。
    /// </summary>
    private async Task<WorkerExecutionResult> AwaitReportAsync(Task<WorkerExecutionResult> reportTask)
    {
        const int pollMs = 1000;

        while (!reportTask.IsCompleted)
        {
            // ConfigureAwait(false)：这是纯等待循环，不应每秒往 UI 线程投递续体
            await Task.Delay(pollMs).ConfigureAwait(false);
            if (reportTask.IsCompleted) break;

            long idleMs = Environment.TickCount64 - Volatile.Read(ref _lastReportTimestamp);
            if (idleMs >= PipeProtocol.ReportIdleTimeoutMs)
            {
                throw new TimeoutException(
                    $"Worker reported nothing for {idleMs / 1000}s (terminal message lost or Worker hung).");
            }
        }

        return await reportTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 硬中断当前任务（仅关闭流程调用）— 请求 Worker 主动终止当前任务，
    /// 15s 未回报则强杀进程树。软取消（AbortButton/新部署覆盖）不应调用此方法。
    /// </summary>
    public void ForceCancelCurrentTask()
    {
        _cancelRequested = true;
        _activeServer?.SendCancel();

        var proc = _activeProcess;
        var tcs = _activeTcs;
        if (proc is null || tcs is null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.Error("WorkerMgr", "Worker did not exit after cancellation request, killing process tree");
                KillProcessTree(proc);
            }
        });
    }

    private static string FindExe()
    {
        string baseDir = AppContext.BaseDirectory;
        string path = Path.Combine(baseDir, "WTGWizard.Worker.exe");
        if (File.Exists(path)) return path;
        path = Path.Combine(Path.GetFullPath(Path.Combine(baseDir, "..")), "WTGWizard.Worker.exe");
        if (File.Exists(path)) return path;
        throw new FileNotFoundException("WTGWizard.Worker.exe not found");
    }

    /// <summary>
    /// 终止 Worker 进程树。降级策略：整树 → 单进程 → 记录 Error 并输出 PID 供人工处置。
    /// 原实现是 `catch { }` 完全静默，导致「超时后 Worker 仍在跑」这类问题在日志里毫无线索。
    /// </summary>
    private void KillProcessTree(Process? process)
    {
        if (process is null) return;

        int pid = SafeGetPid(process);
        try
        {
            if (process.HasExited) return;
            _logger.Warn("WorkerMgr", "Killing Worker process tree, PID={Pid}", pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.Error("WorkerMgr", "Failed to kill Worker process tree, PID={Pid} ({Msg}) — falling back to single process.", pid, ex.Message);
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch (Exception ex2)
            {
                _logger.Error("WorkerMgr", "Failed to kill Worker PID={Pid} ({Msg}). It may still be running; terminate it manually.", pid, ex2.Message);
            }
        }
    }

    private static int SafeGetPid(Process process)
    {
        try { return process.Id; }
        catch { return -1; }
    }

    public void Dispose() { }
}
