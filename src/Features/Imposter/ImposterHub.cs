using Microsoft.AspNetCore.SignalR;
using tero.session.src.Core;
using tero.session.src.Features.Platform;
using tero.session.src.Features.Spin;

namespace tero.session.src.Features.Imposter;

public class ImposterHub(ILogger<SpinHub> logger, HubConnectionManager<ImposterSession> manager, GameSessionCache<ImposterSession> cache, PlatformClient platformClient) : Hub
{
    private const uint MIN_ITERATIONS = 1; // TODO make 10

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
                var log = LogBuilder.New()
                    .WithAction(LogAction.Delete)
                    .WithCeverity(LogCeverity.Warning)
                    .WithFunctionName("OnDisconnectedAsync")
                    .WithDescription("ImposterHub: Failed to get disconnecting user's data to gracefully remove")
                    .Build();

                platformClient.CreateSystemLogAsync(log);
                logger.LogError("Failed to get disconnecting user's data to gracefully remove");

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
                    await base.OnDisconnectedAsync(exception);
                    return;
                }
                await CoreUtils.Broadcast(Clients, getResult.Err(), logger, platformClient);
                await base.OnDisconnectedAsync(exception);
                return;
            }

            var upsertResult = await cache.Upsert(hubInfo.GameKey, session => session.RemovePlayer(hubInfo.UserId));
            if (upsertResult.IsErr())
            {
                await CoreUtils.Broadcast(Clients, upsertResult.Err(), logger, platformClient);
                await base.OnDisconnectedAsync(exception);
                return;
            }

            var session = upsertResult.Unwrap();
            var minPlayers = 3;

            // Only cancel the game if it has started and too few players remain
            if (session.State != ImposterGameState.Finished
                && session.State != ImposterGameState.Created
                && session.State != ImposterGameState.Initialized
                && session.UsersCount() < minPlayers)
            {
                await cache.Remove(hubInfo.GameKey);
                await Clients.Group(hubInfo.GameKey).SendAsync("cancelled", $"En spiller har forlatt spillet. Det må være minst {minPlayers} spillere");
                await platformClient.FreeGameKey(hubInfo.GameKey);
                await base.OnDisconnectedAsync(exception);
                return;
            }

            // If all players have left, clean up the game
            if (session.UsersCount() == 0)
            {
                await cache.Remove(hubInfo.GameKey);
                await platformClient.FreeGameKey(hubInfo.GameKey);
                await base.OnDisconnectedAsync(exception);
                return;
            }

            await Task.WhenAll(
                Clients.Group(hubInfo.GameKey).SendAsync("host", session.HostId),
                Clients.Group(hubInfo.GameKey).SendAsync("players_count", session.UsersCount()),
                base.OnDisconnectedAsync(exception)
            );
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Delete)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("OnDisconnectedAsync")
                .WithDescription("SpinHub OnDisconnectedAsync threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "OnDisconnectedAsync");
        }
    }

    public async Task ConnectToGroup(string key, Guid userId)
    {
        try
        {
            logger.LogInformation("Connecting user to group: {string}", key);
            if (string.IsNullOrEmpty(key))
            {
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
            else
            {
                var log = LogBuilder.New()
                    .WithAction(LogAction.Create)
                    .WithCeverity(LogCeverity.Critical)
                    .WithFunctionName("ConnectToGroup - SpinHub")
                    .WithDescription("Failed to remove old entry from manager cache")
                    .Build();

                platformClient.CreateSystemLogAsync(log);
                logger.LogError("ConnectToGroup: Failed to remove old entry from manager cache");
            }

            var result = await cache.Upsert(key, session => session.AddPlayer(userId));
            if (result.IsErr())
            {
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var session = result.Unwrap();

            var insertResult = manager.Insert(Context.ConnectionId, new HubInfo(key, userId));

            if (insertResult.IsErr())
            {
                await CoreUtils.Broadcast(Clients, insertResult.Err(), logger, platformClient);
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, key);

            await Task.WhenAll(
                Clients.Group(key).SendAsync("host", session.HostId.ToString()),
                Clients.Group(key).SendAsync("iterations", session.GetIterations()),
                Clients.Group(key).SendAsync("players_count", session.UsersCount())
            );
            logger.LogInformation("User added to ImposterSession");
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Create)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("ConnectToGroup")
                .WithDescription("ImposterSession: Add user threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "ConnectToGroup2");
        }
    }

    public async Task AddPlayer(string key, Guid playerId)
    {
        try
        {
            var result = await cache.Upsert(key, session => session.AddPlayer(playerId));
            if (result.IsErr())
            {
                logger.LogError("Failed to manually add user: {Error}", result.Err());
                await Clients.Caller.SendAsync("error", "Klarte ikke legge til spiller, forsøk igjen senere.");
                return;
            }
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Create)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("AddPlayer")
                .WithDescription("ImposterSession: Add player threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "AddPlayer");
            return;
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

            var result = await cache.Upsert(
                key,
                session => session.AddRound(round)
            );

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
            var log = LogBuilder.New()
                .WithAction(LogAction.Create)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("AddRound")
                .WithDescription("ImposterSession: Add round threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(AddRound));
        }
    }

    public async Task<bool> StartGame(string key)
    {
        try
        {
            if (string.IsNullOrEmpty(key))
            {
                logger.LogWarning("Key was empty");
                await CoreUtils.Broadcast(Clients, Error.NullReference, logger, platformClient);
                return false;
            }

            var sessionResult = await cache.Get(key);
            if (sessionResult.IsErr())
            {
                await CoreUtils.Broadcast(Clients, sessionResult.Err(), logger, platformClient);
                return false;
            }

            var session = sessionResult.Unwrap();
            if (session.GetIterations() < MIN_ITERATIONS)
            {
                await Clients.Caller.SendAsync("error", $"Minimum {MIN_ITERATIONS} runder for å starte spillet");
                return false;
            }

            var minPlayers = 3;
            if (session.UsersCount() < minPlayers)
            {
                await Clients.Caller.SendAsync("error", $"Minimum {minPlayers} spillere for å starte spillet");
                return false;
            }

            var result = await cache.Upsert(
                key,
                session => session.StartGame()
            );

            if (result.IsErr())
            {
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return false;
            }

            session = result.Unwrap();
            var roundText = session.GetRoundWord();

            await Task.WhenAll(
                Clients.Group(key).SendAsync("state", session.State),
                // Clients.Group(key).SendAsync("signal_start", true), 
                Clients.Caller.SendAsync("round_text", roundText),
                platformClient.PersistGame(GameType.Imposter, session) // GameType here does not matter if its Roulette or Duel
            );

            return true;
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Update)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("StartGame")
                .WithDescription("ImposterSession: Start game threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "StartGame");
            return false;
        }
    }

    public async Task StartRound(string key)
    {
        try
        {
            if (string.IsNullOrEmpty(key))
            {
                logger.LogWarning("Key was empty");
                await CoreUtils.Broadcast(Clients, Error.NullReference, logger, platformClient);
                return;
            }

            var result = await cache.Get(key);
            if (result.IsErr())
            {
                await platformClient.FreeGameKey(key);
                if (result.Err() == Error.GameNotFound)
                {
                    await Clients.Caller.SendAsync("state", ImposterGameState.Finished);
                    return;
                }
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var session = result.Unwrap();
            await Clients.Group(key).SendAsync("state", ImposterGameState.Started);

            var userIds = session.GetUserIds();
            var imposter = session.GetImposter();
            if (imposter == Guid.Empty)
            {
                logger.LogWarning("No players in the game!");
                await Clients.Group(key).SendAsync("cancelled", $"En feil har skjedd, forsøk å starte ett nytt spill");
                return;
            }

            await Clients.Group(key).SendAsync("round_word", (session.GetRoundWord(), userIds));
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Update)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("StartRound")
                .WithDescription("Start SpinSession round threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(StartRound));
        }
    }
}