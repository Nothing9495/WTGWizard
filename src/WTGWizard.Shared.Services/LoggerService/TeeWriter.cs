using System;
using System.IO;
using System.Text;

namespace WTGWizard.Shared.Services.Logger;

/// <summary>
/// TeeWriter — 同时写入 stdout + 日志文件。
/// Worker 项目使用，将日志输出到控制台的同时写入文件。
/// </summary>
public sealed class TeeWriter : TextWriter
{
    private readonly TextWriter _stdout;
    private readonly StreamWriter _logFile;

    public override Encoding Encoding => _stdout.Encoding;

    public TeeWriter(TextWriter stdout, StreamWriter logFile)
    {
        _stdout = stdout;
        _logFile = logFile;
    }

    public override void Write(char value)
    {
        _stdout.Write(value);
        _logFile.Write(value);
    }

    public override void Write(string? value)
    {
        _stdout.Write(value);
        _logFile.Write(value);
    }

    public override void WriteLine(string? value)
    {
        _stdout.WriteLine(value);
        _logFile.WriteLine(value);
        _logFile.Flush();
    }

    public override void Flush()
    {
        _stdout.Flush();
        _logFile.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stdout.Dispose();
            _logFile.Dispose();
        }
        base.Dispose(disposing);
    }
}
