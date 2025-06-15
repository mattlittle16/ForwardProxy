namespace ForwardProxy.Model;

public record struct ForwardModel
{
    public int StatusCode { get; init; }
    public string? ResponseData { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
}