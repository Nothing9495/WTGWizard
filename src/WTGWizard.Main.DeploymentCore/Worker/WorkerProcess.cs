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

            var processExitTask = process.WaitForExitAsync();
            var result = await tcs.Task;

            if (!process.HasExited)
                await processExitTask;

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
        finally
        {
            TerminalOutputBuffer.Shared.AppendBlankLine();
        }
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
