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

    public WorkerProcess(ILoggerService logger) => _logger = logger;

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
            _logger.Debug("WorkerMgr", "Worker completed: {Task} exit={ExitCode}", task, rc);
            // 防御：completed 语义仅 rc==0；非 0 按失败处理
            Settle(rc == 0
                ? WorkerExecutionResult.Ok(rc)
                : WorkerExecutionResult.Fail(rc, $"Worker reported completion with exit code {rc}"));
        };
        pipeServer.OnFailed += (task, rc, msg) =>
        {
            _logger.Error("WorkerMgr", "Worker failed: {Task} exit={ExitCode} msg={Msg}", task, rc, msg);
            Settle(WorkerExecutionResult.Fail(rc, msg ?? $"Exit code: {rc}"));
        };
        pipeServer.OnCancelled += () =>
        {
            _logger.Debug("WorkerMgr", "Worker reported cancellation");
            Settle(WorkerExecutionResult.Cancelled());
        };
        pipeServer.OnDisconnected += () =>
        {
            if (!tcs.Task.IsCompleted)
            {
                // 取消请求后的断开视为取消（消除事件竞态）；否则为意外断开
                _logger.Warn("WorkerMgr", "Pipe disconnected (cancelRequested={Cancel})", _cancelRequested);
                Settle(_cancelRequested
                    ? WorkerExecutionResult.Cancelled()
                    : WorkerExecutionResult.Fail(-1, "Pipe disconnected unexpectedly"));
            }
        };
        pipeServer.OnProgress += (_, p) => progress?.Report(p);

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

            await pipeServer.WaitForConnectionAsync(PipeProtocol.ConnectTimeoutMs, cts.Token);
            _logger.Debug("WorkerMgr", "Pipe connected, read loop started: {Pipe}", pipeName);

            // 三次握手：等待 Worker 回报 ack（15s 超时）
            await pipeServer.WaitHandshakeAsync(PipeProtocol.ConnectTimeoutMs, cts.Token);
            _logger.Debug("WorkerMgr", "Handshake complete: {Pipe}", pipeName);

            // Worker 回报取消确认（task_cancelled 专用上报）→ 任务取消完成
            pipeServer.OnCancel += () => Settle(WorkerExecutionResult.Cancelled());

            var processExitTask = process.WaitForExitAsync();

            // 无限等待 Worker 回报（正常完成/失败/取消/断开）——软取消时当前任务自然跑完
            var result = await tcs.Task;

            if (!process.HasExited)
                await processExitTask;

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
            _logger.Error("WorkerMgr", "Connection timeout: {Msg}", ex.ToString());
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
            TerminalOutputBuffer.Shared.AppendBlankLine();
        }
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
                await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
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

    private static void KillProcessTree(Process? process)
    {
        if (process is null || process.HasExited) return;
        try { process.Kill(true); } catch { /* best effort */ }
    }

    public void Dispose() { }
}
