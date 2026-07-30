using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Worker;

public class WorkerCommandFactory
{
    public WorkerCommand BuildDism(string args, int timeoutMs = 0)
        => new("dism", $"--args \"{Escape(args)}\"{Timeout(timeoutMs)}");
    public WorkerCommand BuildBcdboot(string args, int timeoutMs = 0)
        => new("bcdboot", $"--args \"{Escape(args)}\"{Timeout(timeoutMs)}");
    public WorkerCommand BuildPwsh(string scriptPath, int timeoutMs = 0)
        => new("pwsh", $"--script \"{Escape(scriptPath)}\"{Timeout(timeoutMs)}");
    public WorkerCommand BuildExtract(string wimPath, int index, string targetDir)
        => new("extract", $"--wim \"{Escape(wimPath)}\" --index {index} --target \"{Escape(targetDir)}\"");
    public WorkerCommand BuildFileCopy(string src, string dst)
        => new("filecopy", $"--src \"{Escape(src)}\" --dst \"{Escape(dst)}\"");

    private static string Timeout(int ms) => ms > 0 ? $" --timeout {ms}" : "";
    private static string Escape(string v) => v.Replace("\"", "\\\"");
}
