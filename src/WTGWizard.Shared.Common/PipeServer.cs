using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WTGWizard.Shared.Common;

/// <summary>
/// Pipe 服务端 — 创建 NamedPipe 并等待 Worker 连接。
/// Main 项目使用此类创建 Pipe 服务端，等待 Worker 连接后读取消息。
/// </summary>
public sealed class PipeServer : IDisposable
{
    private NamedPipeServerStream? _pipeServer;
    private PipeReader? _reader;
    private readonly string _pipeName;

    /// <summary>任务开始事件。</summary>
    public event Action<string, string?>? OnRunning;

    /// <summary>进度更新事件（task, percent）。</summary>
    public event Action<string, double>? OnProgress;

    /// <summary>任务完成事件（task, returnCode）。</summary>
    public event Action<string, int>? OnCompleted;

    /// <summary>任务失败事件（task, returnCode, message）。</summary>
    public event Action<string, int, string?>? OnFailed;

    /// <summary>Pipe 断开事件。</summary>
    public event Action? OnDisconnected;

    public PipeServer(string pipeName)
    {
        _pipeName = pipeName;
    }

    /// <summary>
    /// 创建 Pipe 服务端并等待 Worker 连接。
    /// </summary>
    /// <param name="timeoutMs">等待连接超时（毫秒），默认 15 秒。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task WaitForConnectionAsync(int timeoutMs = PipeProtocol.ConnectTimeoutMs, CancellationToken cancellationToken = default)
    {
        _pipeServer = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            await _pipeServer.WaitForConnectionAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Worker connection timed out after {timeoutMs}ms.");
        }

        // 创建 PipeReader 并订阅事件
        _reader = new PipeReader(_pipeServer);
        _reader.OnRunning += (task, desc) => OnRunning?.Invoke(task, desc);
        _reader.OnProgress += (task, percent) => OnProgress?.Invoke(task, percent);
        _reader.OnCompleted += (task, rc) => OnCompleted?.Invoke(task, rc);
        _reader.OnFailed += (task, rc, msg) => OnFailed?.Invoke(task, rc, msg);
        _reader.OnDisconnected += () => OnDisconnected?.Invoke();
        _reader.StartReading();
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _reader = null;

        _pipeServer?.Dispose();
        _pipeServer = null;
    }
}
