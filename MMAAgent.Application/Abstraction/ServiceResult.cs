namespace MMAAgent.Application.Abstractions;

public sealed class ServiceResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }

    public static ServiceResult Ok(string? message = null) => new() { Success = true, Message = message };
    public static ServiceResult Fail(string message) => new() { Success = false, Message = message };
}
