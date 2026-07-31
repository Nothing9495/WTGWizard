using System;
using System.Text;

namespace WTGWizard.Shared.Services;

/// <summary>
/// 终端输出缓冲 — 线程安全快照缓冲，Worker 输出写入，TaskPage 读取。
/// 快照模式支持断线重连后的历史回放。
/// </summary>
public sealed class TerminalOutputBuffer
{
    private readonly StringBuilder _buffer = new();
    private readonly object _lock = new();

    public static TerminalOutputBuffer Shared { get; } = new();

    public event Action<string>? OutputUpdated;

    public string Snapshot
    {
        get
        {
            lock (_lock) { return _buffer.ToString(); }
        }
    }

    public void Clear()
    {
        string snapshot;
        lock (_lock)
        {
            _buffer.Clear();
            snapshot = string.Empty;
        }
        OutputUpdated?.Invoke(snapshot);
    }

    public void AppendBlankLine()
    {
        if (_buffer.Length == 0) return;

        string snapshot;
        lock (_lock)
        {
            _buffer.Append("\n ");
            snapshot = _buffer.ToString();
        }
        OutputUpdated?.Invoke(snapshot);
    }

    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        string snapshot;
        lock (_lock)
        {
            if (_buffer.Length > 0)
                _buffer.Append('\n');
            _buffer.Append(text);
            snapshot = _buffer.ToString();
        }
        OutputUpdated?.Invoke(snapshot);
    }
}
