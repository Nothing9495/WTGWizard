using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Shared.Common;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Main.DeploymentCore.WorkerCore;

/// <summary>
/// Worker 进程管理器 — 负责单个 Worker 进程的完整生命周期：
/// 启动进程、通过 PipeServer 接收进度消息、清理。
/// Worker 侧自控超时，Main 只等待 pipe 消息。
/// </summary>
public sealed class WorkerProcessManager
{
    private readonly ILoggerService _logger;

    public WorkerProcessManager(ILoggerService logger)
    {
        _logger = logger;
    }

    public async Task<WorkerExecutionResult> ExecuteCommandAsync(
        string command,
        string arguments,
        Action<double>? onProgress = null,
        CancellationToken ct = default)
    {
        string workerExePath = FindWorkerExe();
        string pipeName = PipeProtocol.GeneratePipeName();
        string workerArgs = $"{command} {arguments} --pipe {pipeName}";

        _logger.Debug("WorkerMgr", "Launching Worker: {Path} {Args}", workerExePath, workerArgs);

        using var pipeServer = new PipeServer(pipeName);

        var tcs = new TaskCompletionSource<WorkerExecutionResult>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        pipeServer.OnCompleted += (task, rc) =>
        {
            _logger.Debug("WorkerMgr", "Worker completed: {Task} exit={ExitCode}", task, rc);
            tcs.TrySetResult(WorkerExecutionResult.Ok(rc));
        };

        pipeServer.OnFailed += (task, rc, msg) =>
        {
            _logger.Error("WorkerMgr", "Worker failed: {Task} exit={ExitCode} msg={Msg}", task, rc, msg);
            tcs.TrySetResult(WorkerExecutionResult.Fail(rc, msg ?? $"Exit code: {rc}"));
        };

        pipeServer.OnDisconnected += () =>
        {
            if (!tcs.Task.IsCompleted)
            {
                _logger.Warn("WorkerMgr", "Pipe disconnected unexpectedly");
                tcs.TrySetResult(WorkerExecutionResult.Fail(-1, "Pipe disconnected unexpectedly"));
            }
        };

        pipeServer.OnProgress += (task, percent) => onProgress?.Invoke(percent);

        Process? process = null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = workerExePath,
                Arguments = workerArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            psi.Environment.Remove("DOTNET_ROOT");
            psi.Environment.Remove("DOTNET_ROOT(x86)");

            process = Process.Start(psi);
            if (process is null)
            {
                _logger.Error("WorkerMgr", "Failed to start Worker process");
                return WorkerExecutionResult.Fail(-1, "Failed to start Worker process");
            }

            await pipeServer.WaitForConnectionAsync(PipeProtocol.ConnectTimeoutMs, cts.Token);

            var processExitTask = process.WaitForExitAsync();
            var result = await tcs.Task;

            if (!process.HasExited)
            {
                await processExitTask;
            }

            _logger.Debug("WorkerMgr", "Process exited with code {ExitCode}", process.ExitCode);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("WorkerMgr", "Operation cancelled");
            KillProcessTree(process);
            return WorkerExecutionResult.Fail(-1, "Operation cancelled");
        }
        catch (TimeoutException ex)
        {
            _logger.Error("WorkerMgr", "Connection timeout: {Msg}", ex.Message);
            KillProcessTree(process);
            return WorkerExecutionResult.Fail(-1, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Error("WorkerMgr", "Unexpected error: {Msg}", ex.Message);
            KillProcessTree(process);
            return WorkerExecutionResult.Fail(-1, ex.Message);
        }
    }

    private static string FindWorkerExe()
    {
        string baseDir = AppContext.BaseDirectory;
        string workerPath = Path.Combine(baseDir, "WTGWizard.Worker.exe");
        if (File.Exists(workerPath))
            return workerPath;

        string parentDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
        string parentWorkerPath = Path.Combine(parentDir, "WTGWizard.Worker.exe");
        if (File.Exists(parentWorkerPath))
            return parentWorkerPath;

        throw new FileNotFoundException($"WTGWizard.Worker.exe not found in {baseDir} or {parentDir}");
    }

    private static void KillProcessTree(Process? process)
    {
        if (process is null || process.HasExited)
            return;

        try
        {
            process.Kill(true);
        }
        catch
        {
            // Best effort
        }
    }
}
