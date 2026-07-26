using System;
using System.IO.Pipes;
using System.Text;

namespace WTGWizard.Shared.Common;

/// <summary>
/// Worker 侧 Pipe 写入器 — 通过 NamedPipe 发送协议消息到主进程。
/// </summary>
public sealed class PipeWriter : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private readonly object _lock = new();
    private bool _connected;

    public bool IsConnected => _connected && _pipe?.IsConnected == true;

    /// <summary>
    /// 连接到主进程的 NamedPipe 服务端。
    /// </summary>
    /// <param name="pipeName">Pipe 名称。</param>
    /// <param name="timeoutMs">连接超时（毫秒），默认 15 秒。</param>
    public void Connect(string pipeName, int timeoutMs = PipeProtocol.ConnectTimeoutMs)
    {
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
        _pipe.Connect(timeoutMs);
        _connected = true;
    }

    public void WriteRunning(string task, string? description = null) => Send(PipeProtocol.BuildRunning(task, description));
    public void WriteProgress(string task, double percent) => Send(PipeProtocol.BuildProgress(task, percent));
    public void WriteCompleted(string task, int returnCode) => Send(PipeProtocol.BuildCompleted(task, returnCode));
    public void WriteFailed(string task, int returnCode, string? message = null) => Send(PipeProtocol.BuildFailed(task, returnCode, message));
    public void WriteCancel() => Send(PipeProtocol.BuildCancel());

    private void Send(string json)
    {
        lock (_lock)
        {
            if (!_connected) return;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json + PipeProtocol.NewLine);
                _pipe!.Write(bytes);
                _pipe.Flush();
            }
            catch
            {
                _connected = false;
            }
        }
    }

    public void Dispose() => _pipe?.Dispose();
}
