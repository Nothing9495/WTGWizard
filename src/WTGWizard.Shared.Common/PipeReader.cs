using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace WTGWizard.Shared.Common;

/// <summary>
/// Pipe 协议读取器 — 从 NamedPipe 读取 JSON 消息并分发事件。
/// 双向通信：子→主为任务消息，主→子为控制消息（task_cancel）。
/// </summary>
public sealed class PipeReader : IDisposable
{
    private readonly PipeStream _pipe;
    private readonly StreamReader _reader;

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

    /// <summary>收到取消指令（主 → 子）。</summary>
    public event Action? OnCancelRequested;

    /// <summary>收到取消回报（子 → 主，Worker 确认已取消）。</summary>
    public event Action? OnCancelled;

    /// <summary>收到握手 ready（主 → 子）。</summary>
    public event Action? OnReady;

    /// <summary>收到握手 ack（子 → 主）。</summary>
    public event Action? OnAck;

    public PipeReader(PipeStream pipe)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe, System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// 开始读取 Pipe 消息（异步）。读取循环在 Pipe 断开时结束。
    /// </summary>
    public void StartReading()
    {
        _ = System.Threading.Tasks.Task.Factory.StartNew(ReadLoop, System.Threading.Tasks.TaskCreationOptions.LongRunning);
    }

    private void ReadLoop()
    {
        try
        {
            while (_pipe.IsConnected)
            {
                string? line = _reader.ReadLine();
                if (line is null)
                    break;

                DispatchMessage(line);
            }
        }
        catch (IOException)
        {
            // Pipe 断开
        }
        catch (ObjectDisposedException)
        {
            // Pipe 已释放
        }

        OnDisconnected?.Invoke();
    }

    private void DispatchMessage(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line[0] != '{')
            return;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
                return;

            string? type = typeElement.GetString();
            string? task = root.TryGetProperty("task", out var taskElement) ? taskElement.GetString() : null;

            switch (type)
            {
                case PipeProtocol.TaskRunning:
                    string? description = root.TryGetProperty("description", out var descElement) ? descElement.GetString() : null;
                    OnRunning?.Invoke(task ?? "", description);
                    break;

                case PipeProtocol.TaskProgress:
                    if (root.TryGetProperty("percent", out var percentElement))
                        OnProgress?.Invoke(task ?? "", percentElement.GetDouble());
                    break;

                case PipeProtocol.TaskCompleted:
                    int returnCode = root.TryGetProperty("returnCode", out var rcElement) ? rcElement.GetInt32() : 0;
                    OnCompleted?.Invoke(task ?? "", returnCode);
                    break;

                case PipeProtocol.TaskFailed:
                    int failCode = root.TryGetProperty("returnCode", out var frcElement) ? frcElement.GetInt32() : -1;
                    string? message = root.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : null;
                    OnFailed?.Invoke(task ?? "", failCode, message);
                    break;

                case PipeProtocol.TaskCancel:
                    OnCancelRequested?.Invoke();
                    break;

                case PipeProtocol.TaskCancelled:
                    OnCancelled?.Invoke();
                    break;

                case PipeProtocol.HandshakeReady:
                    OnReady?.Invoke();
                    break;

                case PipeProtocol.HandshakeAck:
                    OnAck?.Invoke();
                    break;
            }
        }
        catch (JsonException)
        {
            // 忽略无效 JSON
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}
