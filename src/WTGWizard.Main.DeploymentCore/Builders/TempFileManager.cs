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

    public void Dispose()
    {
        try { Directory.Delete(Path.GetTempPath() + "WTGWizard", recursive: true); }
        catch { /* best effort */ }
    }
}
