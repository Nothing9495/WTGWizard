namespace WTGWizard.Worker.Models;

public class WorkerResult
{
    public bool Success { get; set; }

    public string? Message { get; set; }

    public object? Data { get; set; }

    public static WorkerResult Ok(object? data = null, string? message = null)
    {
        return new WorkerResult
        {
            Success = true,
            Message = message,
            Data = data
        };
    }

    public static WorkerResult Fail(string message)
    {
        return new WorkerResult
        {
            Success = false,
            Message = message
        };
    }
}
