using System.Text.Json;
using System.Text.Json.Serialization;
using tero.session.src.Core;

namespace tero.session.src.Features.Platform;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApiResponse
{
    Ok,
    KeyExists,
    InvalidJson,
    Error
}

public sealed record InitiateGameRequest
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }
}

public sealed record CacheInfo
{
    public int SpinSessionSize { get; set; }
    public int SpinManagerSize { get; set; }
    public int QuizSessionSize { get; set; }
    public int QuizManagerSize { get; set; }
    public int ImposterSessionSize { get; set; }
    public int ImposterManagerSize { get; set; }
}

public class PlatformOptions
{
    public string BaseUrl { get; set; } = string.Empty;
}

public record GameSessionRequest
{
    [JsonPropertyName("payload")]
    public JsonElement Value { get; init; }
}

public sealed record CreateSyslogRequest
{
    [JsonPropertyName("action")]
    public LogAction? Action { get; set; }

    [JsonPropertyName("ceverity")]
    public LogCeverity? Ceverity { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("metadata")]
    public object? Metadata { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogAction
{
    [JsonPropertyName("create")]
    Create,
    [JsonPropertyName("read")]
    Read,
    [JsonPropertyName("update")]
    Update,
    [JsonPropertyName("delete")]
    Delete,
    [JsonPropertyName("sync")]
    Sync,
    [JsonPropertyName("other")]
    Other,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogCeverity
{
    [JsonPropertyName("critical")]
    Critical,
    [JsonPropertyName("warning")]
    Warning,
    [JsonPropertyName("info")]
    Info,
}

public class LogBuilder
{
    private LogAction? Action { get; set; }
    private LogCeverity? Ceverity { get; set; }
    private string? Description { get; set; }
    private string? FileName { get; set; }
    private object? Metadata { get; set; }

    public static LogBuilder New() => new();

    public LogBuilder WithAction(LogAction action)
    {
        Action = action;
        return this;
    }

    public LogBuilder WithCeverity(LogCeverity ceverity)
    {
        Ceverity = ceverity;
        return this;
    }

    public LogBuilder WithDescription(string description)
    {
        Description = description;
        return this;
    }

    public LogBuilder WithFunctionName(string fileName)
    {
        FileName = fileName;
        return this;
    }

    public LogBuilder WithMetadata(Exception error)
    {
        Metadata = error.ToSerializable();
        return this;
    }

    public CreateSyslogRequest Build()
        => new()
        {
            Action = Action is null ? LogAction.Other : Action,
            Ceverity = Ceverity is null ? LogCeverity.Info : Ceverity,
            Description = Description is not null ? Description : "No description",
            FileName = FileName is not null ? FileName : "unknown",
            Metadata = Metadata,
        };
}