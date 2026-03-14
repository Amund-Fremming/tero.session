using System.Text.Json;
using System.Text.Json.Serialization;

namespace tero.session.src.Core;

public sealed record SerializableError
{
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? StackTrace { get; set; } = string.Empty;
};

public sealed record CachedSession<T>
{
    private T Session { get; set; } = default!;
    private DateTime ExpiresAt { get; set; } = DateTime.Now;
    private TimeSpan Ttl { get; set; }

    public CachedSession(T session, TimeSpan ttl)
    {
        Session = session;
        Ttl = ttl;
        ExpiresAt = DateTime.Now.Add(ttl);
    }

    public bool HasExpired() => ExpiresAt < DateTime.Now;

    public T GetSession() => Session;

    public void SetSession(T session)
    {
        ExpiresAt = DateTime.Now.Add(Ttl);
        Session = session;
    }
}

public sealed record Error
{
    public ErrorType Type { get; init; }
    public string Message { get; init; }

    public Error(ErrorType type, string message)
    {
        Type = type;
        Message = message;
    }

    public enum ErrorType
    {
        KeyExists = 0,
        NotGameHost = 1,
        GameClosed = 2,
        GameFinished = 3,
        GameNotFound = 4,
        System = 5,
        Json = 6,
        NullReference = 7,
        Overflow = 8,
        Http = 9,
        Upstream = 10,
        IdConflict = 11,
    }

    public static Error KeyExists => new(ErrorType.KeyExists, "Game key in use");
    public static Error NotGameHost => new(ErrorType.NotGameHost, "Only game host can perform this action");
    public static Error GameClosed => new(ErrorType.GameClosed, "Game is closed for new actions");
    public static Error GameFinished => new(ErrorType.GameFinished, "Game is finished");
    public static Error GameNotFound => new(ErrorType.GameNotFound, "Game not found");
    public static Error System => new(ErrorType.System, "Internal system error");
    public static Error Json => new(ErrorType.Json, "Serialization error");
    public static Error NullReference => new(ErrorType.NullReference, "Null or empty value provided");
    public static Error Overflow => new(ErrorType.Overflow, "Overflow error");
    public static Error Http => new(ErrorType.Http, "HTTP error");
    public static Error Upstream => new(ErrorType.Upstream, "Upstream service error");
}

public class CacheTTLOptions
{
    public int SessionMinuttes { get; set; }
    public int ManagerMinuttes { get; set; }
}

public sealed record HubInfo
{
    public string GameKey { get; set; }
    public Guid UserId { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// Only use this for hubs where the disconnec function never user the user id
    public HubInfo(string gameKey)
    {
        GameKey = gameKey;
        UserId = Guid.NewGuid();
        ExpiresAt = DateTime.Now;
    }

    public HubInfo(string gameKey, Guid userId)
    {
        GameKey = gameKey;
        UserId = userId;
        ExpiresAt = DateTime.Now;
    }

    public bool HasExpired() => ExpiresAt < DateTime.Now;

    public void SetTtl(TimeSpan ttl) => ExpiresAt = DateTime.Now.Add(ttl);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameCategory
{
    [JsonPropertyName("girls")]
    Girls,
    [JsonPropertyName("boys")]
    Boys,
    [JsonPropertyName("mixed")]
    Mixed,
    [JsonPropertyName("innercircle")]
    InnerCircle
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameType
{
    Quiz,
    Duel,
    Roulette,
    Imposter
}

public sealed record Result<T, E>
{
    private T? Data { get; set; }
    private E? Error { get; set; }

    private Result(T? data, E? error)
    {
        Data = data;
        Error = error;
    }

    public static Result<T, E> Ok(T data) => new(data, default!);

    public static Result<T, E> Err(E error) => new(default!, error);

    public static implicit operator Result<T, E>(T data) => new(data, default!);

    public static implicit operator Result<T, E>(E error) => new(default!, error);
    public E Err() => Error!;

    public bool IsErr() => Data is null;
    public bool IsOk() => Data is not null;
    public T Unwrap()
    {
        if (Data is null)
        {
            throw new NullReferenceException("Cannot unwrap a error result");
        }

        return Data;
    }
}

public sealed record Result<E>
{
    private bool IsError { get; set; }
    private E? Error { get; set; }

    private Result(bool isError, E? error)
    {
        IsError = isError;
        Error = error;
    }

    public static Result<E> Ok => new(false, default);

    public static Result<E> Err(E error) => new(true, error);

    public static implicit operator Result<E>(E error) => new(true, error);

    public E Err() => Error!;

    public bool IsErr() => IsError;
    public bool IsOk() => !IsError;
}

public sealed record Option<T>(T Data)
{
    public static Option<T> Some(T data) => new(data);
    public static Option<T> None => new(default(T)!);

    public bool IsNone() => Data is null;
    public bool IsSome() => Data is not null;

    public T Unwrap()
    {
        if (Data is null)
        {
            throw new Exception("Cannot unwrap a empty option");
        }

        return Data;
    }
}