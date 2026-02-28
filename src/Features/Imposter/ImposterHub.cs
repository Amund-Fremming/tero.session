using Microsoft.AspNetCore.SignalR;
using tero.session.src.Core;
using tero.session.src.Features.Platform;

namespace tero.session.src.Features.Imposter;

public class ImposterHub(
    ILogger<ImposterHub> logger,
    HubConnectionManager<ImposterSession> manager,
    GameSessionCache<ImposterSession> cache,
    PlatformClient platformClient) : Hub
{
    private const uint MIN_ITERATIONS = 1;
    private const uint MIN_PLAYERS = 3;
    public override async Task OnConnectedAsync()
    {
        try
        {
            await base.OnConnectedAsync();
            logger.LogDebug("Client connected to ImposterSession");
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Other)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("OnConnectedAsync")
                .WithDescription("ImposterHub: OnConnectedAsync threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "OnConnectedAsync");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var result = manager.Remove(Context.ConnectionId);
            if (result.IsErr())
            {
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                await base.OnDisconnectedAsync(exception);
                return;
            }

            var option = result.Unwrap();
            if (option.IsNone())
            {
                logger.LogWarning("Failed to get disconnecting user's data to gracefully remove");
                await base.OnDisconnectedAsync(exception);
                return;
            }

            var hubInfo = option.Unwrap();
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, hubInfo.GameKey);

            var getResult = await cache.Get(hubInfo.GameKey);
            if (getResult.IsErr())
            {
                if (getResult.Err() == Error.GameNotFound)
                {
                    logger.LogDebug("Game already removed during disconnect for key: {GameKey}", hubInfo.GameKey);
                }
                await base.OnDisconnectedAsync(exception);
                return;
            }

            var session = getResult.Unwrap();
            if (hubInfo.UserId == session.HostId)
            {
                await cache.Remove(hubInfo.GameKey);
            }

            await base.OnDisconnectedAsync(exception);
        }
        catch (Exception error)
        {
            logger.LogError(error, "OnDisconnectedAsync");
            CoreUtils.LogCriticalError(platformClient, "OnDisconnectedAsync", "ImposterHub OnDisconnectedAsync threw an exception", error);
        }
    }

    public async Task ConnectToGroup(string key)
    {
        try
        {
            if (string.IsNullOrEmpty(key))
            {
                logger.LogWarning("Received an empty game key");
                await CoreUtils.Broadcast(Clients, Error.NullReference, logger, platformClient);
                return;
            }

            var removeOldResult = manager.Remove(Context.ConnectionId);
            if (removeOldResult.IsOk())
            {
                var removeOldOption = removeOldResult.Unwrap();
                if (removeOldOption.IsSome())
                {
                    var entry = removeOldOption.Unwrap();
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, entry.GameKey);
                }
            }

            var result = await cache.Get(key);
            if (result.IsErr())
            {
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var session = result.Unwrap();
            await Clients.Caller.SendAsync("iterations", session.GetIterations());

            var managerResult = manager.Insert(Context.ConnectionId, new HubInfo(key));
            if (managerResult.IsErr())
            {
                await CoreUtils.Broadcast(Clients, managerResult.Err(), logger, platformClient);
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, key);
            logger.LogInformation("User added to ImposterSession");
        }
        catch (Exception error)
        {
            logger.LogError(error, nameof(ConnectToGroup));
            CoreUtils.LogCriticalError(platformClient, "ConnectToGroup", "Add user to ImposterSession threw an exception", error);
        }
    }

    public async Task<bool> AddPlayers(string key, HashSet<string> players)
    {
        try
        {
            var result = await cache.Upsert(key, session => session.AddPlayers(players));
            if (result.IsErr())
            {
                logger.LogError("Failed to manually add user: {Error}", result.Err());
                await Clients.Caller.SendAsync("error", "Klarte ikke legge til spiller, forsøk igjen senere.");
                return false;
            }

            return true;
        }
        catch (Exception error)
        {
            logger.LogError(error, "AddPlayer");
            CoreUtils.LogCriticalError(platformClient, "AddPlayer", "ImposterSession: Add player threw an exception", error);
            return false;
        }
    }

    public async Task AddRound(string key, string round)
    {
        try
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(round))
            {
                logger.LogWarning("Key or round was empty");
                await CoreUtils.Broadcast(Clients, Error.NullReference, logger, platformClient);
                return;
            }

            var result = await cache.Upsert(key, session => session.AddRound(round));
            if (result.IsErr())
            {
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var session = result.Unwrap();
            logger.LogDebug("User added a round to ImposterSession");
            await Clients.Group(key).SendAsync("iterations", session.GetIterations());
        }
        catch (Exception error)
        {
            logger.LogError(error, nameof(AddRound));
            CoreUtils.LogCriticalError(platformClient, "AddRound", "ImposterSession: Add round threw an exception", error);
        }
    }

    public async Task StartGame(string key)
    {
        try
        {
            if (string.IsNullOrEmpty(key))
            {
                logger.LogWarning("Key was empty");
                await CoreUtils.Broadcast(Clients, Error.NullReference, logger, platformClient);
                return;
            }

            var sessionResult = await cache.Get(key);
            if (sessionResult.IsErr())
            {
                await CoreUtils.Broadcast(Clients, sessionResult.Err(), logger, platformClient);
                return;
            }

            var session = sessionResult.Unwrap();
            if (session.GetIterations() < MIN_ITERATIONS)
            {
                await Clients.Caller.SendAsync("error", $"Minimum {MIN_ITERATIONS} runder for å starte spillet");
                return;
            }

            if (session.PlayersCount() < MIN_PLAYERS)
            {
                await Clients.Caller.SendAsync("error", $"Minimum {MIN_PLAYERS} spillere for å starte spillet");
                return;
            }

            await Clients.Caller.SendAsync("session", session);
            await Clients.OthersInGroup(key).SendAsync("signal_start", true);

            var removeResult = await cache.Remove(key);
            if (removeResult.IsErr())
            {
                logger.LogError("Failed to remove game");
            }

            var persistResult = await platformClient.PersistGame(GameType.Imposter, session);
            if (persistResult.IsErr())
            {
                logger.LogError("Failed to persist game after starting");
            }

            await platformClient.FreeGameKey(key);
        }
        catch (Exception error)
        {
            logger.LogError(error, "StartGame");
            CoreUtils.LogCriticalError(platformClient, "StartGame", "ImposterSession: Start game threw an exception", error);
        }
    }
}