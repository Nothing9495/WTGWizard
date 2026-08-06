using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WTGWizard.Shared.Common;

/// <summary>
/// Worker 侧 Pipe 通道 — 通过 NamedPipe 与主进程双向通信：
/// 发送协议消息到主进程，并监听主进程下发的控制消息（task_cancel）。
/// 写入支持取消/超时（Asynchronous 模式使取消可中断 NtWriteFile 阻塞）。
/// </summary>
public sealed class PipeWriter : IDisposable
{
    private const int WriteTimeoutMs = 15000;
    private const int CancelAckTimeoutMs = 3000;

    private NamedPipeClientStream? _pipe;
    private PipeReader? _reader;
    private readonly object _lock = new();
    private bool _connected;
    private CancellationToken _ct = CancellationToken.None;

    public bool IsConnected => _connected && _pipe?.IsConnected == true;

    /// <summary>收到取消指令（主 → 子）。</summary>
    public event Action? OnCancelRequested;

    /// <summary>收到握手 ready（主 → 子）。</summary>
    public event Action? OnReady;

    /// <summary>收到握手 ack（子 → 主，Main 侧经 PipeServer 桥接）。</summary>
    public event Action? OnAck;

    /// <summary>
    /// 诊断日志回调（Worker 侧注入 WorkerDebug.Write，Shared.Common 保持无日志依赖）。
    /// </summary>
    public Action<string>? DebugLog { get; set; }

    /// <summary>
    /// 注入取消令牌（Worker 侧传入全局取消源），写入阻塞可被取消中断。
    /// </summary>
    public void SetCancellationToken(CancellationToken token) => _ct = token;

    /// <summary>
    /// 连接到主进程的 NamedPipe 服务端，并启动下行读取。
    /// </summary>
    /// <param name="pipeName">Pipe 名称。</param>
    /// <param name="timeoutMs">连接超时（毫秒），默认 15 秒。</param>
    public void Connect(string pipeName, int timeoutMs = PipeProtocol.ConnectTimeoutMs)
    {
        // Asynchronous：WriteAsync 使用重叠 I/O，取消可通过 CancelIoEx 中断 NtWriteFile 阻塞
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        _pipe.Connect(timeoutMs);
        _connected = true;

        DebugLog?.Invoke($"Pipe: connect succeeded ({pipeName})");

        _reader = new PipeReader(_pipe);
        _reader.OnCancelRequested += () => OnCancelRequested?.Invoke();
        _reader.OnReady += () => OnReady?.Invoke();
        _reader.OnAck += () => OnAck?.Invoke();
        _reader.StartReading();
    }

    public void WriteRunning(string task, string? description = null) => Send(PipeProtocol.BuildRunning(task, description));
    public void WriteProgress(string task, double percent)
        => Write(Encoding.UTF8.GetBytes(PipeProtocol.BuildProgress(task, percent) + PipeProtocol.NewLine), _ct, WriteTimeoutMs, "progress", throwOnTimeout: true, logDetail: false);
    public void WriteCompleted(string task, int returnCode) => Send(PipeProtocol.BuildCompleted(task, returnCode));
    public void WriteFailed(string task, int returnCode, string? message = null) => Send(PipeProtocol.BuildFailed(task, returnCode, message));
    public void WriteCancel() => Write(Encoding.UTF8.GetBytes(PipeProtocol.BuildCancel() + PipeProtocol.NewLine),
        CancellationToken.None, CancelAckTimeoutMs, "cancel ack", throwOnTimeout: false);
    public void WriteReady() => Send(PipeProtocol.BuildReady());
    public void WriteAck() => Send(PipeProtocol.BuildAck());

    /// <summary>
    /// 三次握手（Worker 侧）：等待 Main 的 ready，超时/取消则失败。
    /// </summary>
    /// <param name="timeoutMs">握手超时（毫秒），默认 15 秒。</param>
    /// <returns>握手成功返回 true。</returns>
    public bool WaitForReady(int timeoutMs = PipeProtocol.ConnectTimeoutMs)
    {
        if (!_connected) return false;

        using var waitHandle = new ManualResetEventSlim(false);
        void OnReadyHandler() => waitHandle.Set();
        OnReady += OnReadyHandler;
        using var cancelReg = _ct.Register(() => waitHandle.Set());
        try
        {
            return waitHandle.Wait(TimeSpan.FromMilliseconds(timeoutMs))
                && !_ct.IsCancellationRequested;
        }
        finally
        {
            OnReady -= OnReadyHandler;
        }
    }

    private void Send(string json)
        => Write(Encoding.UTF8.GetBytes(json + PipeProtocol.NewLine), _ct, WriteTimeoutMs, "write", throwOnTimeout: true);

    /// <summary>
    /// 统一写入路径：可取消（任务消息）/ 不可取消（取消确认，token 已取消时仍须送达）。
    /// 超时/失败时 Dispose 中断 NtWriteFile 阻塞兜底。
    /// logDetail=false 用于高频消息（如进度），跳过明细日志；异常路径日志始终保留。
    /// </summary>
    private void Write(byte[] bytes, CancellationToken ct, int timeoutMs, string tag, bool throwOnTimeout, bool logDetail = true)
    {
        lock (_lock)
        {
            if (!_connected) return;
            var pipe = _pipe;
            if (pipe is null) return;

            try
            {
                if (logDetail) DebugLog?.Invoke($"Pipe: {tag} {bytes.Length} bytes");
                var writeTask = pipe.WriteAsync(bytes, ct).AsTask();

                if (!writeTask.Wait(TimeSpan.FromMilliseconds(timeoutMs)))
                {
                    _connected = false;
                    pipe.Dispose();
                    DebugLog?.Invoke($"Pipe: {tag} timed out, pipe disposed");
                    if (throwOnTimeout) throw new OperationCanceledException("Pipe write timed out");
                    return;
                }

                writeTask.GetAwaiter().GetResult();
                pipe.Flush();
                if (logDetail) DebugLog?.Invoke($"Pipe: {tag} completed");
            }
            catch (OperationCanceledException)
            {
                _connected = false;
                DebugLog?.Invoke($"Pipe: {tag} cancelled");
                if (throwOnTimeout) throw;
            }
            catch
            {
                _connected = false;
            }
        }
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _reader = null;
        _pipe?.Dispose();
    }
}
