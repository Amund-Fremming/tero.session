using System.ComponentModel;
using System.Linq.Expressions;
using System.Net.Sockets;
using System.Threading.Tasks.Dataflow;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.ObjectPool;
using tero.session.src.Core;
using tero.session.src.Features.Platform;

namespace tero.session.src.Features.Spin;


public class SpinHub(ILogger<SpinHub> logger, HubConnectionManager<SpinSession> manager, GameSessionCache<SpinSession> cache, PlatformClient platformClient) : Hub
{
    private const uint MIN_ITERATIONS = 10;

    public override async Task OnConnectedAsync()
    {
        try
        {
            await base.OnConnectedAsync();
            logger.LogDebug("Client connected to SpinSession");
        }
        catch (Exception error)
        {
            logger.LogError(error, "OnConnectedAsync");
            CoreUtils.LogCriticalError(platformClient, "OnConnectedAsync", "SpinHub: OnConnectedAsync threw an exception", error);
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
                if (getResult.Err().Type == Error.ErrorType.GameNotFound)
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
            var minPlayers = session.SelectionSize + 1;

            if (session.State != SpinGameState.Finished
                && session.State != SpinGameState.Created
                && session.State != SpinGameState.Initialized
                && session.PlayersCount() < minPlayers)
            {
                await cache.Remove(hubInfo.GameKey);
                await Clients.Group(hubInfo.GameKey).SendAsync("cancelled", $"En spiller har forlatt spillet. Det må være minst {minPlayers} spillere for å fortsette");
                await platformClient.FreeGameKey(hubInfo.GameKey);
                await base.OnDisconnectedAsync(exception);
                return;
            }

            if (session.PlayersCount() == 0)
            {
                await cache.Remove(hubInfo.GameKey);
                await platformClient.FreeGameKey(hubInfo.GameKey);
                await base.OnDisconnectedAsync(exception);
                return;
            }

            await Task.WhenAll(
                Clients.Group(hubInfo.GameKey).SendAsync("host", session.HostId),
                Clients.Group(hubInfo.GameKey).SendAsync("players_count", session.PlayersCount()),
                base.OnDisconnectedAsync(exception)
            );
        }
        catch (Exception error)
        {
            logger.LogError(error, nameof(OnDisconnectedAsync));
            CoreUtils.LogCriticalError(platformClient, "OnDisconnectedAsync", "SpinHub OnDisconnectedAsync threw an exception", error);
        }
    }

    /// <summary>
    /// Returns true if game has started   
    /// </summary>
    public async Task<bool> ConnectToGroup(string key, Guid userId)
    {
        try
        {
            logger.LogInformation("Connecting user to group: {string}", key);
            if (string.IsNullOrEmpty(key))
            {
                logger.LogWarning("Received a empty game key");
                await CoreUtils.Broadcast(Clients, Error.NullReference, logger, platformClient);
                return false;
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
                logger.LogWarning("ConnectToGroup: Failed to remove old entry from manager cache");
            }

            var result = await cache.Upsert(key, session => session.AddPlayer(userId));
            if (result.IsErr())
            {

                if (result.Err().Type == Error.ErrorType.IdConflict)
                {
                    await Clients.Caller.SendAsync("error", "En spiller med din id har allerede blitt med");
                    await base.OnDisconnectedAsync(new Exception(string.Empty));
                    return false;
                }
                if (result.Err().Type == Error.ErrorType.GameClosed)
                {
                    await Clients.Caller.SendAsync("error", "Spillet har allerede startet");
                    await base.OnDisconnectedAsync(new Exception(string.Empty));
                    return false;
                }

                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return false;
            }

            var session = result.Unwrap();
            var minPlayers = session.SelectionSize + 1;
            if (session.State != SpinGameState.Created && session.State != SpinGameState.Initialized && session.PlayersCount() < minPlayers)
            {
                await platformClient.FreeGameKey(key);
                await Clients.Group(key).SendAsync("state", SpinGameState.Finished);
                return false;
            }

            var insertResult = manager.Insert(Context.ConnectionId, new HubInfo(key, userId));

            if (insertResult.IsErr())
            {
                await CoreUtils.Broadcast(Clients, insertResult.Err(), logger, platformClient);
                return false;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, key);


            await Task.WhenAll(
                Clients.Group(key).SendAsync("host", session.HostId.ToString()),
                Clients.Group(key).SendAsync("iterations", session.GetIterations()),
                Clients.Group(key).SendAsync("players_count", session.PlayersCount())
            );

            logger.LogInformation("User added to SpinSession");

            if (session.State == SpinGameState.RoundFinished || session.State == SpinGameState.RoundInProgress || session.State == SpinGameState.RoundStarted)
            {
                return true;
            }

            return false;
        }
        catch (Exception error)
        {
            logger.LogError(error, nameof(ConnectToGroup));
            CoreUtils.LogCriticalError(platformClient, "AddUser", "Add user to SpinSession threw an exception", error);
            return false;
        }
    }

