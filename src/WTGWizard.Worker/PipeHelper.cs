using System;
using WTGWizard.Shared.Common;

namespace WTGWizard.Worker;

/// <summary>
/// Pipe 连接辅助类。
/// </summary>
internal static class PipeHelper
{
    /// <summary>
    /// 连接到主进程的 NamedPipe，15 秒超时自动退出。
    /// </summary>
    /// <param name="pipeName">Pipe 名称。</param>
    /// <returns>PipeWriter 实例。</returns>
    public static PipeWriter Connect(string pipeName)
    {
        var pipe = new PipeWriter();
        try
        {
            pipe.Connect(pipeName, timeoutMs: PipeProtocol.ConnectTimeoutMs);
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
