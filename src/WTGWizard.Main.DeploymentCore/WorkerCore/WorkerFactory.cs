namespace WTGWizard.Main.DeploymentCore.WorkerCore;

/// <summary>
/// Worker 工厂 — 为每种 Worker 命令构建 (command, arguments) 元组。
/// </summary>
public static class WorkerFactory
{
    public static (string command, string args) BuildDism(string arguments, int timeoutMs = 0)
        => ("dism", $"--args \"{EscapeArgument(arguments)}\"{AppendTimeout(timeoutMs)}");

    public static (string command, string args) BuildBcdboot(string arguments, int timeoutMs = 0)
        => ("bcdboot", $"--args \"{EscapeArgument(arguments)}\"{AppendTimeout(timeoutMs)}");

    public static (string command, string args) BuildPwsh(string scriptPath, int timeoutMs = 0)
        => ("pwsh", $"--script \"{EscapeArgument(scriptPath)}\"{AppendTimeout(timeoutMs)}");

    public static (string command, string args) BuildExtract(string wimPath, int index, string targetDir)
        => ("extract", $"--wim \"{EscapeArgument(wimPath)}\" --index {index} --target \"{EscapeArgument(targetDir)}\"");

    public static (string command, string args) BuildFileCopy(string src, string dst)
        => ("filecopy", $"--src \"{EscapeArgument(src)}\" --dst \"{EscapeArgument(dst)}\"");

    private static string AppendTimeout(int timeoutMs)
        => timeoutMs > 0 ? $" --timeout {timeoutMs}" : "";

    private static string EscapeArgument(string value)
        => value.Replace("\"", "\\\"");
}
