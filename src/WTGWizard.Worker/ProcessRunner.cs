using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace WTGWizard.Worker;

/// <summary>
/// 外部进程运行器 — 统一的进程启动逻辑。
/// </summary>
internal static class ProcessRunner
{
    /// <summary>
    /// 运行外部进程并返回退出码。
    /// </summary>
    /// <param name="fileName">可执行文件路径。</param>
    /// <param name="arguments">命令行参数。</param>
    /// <param name="timeoutMs">超时毫秒数，0 表示不超时。</param>
    /// <returns>进程退出码。</returns>
    public static int Run(string fileName, string arguments, int timeoutMs = 0)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
            EnableRaisingEvents = true,
        };

        using var exited = new ManualResetEventSlim(false);
        using var outputDone = new ManualResetEventSlim(false);
        var exitCode = -1;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) { outputDone.Set(); return; }
            Console.WriteLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Console.Error.WriteLine(e.Data);
        };

        process.Exited += (_, _) =>
        {
            process.WaitForExit();
            outputDone.Wait(TimeSpan.FromSeconds(1));
            exitCode = process.ExitCode;
            exited.Set();
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {fileName}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (timeoutMs > 0)
        {
            var timeoutTask = System.Threading.Tasks.Task.Delay(timeoutMs);
            var completedTask = System.Threading.Tasks.Task.WhenAny(exited.WaitHandle.AsTask(), timeoutTask).Result;

            if (completedTask == timeoutTask)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"Process timed out after {timeoutMs}ms: {fileName}");
            }
        }
        else
        {
            exited.Wait();
        }

        outputDone.Wait(TimeSpan.FromSeconds(2));

        return exitCode;
    }

    /// <summary>
    /// 将 WaitHandle 转换为 Task。
    /// </summary>
    private static System.Threading.Tasks.Task AsTask(this WaitHandle handle)
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        ThreadPool.RegisterWaitForSingleObject(
            handle,
            (state, _) => ((System.Threading.Tasks.TaskCompletionSource<bool>)state!).TrySetResult(true),
            tcs,
            Timeout.Infinite,
            executeOnlyOnce: true);
        return tcs.Task;
    }
}
