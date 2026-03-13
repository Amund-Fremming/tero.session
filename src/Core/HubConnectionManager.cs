using System.Collections.Concurrent;
using tero.session.src.Features.Platform;

namespace tero.session.src.Core;

public class HubConnectionManager<T>(ILogger<HubConnectionManager<T>> logger, CacheTTLOptions options, PlatformClient platformClient)
{
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(options.ManagerMinuttes);
    private readonly ConcurrentDictionary<string, HubInfo> _manager = [];

    public ConcurrentDictionary<string, HubInfo> GetCopy() => new(_manager);

    public int Size() => _manager.Count;

    public Result<Option<HubInfo>, Error> Get(string connectionId)
    {
        try
        {
            if (connectionId == string.Empty || connectionId is null)
            {
                return new Error(Error.ErrorType.NullReference, "Get failed: connectionId was null or empty");
            }

            if (!_manager.TryGetValue(connectionId, out var value))
            {
                return Option<HubInfo>.None;
            }

            if (value is null)
            {
                return Option<HubInfo>.None;
            }

            return Option<HubInfo>.Some(value);
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Other)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("Get")
                .WithDescription("Get on HubConnectionManager threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(Get));
            return new Error(Error.ErrorType.System, "Get failed: unexpected exception while reading hub connection manager entry");
        }
    }

    public Result<Error> Insert(string connectionId, HubInfo value)
    {
        try
        {
            value.SetTtl(_ttl);
            var added = _manager.TryAdd(connectionId, value);
            if (!added)
            {
                var log = LogBuilder.New()
                    .WithAction(LogAction.Update)
                    .WithCeverity(LogCeverity.Warning)
                    .WithFunctionName($"Insert - manager")
                    .WithDescription("Key already exists in game cache")
                    .Build();

                platformClient.CreateSystemLogAsync(log);
                logger.LogWarning("Key already exists in game cache");
                return new Error(Error.ErrorType.KeyExists, $"Insert failed: connectionId '{connectionId}' already exists");
            }

            return Result<Error>.Ok;
        }
        catch (OverflowException error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Create)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("Insert")
                .WithDescription("HubConnectionManager overflow on insert")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "Insert - Cache overflow");
            return new Error(Error.ErrorType.Overflow, "Insert failed: cache overflow while inserting hub connection entry");
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Create)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("Insert")
                .WithDescription("Insert into HubConnectionManager threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(Insert));
            return new Error(Error.ErrorType.System, "Insert failed: unexpected exception while inserting hub connection manager entry");
        }
    }

    public Result<Option<HubInfo>, Error> Remove(string connectionId)
    {
        try
        {
            if (connectionId == string.Empty || connectionId is null)
            {
                return new Error(Error.ErrorType.NullReference, "Remove failed: connectionId was null or empty");
            }

            if (!_manager.TryRemove(connectionId, out var value))
            {
                return Option<HubInfo>.None;
            }

            if (value is null)
            {
                return Option<HubInfo>.None;
            }

            return Option<HubInfo>.Some(value);
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Delete)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("Remove")
                .WithDescription("Remove from HubConnectionManager threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(Remove));
            return new Error(Error.ErrorType.System, "Remove failed: unexpected exception while removing hub connection manager entry");
        }
    }
}