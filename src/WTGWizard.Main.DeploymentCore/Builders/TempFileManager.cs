using System;
using System.IO;
using System.Text;

namespace WTGWizard.Main.DeploymentCore.Builders;

public sealed class TempFileManager : IDisposable
{
    private readonly string _rootDir;

    public TempFileManager()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "WTGWizard", "WorkerCache", "Scripts");
        Directory.CreateDirectory(_rootDir);
    }

    public string SaveScript(string fileName, string content)
    {
        string path = Path.Combine(_rootDir, fileName);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    /// <summary>
    /// 部署完成清理 — 仅删除本管理器管理的 Scripts 目录。
    /// </summary>
    public void Dispose()
    {
        try { Directory.Delete(_rootDir, recursive: true); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// 全局清理 — 删除整个 WTGWizard 临时目录（含 AnswerFiles 等）。
    /// 由主程序生命周期结束（App 关闭）时调用，防部署中断残留。
    /// </summary>
    public static void CleanupAll()
    {
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "WTGWizard");
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort */ }
    }
}
