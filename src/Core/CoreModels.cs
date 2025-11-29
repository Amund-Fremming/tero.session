using System.Text.Json;
using Newtonsoft.Json;

namespace tero.session.src.Core;

public sealed record CachedSession<T>
{
    private T Session { get; set; } = default!;
    private DateTime ExpiresAt {get; set;} = DateTime.Now;

    public CachedSession(T session, TimeSpan ttl)
    {
        Session = session;
        ExpiresAt = DateTime.Now.Add(ttl);
    }

    public bool HasExpired() => ExpiresAt < DateTime.Now;

    public T GetSession() => Session;
    
    public void SetSession(T session)
    {
        ExpiresAt = DateTime.Now;
        Session = session;
    }
}

public enum Error
{
    None = 0,
    KeyExists = 1,
    NotGameHost = 2,
    GameClosed = 3,
    GameFinished = 4,
    GameNotFound = 5,
    System = 6
}



public class CacheTTLOptions
{
    public int SessionMinuttes {get; set;} 
    public int ManagerMinuttes {get; set;} 
}

public sealed record HubInfo
{
    public string GameKey { get; set; }
    public Guid UserId { get; set; }
    public DateTime ExpiresAt { get; set; }

    public HubInfo(string gameKey, Guid userId)
    {
        GameKey = gameKey;
        UserId = userId;
        ExpiresAt = DateTime.Now;
    }

    public bool HasExpired() => ExpiresAt < DateTime.Now;

    public void SetTtl(TimeSpan ttl) => ExpiresAt = DateTime.Now.Add(ttl);
}


public enum GameCategory
{
    [JsonProperty("casual")]
    Casual,
    [JsonProperty("random")]
    Random,
    [JsonProperty("ladies")]
    Ladies,
    [JsonProperty("boys")]
    Boys,
    [JsonProperty("default")]
    Default
}

public enum GameType
{
    Spin,
    Quiz
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

    public bool IsErr() => Error is not null;
    public bool IsOk() => Data is not null;
    public T Unwrap()
    {
        if (Data is null)
        {
            throw new Exception("Cannot unwrap a error result");
        }

        return Data;
    }
}

public sealed record Result<E>
{
    private E? Error { get; set; }

    private Result(E? error)
    {
        Error = error;
    }

    public static Result<E> Ok => new(default);

    public static Result<E> Err(E error) => new(error);

    public static implicit operator Result<E>(E error) => new(error);

    public E Err() => Error!;

    public bool IsErr() => Error is not null && (!typeof(E).IsValueType || !EqualityComparer<E?>.Default.Equals(Error, default(E)));
    public bool IsOk() => !IsErr();
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