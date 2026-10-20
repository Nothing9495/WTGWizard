using System;
using System.Diagnostics;
using System.Threading;
using WTGWizard.Worker.Encoding;

namespace WTGWizard.Worker;

/// <summary>
/// 外部进程运行器 — 统一的进程启动逻辑。
/// 输出解码编码由 EncodingResolver 按程序名解析。
/// </summary>
/// <remarks>
/// 退出语义：<b>只以进程自身退出为判据</b>，不等 stdout/stderr 的 EOF。无参 <c>WaitForExit()</c>
/// 与 <c>WaitForExitAsync()</c> 都会额外等 EOF，而 EOF 可能被继承了输出句柄的孙进程
/// （如 dism → DismHost）无限期推迟，使已成功退出的进程被判超时。
/// <para>
/// 退出判定不得交给 <c>Exited</c> 回调：回调在线程池上，生命周期可能长于本方法，触碰已释放对象
/// 会抛异常并直接终止进程（见 docs/.oc/01-incident-processrunner.md）。
/// </para>
/// </remarks>
internal static class ProcessRunner
{
    /// <summary>输出排空上限；超时按上限放弃，不影响退出码与超时判定。</summary>
    private const int DrainTimeoutMs = 2_000;

    /// <summary>Kill 后等待进程真正退出的上限。</summary>
    private const int KillSettleTimeoutMs = 2_000;

    /// <summary>「不设超时」的等待值。不能用 <c>Timeout.Infinite</c>——它等价于无参重载，会等输出 EOF。</summary>
    private const int NoTimeoutWaitMs = int.MaxValue;

    /// <summary>
    /// 运行外部进程并返回退出码。
    /// </summary>
    /// <param name="fileName">可执行文件路径。</param>
    /// <param name="arguments">命令行参数。</param>
    /// <param name="timeoutMs">超时毫秒数；<c>&lt;= 0</c> 表示不设超时（只等进程退出，仍不等 EOF）。</param>
    /// <param name="ct">取消令牌：取消时终止整个进程树并抛 OperationCanceledException。</param>
    /// <returns>进程退出码。</returns>
    /// <exception cref="TimeoutException">进程在超时内未退出（已杀进程树）；EOF 被推迟不会触发。</exception>
    public static int Run(string fileName, string arguments, int timeoutMs = 0, CancellationToken ct = default)
    {
        var encoding = EncodingResolver.Resolve(fileName);
        WorkerDebug.Write($"Process: file={fileName}, args={arguments}, timeout={WorkerDebug.TimeoutLabel(timeoutMs)}, encoding={encoding.WebName}");

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
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding,
            },
        };

        // 排空信号：仅表示异步读取已到 EOF。不释放。
        // EOF 可能永远不到，迟到的 Set() 会直接杀进程。
        var stdoutDone = new ManualResetEventSlim(false);
        var stderrDone = new ManualResetEventSlim(false);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) { stdoutDone.Set(); return; }
            Console.WriteLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) { stderrDone.Set(); return; }
            Console.Error.WriteLine(e.Data);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {fileName}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 取消：杀进程树；Kill 失败不得覆盖取消语义。
        using var cancelReg = ct.Register(() =>
        {
            try
            {
                WorkerDebug.Write($"Process cancelled, killing tree: {fileName}");
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                WorkerDebug.Write($"Process kill on cancel failed: {ex.GetType().Name}: {ex.Message}");
            }
        });

        int waitMs = timeoutMs > 0 ? timeoutMs : NoTimeoutWaitMs;

        // 只等进程自身退出（不等 EOF，见类型注释）。
        if (!process.WaitForExit(waitMs))
            {
                WorkerDebug.Write($"Process timed out after {timeoutMs}ms: {fileName}");
            KillTree(fileName, process);
            DrainOutput(stdoutDone, stderrDone);   // 失败现场的最后几行同样值得显示

            // 取消与超时竞态：取消优先（与编排器一致）
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

                throw new TimeoutException($"Process timed out after {timeoutMs}ms: {fileName}");
        }

        ct.ThrowIfCancellationRequested();

        // 进程已退出 → 退出码必然可用（与输出是否排空无关）
        int exitCode = process.ExitCode;

        DrainOutput(stdoutDone, stderrDone);

        WorkerDebug.Write($"Process exited: {fileName}, code={exitCode}");
        return exitCode;
    }

    /// <summary>有界排空：stdout/stderr 共用截止时间，最多等 <see cref="DrainTimeoutMs"/>。</summary>
    private static void DrainOutput(ManualResetEventSlim stdoutDone, ManualResetEventSlim stderrDone)
    {
        long deadline = Environment.TickCount64 + DrainTimeoutMs;
        WaitRemaining(stdoutDone, deadline);
        WaitRemaining(stderrDone, deadline);
    }

    private static void WaitRemaining(ManualResetEventSlim signal, long deadline)
    {
        long remaining = deadline - Environment.TickCount64;
        if (remaining <= 0) return;

        signal.Wait(TimeSpan.FromMilliseconds(remaining));
    }

    /// <summary>杀进程树并短暂等待退出；Kill 失败只记录，不得顶掉调用方要抛的超时/取消异常。</summary>
    private static void KillTree(string fileName, Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            WorkerDebug.Write($"Process killed: {fileName}");
        }
        catch (Exception ex)
        {
            WorkerDebug.Write($"Process kill failed: {ex.GetType().Name}: {ex.Message}");
        }

        try { process.WaitForExit(KillSettleTimeoutMs); }
        catch (Exception ex) { WorkerDebug.Write($"Process wait after kill failed: {ex.GetType().Name}"); }
    }
}
