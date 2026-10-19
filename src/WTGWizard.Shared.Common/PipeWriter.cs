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

    // 握手 ready 信号：与 OnAck 同样在 Connect() 的读循环接线处挂载，
    // 避免 ready 因 WaitForReady 订阅过晚而丢失（PipeServer 侧的同源修复）。
    private readonly ManualResetEventSlim _readySignal = new(false);

    public bool IsConnected => _connected && _pipe?.IsConnected == true;

    /// <summary>收到取消指令（主 → 子）。</summary>
    public event Action? OnCancelRequested;

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
        _reader.OnReady += () => _readySignal.Set();
        _reader.StartReading();
    }

    public bool WriteRunning(string task, string? description = null) => Send(PipeProtocol.BuildRunning(task, description));
    public bool WriteProgress(string task, double percent)
        => Write(Encoding.UTF8.GetBytes(PipeProtocol.BuildProgress(task, percent) + PipeProtocol.NewLine), _ct, WriteTimeoutMs, "progress", throwOnTimeout: true, logDetail: false);
    public bool WriteCompleted(string task, int returnCode) => Send(PipeProtocol.BuildCompleted(task, returnCode));
    public bool WriteFailed(string task, int returnCode, string? message = null) => Send(PipeProtocol.BuildFailed(task, returnCode, message));
    public bool WriteCancelled(string task) => Send(PipeProtocol.BuildCancelled(task));
    public bool WriteCancel() => Write(Encoding.UTF8.GetBytes(PipeProtocol.BuildCancel() + PipeProtocol.NewLine),
        CancellationToken.None, CancelAckTimeoutMs, "cancel ack", throwOnTimeout: false);
    public bool WriteReady() => Send(PipeProtocol.BuildReady());
    public bool WriteAck() => Send(PipeProtocol.BuildAck());

    /// <summary>
    /// 三次握手（Worker 侧）：等待 Main 的 ready，超时/取消则失败。
    /// </summary>
    /// <param name="timeoutMs">握手超时（毫秒），默认 15 秒。</param>
    /// <returns>握手成功返回 true。</returns>
    public bool WaitForReady(int timeoutMs = PipeProtocol.ConnectTimeoutMs)
    {
        if (!_connected) return false;

        // 信号由 Connect() 中的 OnReady 接线置位，与等待方是否已就绪无关。
        using var cancelReg = _ct.Register(() => _readySignal.Set());
        return _readySignal.Wait(TimeSpan.FromMilliseconds(timeoutMs))
            && !_ct.IsCancellationRequested;
    }

    private bool Send(string json)
        => Write(Encoding.UTF8.GetBytes(json + PipeProtocol.NewLine), _ct, WriteTimeoutMs, "write", throwOnTimeout: true);

    /// <summary>
    /// 统一写入路径：可取消（任务消息）/ 不可取消（取消确认，token 已取消时仍须送达）。
    /// 超时/失败时 Dispose 中断 NtWriteFile 阻塞兜底。
    /// logDetail=false 用于高频消息（如进度），跳过明细日志。
    /// 返回值：消息是否**确实送达管道**；false 表示管道已不可用（已置 _connected=false）。
    /// </summary>
    private bool Write(byte[] bytes, CancellationToken ct, int timeoutMs, string tag, bool throwOnTimeout, bool logDetail = true)
    {
        lock (_lock)
        {
            if (!_connected) return false;
            var pipe = _pipe;
            if (pipe is null) return false;

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
                    return false;
                }

                writeTask.GetAwaiter().GetResult();
                pipe.Flush();
                if (logDetail) DebugLog?.Invoke($"Pipe: {tag} completed");
                return true;
            }
            catch (OperationCanceledException)
            {
                _connected = false;
                DebugLog?.Invoke($"Pipe: {tag} cancelled");
                if (throwOnTimeout) throw;
                return false;
            }
            catch (Exception ex)
            {
                _connected = false;
                DebugLog?.Invoke($"Pipe: {tag} failed ({ex.GetType().Name}: {ex.Message})");
                return false;
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
