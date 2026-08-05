using System;
using WTGWizard.Shared.Common;

namespace WTGWizard.Worker;

/// <summary>
/// Pipe 连接辅助类。
/// </summary>
internal static class PipeHelper
{
    /// <summary>
    /// 连接到主进程的 NamedPipe 并完成三次握手，15 秒超时自动退出。
    /// 握手流程：等待 Main 的 ready → 回报 ack → 返回（任务消息通道就绪）。
    /// </summary>
    /// <param name="pipeName">Pipe 名称。</param>
    /// <returns>PipeWriter 实例。</returns>
    public static PipeWriter Connect(string pipeName)
    {
        var pipe = new PipeWriter();
        try
        {
            WorkerDebug.Write($"Pipe: connecting to {pipeName}");
            pipe.DebugLog = WorkerDebug.Write;
            pipe.Connect(pipeName, timeoutMs: PipeProtocol.ConnectTimeoutMs);

            // 写入取消令牌（阻塞 Write 可被取消中断）+ 主进程取消指令接线
            pipe.SetCancellationToken(WorkerCancellation.Token);
            pipe.OnCancelRequested += () => WorkerCancellation.Cts.Cancel();

            // 三次握手：等待 Main 的 ready，回报 ack
            if (!pipe.WaitForReady(PipeProtocol.ConnectTimeoutMs))
            {
                Console.Error.WriteLine("Handshake failed: no ready from Main within 15 seconds, exiting.");
                Environment.Exit(-1);
            }
            pipe.WriteAck();
            WorkerDebug.Write("Pipe: handshake complete (ready received, ack sent)");

            return pipe;
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("Failed to connect to Pipe within 15 seconds, exiting.");
            Environment.Exit(-1);
            throw;
        }
    }
}
