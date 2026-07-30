namespace WTGWizard.Main.DeploymentCore.Models;

/// <summary>
/// Worker 命令执行结果。
/// </summary>
public sealed record WorkerExecutionResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static WorkerExecutionResult Ok(int exitCode = 0)
        => new() { Success = true, ExitCode = exitCode };

    public static WorkerExecutionResult Fail(int exitCode, string error)
        => new() { Success = false, ExitCode = exitCode, ErrorMessage = error };
}