    public async Task AddRound(string key, string round)
    {
        try
        {
            round = round.Trim();
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(round))
            {
                logger.LogWarning("Key or round was empty");
                await CoreUtils.Broadcast(Clients, Error.NullReference, logger, platformClient);
                return;
            }

            if (round.Length > 80)
            {
                await Clients.Caller.SendAsync("error", "Teksten kan ikke være lengre enn 80 tegn.");
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
            logger.LogDebug("User added a round to SpinSession");
            await Clients.Group(key).SendAsync("iterations", session.GetIterations());
        }
        catch (Exception error)
        {
            logger.LogError(error, nameof(AddRound));
            CoreUtils.LogCriticalError(platformClient, "AddRound", "Add round to SpinSession threw an exception", error);
        }
    }

    public async Task<bool> PersistGame(string key, GameType gameType, string name, GameCategory category)
    {
        try
        {
            var result = await cache.Get(key);
            if (result.IsErr())
            {
                logger.LogError("Failed to get game from cache");
                return false;
            }

            var session = result.Unwrap();
            var persistResult = await platformClient.PersistGame(name, category, gameType, session);
            if (persistResult.IsErr())
            {
                logger.LogError("Failed to persist spin game with key {string}", key);
                return false;
            }
            return true;
        }
        catch (Exception error)
        {
            logger.LogError(error, nameof(PersistGame));
            CoreUtils.LogCriticalError(platformClient, nameof(PersistGame), "PersistGame SpinSession threw an exception", error);
            return false;
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

            var minPlayers = session.SelectionSize + 1;
            if (session.PlayersCount() < minPlayers)
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
            var roundText = session.GetRoundText();

            await Task.WhenAll(
                Clients.Group(key).SendAsync("state", session.State),
                Clients.Group(key).SendAsync("signal_start", true),
                Clients.Group(key).SendAsync("round_text", roundText)
            );

            return true;
        }
        catch (Exception error)
        {
            logger.LogError(error, nameof(StartGame));
            CoreUtils.LogCriticalError(platformClient, "StartGame", "Start SpinSession threw an exception", error);
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
                if (result.Err().Type == Error.ErrorType.GameNotFound)
                {
                    await Clients.Caller.SendAsync("state", SpinGameState.Finished);
                    return;
                }
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var session = result.Unwrap();
            await Clients.Group(key).SendAsync("state", SpinGameState.RoundInProgress);
            await Clients.Caller.SendAsync("round_text", session.GetRoundText());
            var userIds = session.GetPlayerIds();
            var selected = session.GetSpinResult(session.SelectionSize);
            if (selected.Count == 0)
            {
                logger.LogWarning("No players in the game!");
                return;
            }

            var rng = new Random();
            int spinRounds = rng.Next(3, 8);

            for (var i = 0; i < spinRounds; i++)
            {
                for (var j = 0; j < userIds.Count; j++)
                {
                    var batch = new List<Guid>();
                    for (var k = 0; k < session.SelectionSize; k++)
                    {
                        var userId = userIds[(j + k) % userIds.Count];
                        batch.Add(userId);
                    }

                    await Clients.Group(key).SendAsync("selected", batch);
                    await Task.Delay(250);
                }
            }

            await Clients.Group(key).SendAsync("selected", selected);
            await Clients.Group(key).SendAsync("state", SpinGameState.RoundFinished);
            logger.LogDebug("Round players selected for SpinSession");
        }
        catch (Exception error)
        {
            logger.LogError(error, nameof(StartRound));
            CoreUtils.LogCriticalError(platformClient, "StartRound", "Start SpinSession round threw an exception", error);
        }
    }

    public async Task<SpinGameState> NextRound(string key)
    {
        try
        {
            if (string.IsNullOrEmpty(key))
            {
                logger.LogWarning("Key was empty");
                await CoreUtils.Broadcast(Clients, Error.NullReference, logger, platformClient);
                return SpinGameState.Finished;
            }

            var result = await cache.Upsert(
                key,
                session => session.NextRound()
            );

            if (result.IsErr())
            {
                await platformClient.FreeGameKey(key);
                if (result.Err().Type == Error.ErrorType.GameFinished)
                {
                    await Clients.Group(key).SendAsync("state", SpinGameState.Finished);
                    return SpinGameState.Finished;
                }

                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return SpinGameState.Finished;
            }

            var updatedSession = result.Unwrap();
            var round = updatedSession.GetRoundText();

            await Clients.Group(key).SendAsync("round_text", round);
            await Clients.Group(key).SendAsync("state", updatedSession.State);

            logger.LogDebug("SpinSession round initialized");
            return updatedSession.State;
        }
        catch (Exception error)
        {
            logger.LogError(error, nameof(NextRound));
            CoreUtils.LogCriticalError(platformClient, "NextRound", "Next SpinSession round threw an exception", error);
            await platformClient.FreeGameKey(key);
            return SpinGameState.Finished;
        }
    }
}