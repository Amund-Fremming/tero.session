using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using tero.session.src.Features.Platform;
using tero.session.src.Features.Quiz;

namespace tero.session.src.Core;

public static class CoreUtils
{
    public static (int, string) InsertPayload<TSession>(PlatformClient platformClient, GameSessionCache<TSession> cache, string key, JsonElement value)
    {
        try
        {
            var session = JsonSerializer.Deserialize<TSession>(value);
            if (session is null)
            {
                return (400, "Invalid payload");
            }

            var result = cache.Insert(key, session);
            if (result.IsErr())
            {
                return result.Err() switch
                {
                    Error.KeyExists => (409, "Game key in use"),
                    _ => (500, "Internal server error")
                };
            }

            return (200, "Game initialized");
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Update)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("InsertPayload")
                .WithDescription("Insert payload to cache threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            return (500, "Internal server error");
        }
    }

    public static async Task Broadcast(IHubCallerClients clients, Error error, ILogger logger, PlatformClient platformClient)
    {
        try
        {
            logger.LogError("Broadcast recieved error: {Error}", error);
            switch (error)
            {
                case Error.KeyExists:
                    await clients.Caller.SendAsync("error", "Spill nøkkelen er allerede i bruk");
                    break;
                case Error.NotGameHost:
                    await clients.Caller.SendAsync("error", "Denne handlingen kan bare en host gjøre");
                    break;
                case Error.GameClosed:
                    await clients.Caller.SendAsync("error", "Spillet er lukket for fler handlinger");
                    break;
                case Error.GameFinished:
                    await clients.Caller.SendAsync("error", "Spillet er ferdig");
                    break;
                case Error.GameNotFound:
                    await clients.Caller.SendAsync("error", "Spillet finnes ikke");
                    break;
                case Error.System:
                    await clients.Caller.SendAsync("error", "En feil har skjedd, forsøk igjen senere");
                    break;
                case Error.Json:
                    await clients.Caller.SendAsync("error", "En feil har skjedd, forsøk på nytt");
                    break;
                case Error.NullReference:
                    await clients.Caller.SendAsync("error", "Mottok en tom verdi, forsøk på nytt");
                    break;
                case Error.Overflow:
                    await clients.Caller.SendAsync("error", "En feil har skjedd, forsøk på nytt");
                    break;
                case Error.Http:
                    await clients.Caller.SendAsync("error", "En feil har skjedd, forsøk på nytt");
                    break;
                case Error.Upstream:
                    await clients.Caller.SendAsync("error", "En feil har skjedd, forsøk på nytt");
                    break;
            }
        }
        catch (Exception ex)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Other)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("Broadcast")
                .WithDescription("Broadcast error threw an exception")
                .WithMetadata(ex)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(ex, nameof(Broadcast));
        }
    }

    public static void LogCriticalError(PlatformClient platformClient, string functionName, string description, Exception error)
    {
        var log = LogBuilder.New()
            .WithAction(LogAction.Other)
            .WithCeverity(LogCeverity.Critical)
            .WithFunctionName(functionName)
            .WithDescription(description)
            .WithMetadata(error)
            .Build();

        platformClient.CreateSystemLogAsync(log);
    }

    public static SerializableError ToSerializable(this Exception ex)
        => new()
        {
            Type = ex.GetType().Name,
            Message = ex.Message,
            StackTrace = ex.StackTrace,
        };
}