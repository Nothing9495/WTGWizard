using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WTGWizard.Shared.Common;

/// <summary>
/// Pipe 服务端 — 创建 NamedPipe 并等待 Worker 连接。
/// Main 项目使用此类创建 Pipe 服务端，等待 Worker 连接后读取消息，
/// 并可下发控制消息（task_cancel）。
/// </summary>
public sealed class PipeServer : IDisposable
{
    private readonly object _writeLock = new();
    private NamedPipeServerStream? _pipeServer;
    private PipeReader? _reader;
    private readonly string _pipeName;
    private bool _handshakeCompleted;

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

    /// <summary>收到取消确认（Worker 回报）。</summary>
    public event Action? OnCancel;

    /// <summary>握手完成（Worker 回报 ack）。</summary>
    public event Action? OnHandshakeComplete;

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
            PipeDirection.InOut,
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
        _reader.OnCancelRequested += () => OnCancel?.Invoke();
        _reader.OnAck += () => OnHandshakeComplete?.Invoke();
        _reader.StartReading();

        // 三次握手：发送 ready，等待 Worker 回报 ack（15s 超时）
        SendMessage(PipeProtocol.BuildReady());
    }

    /// <summary>
    /// 下发 JSON 消息（Main → Worker），Worker 已断开时静默忽略。
    /// </summary>
    private void SendMessage(string json)
    {
        if (_pipeServer is null) return;

        byte[] bytes = Encoding.UTF8.GetBytes(json + PipeProtocol.NewLine);
        lock (_writeLock)
        {
            try
            {
                _pipeServer.Write(bytes);
                _pipeServer.Flush();
            }
            catch
            {
                // Worker 已断开：忽略
            }
        }
    }

    /// <summary>
    /// 等待 Worker 回报握手 ack。
    /// </summary>
    /// <param name="timeoutMs">握手超时（毫秒），默认 15 秒。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="TimeoutException">握手超时。</exception>
    public async Task WaitHandshakeAsync(int timeoutMs = PipeProtocol.ConnectTimeoutMs, CancellationToken cancellationToken = default)
    {
        if (_handshakeCompleted) return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnHandshakeHandler() { _handshakeCompleted = true; tcs.TrySetResult(); }
        OnHandshakeComplete += OnHandshakeHandler;
        try
        {
            await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);
        }
        finally
        {
            OnHandshakeComplete -= OnHandshakeHandler;
        }
    }

    /// <summary>
    /// 下发取消指令（主 → Worker），请求 Worker 主动终止当前任务。
    /// </summary>
    public void SendCancel() => SendMessage(PipeProtocol.BuildCancel());

    public void Dispose()
    {
        _reader?.Dispose();
        _reader = null;

        _pipeServer?.Dispose();
        _pipeServer = null;
    }
}
