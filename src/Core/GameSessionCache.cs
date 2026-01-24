using System.Collections.Concurrent;
using tero.session.src.Features.Platform;

namespace tero.session.src.Core;

public class GameSessionCache<TSession>(ILogger<GameSessionCache<TSession>> logger, CacheTTLOptions options, PlatformClient platformClient)
{
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(options.SessionMinuttes);
    private readonly ConcurrentDictionary<string, CachedSession<TSession>> _cache = [];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = [];

    public ConcurrentDictionary<string, CachedSession<TSession>> GetCopy() => new(_cache);

    public int Size() => _cache.Count;

    public async Task<Result<TSession, Error>> Get(string key)
    {
        SemaphoreSlim sem = null!;
        try
        {
            if (key == string.Empty || key is null)
            {
                return Error.NullReference;
            }

            sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync();

            if (!_cache.TryGetValue(key, out var cacheEntry))
            {
                return Error.GameNotFound;
            }

            return cacheEntry.GetSession();
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Update)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("Get")
                .WithDescription("Get cache entry")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "Failed to get session cache entry");
            return Error.System;
        }
        finally
        {
            sem?.Release();
        }
    }

    public Result<Error> Insert(string key, TSession session)
    {
        try
        {
            if (key == string.Empty || key is null || session is null)
            {
                logger.LogInformation("Recieved a empty key");
                return Error.NullReference;
            }

            var entry = new CachedSession<TSession>(session, _ttl);
            if (!_cache.TryAdd(key, entry))
            {
                var log = LogBuilder.New()
                    .WithAction(LogAction.Update)
                    .WithCeverity(LogCeverity.Critical)
                    .WithDescription("Tried adding session with existing key")
                    .WithFunctionName("Insert")
                    .Build();

                platformClient.CreateSystemLogAsync(log);
                logger.LogWarning("Key already exists");
                return Error.KeyExists;
            }

            return Result<Error>.Ok;
        }
        catch (OverflowException error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Create)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("Insert")
                .WithDescription("GameSessionCache overflow on insert")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "Cache overflowed");
            return Error.Overflow;
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Create)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName($"Insert - {typeof(TSession)}")
                .WithDescription("Insert into GameSessionCache threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "Failed to insert into session cache");
            return Error.System;
        }
    }

    public async Task<Result<TSession, Error>> Upsert(string key, Func<TSession, Result<TSession, Error>> func)
    {
        SemaphoreSlim sem = null!;

        try
        {
            if (key == string.Empty || key is null)
            {
                return Error.NullReference;
            }

            sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync();

            if (!_cache.TryGetValue(key, out var entry))
            {
                var log = LogBuilder.New()
                    .WithAction(LogAction.Read)
                    .WithCeverity(LogCeverity.Warning)
                    .WithFunctionName($"Upsert - {typeof(TSession)}")
                    .WithDescription("Game session not found in cache")
                    .Build();

                platformClient.CreateSystemLogAsync(log);
                logger.LogWarning("Game not found");
                return Error.GameNotFound;
            }

            var session = entry.GetSession();
            var result = func(session);
            entry.SetSession(session);

            return result;
        }
        catch (OverflowException error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Update)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("Upsert")
                .WithDescription("GameSessionCache overflow on upsert")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "Overflow error");
            return Error.Overflow;
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Update)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("Upsert")
                .WithDescription("Upsert into GameSessionCache threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "Failed to upsert into session cache");
            return Error.System;
        }
        finally
        {
            sem?.Release();
        }
    }

    public async Task<Result<TResult, Error>> Upsert<TResult>(string key, Func<TSession, TResult> func)
    {
        SemaphoreSlim sem = null!;

        try
        {
            if (key == string.Empty || key is null)
            {
                return Error.NullReference;
            }

            sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync();

            if (!_cache.TryGetValue(key, out var entry))
            {
                var log = LogBuilder.New()
                    .WithAction(LogAction.Read)
                    .WithCeverity(LogCeverity.Warning)
                    .WithFunctionName($"Upsert - {typeof(TSession)}")
                    .Build();

                platformClient.CreateSystemLogAsync(log);
                logger.LogWarning("Game not found");
                return Error.GameNotFound;
            }

            var session = entry.GetSession();
            var result = func(session);
            entry.SetSession(session);

            return result;
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Update)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("Upsert<TResult>")
                .WithDescription("Upsert<TResult> into GameSessionCache threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "Failed to upsert into session cache");
            return Error.System;
        }
        finally
        {
            sem?.Release();
        }
    }

    public async Task<Result<Error>> Remove(string key)
    {
        SemaphoreSlim sem = null!;

        try
        {
            if (key == string.Empty || key is null)
            {
                logger.LogCritical("Tried to remove session with non present key");
                return Error.NullReference;
            }

            sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync();

            if (!_cache.TryRemove(key, out _))
            {
                logger.LogWarning("Tried removing non exising session from the cache");
            }

            if (_locks.TryRemove(key, out var removedSem))
            {
                removedSem.Dispose();
            }

            return Result<Error>.Ok;
        }
        catch (ObjectDisposedException)
        {
            logger.LogWarning("Attempted to access a disposed semaphore for key: {Key}", key);
            return Result<Error>.Ok;
        }
        catch (OverflowException error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Delete)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("Remove")
                .WithDescription("GameSessionCache overflow on remove")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "Remove - overflow error");
            return Error.Overflow;
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Delete)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("Remove")
                .WithDescription("Remove from GameSessionCache threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "Failed to remove session from cache");
            return Error.System;
        }
        finally
        {
            try
            {
                sem?.Release();
            }
            catch (ObjectDisposedException)
            {
                // Semaphore already disposed, ignore
            }
        }
    }
}