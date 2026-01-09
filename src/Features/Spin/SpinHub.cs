using System.Data;
using System.Net.Sockets;
using System.Threading.Tasks.Dataflow;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.ObjectPool;
using tero.session.src.Core;
using tero.session.src.Features.Platform;

namespace tero.session.src.Features.Spin;

public class SpinHub(ILogger<SpinHub> logger, HubConnectionManager<SpinSession> manager, GameSessionCache<SpinSession> cache, PlatformClient platformClient) : Hub
{
    public override async Task OnConnectedAsync()
    {
        try
        {
            await base.OnConnectedAsync();
            logger.LogDebug("Client connected to SpinSession");
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Other)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("OnConnectedAsync")
                .WithDescription("SpinHub OnConnectedAsync threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(OnConnectedAsync));
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
                    .WithDescription("Failed to get disconnecting user's data to gracefully remove")
                    .Build();

                platformClient.CreateSystemLogAsync(log);
                logger.LogError("Failed to get disconnecting user's data to gracefully remove");

                await base.OnDisconnectedAsync(exception);
                return;
            }

            var hubInfo = option.Unwrap();
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, hubInfo.GameKey);

            var upsertResult = await cache.Upsert(hubInfo.GameKey, session => session.RemoveUser(hubInfo.UserId));
            if (upsertResult.IsErr())
            {
                await CoreUtils.Broadcast(Clients, upsertResult.Err(), logger, platformClient);
                await base.OnDisconnectedAsync(exception);
                return;
            }

            var session = upsertResult.Unwrap();
            var minPlayers = session.SelectionSize + 1;

            if (session.UsersCount() < minPlayers)
            {
                await Task.WhenAll(
                    cache.Remove(hubInfo.GameKey),
                    Clients.Group(hubInfo.GameKey).SendAsync("state", SpinGameState.Finished),
                    Clients.Group(hubInfo.GameKey).SendAsync("cancelled", $"En spiller har forlatt spillet. Det må være minst {minPlayers} spillere")
                );
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
            logger.LogError(error, nameof(OnDisconnectedAsync));
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

            var getResult = await cache.Get(key);
            if (getResult.IsErr())
            {
                await CoreUtils.Broadcast(Clients, getResult.Err(), logger, platformClient);
                return;
            }

            var iterations = getResult.Unwrap().GetIterations();
            await Clients.Caller.SendAsync("iterations", iterations);

            var result = await cache.Upsert(
                key,
                session => session.AddUser(userId)
            );

            if (result.IsErr())
            {
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var session = result.Unwrap();
            await Clients.Group(key).SendAsync("host", session.HostId);

            var insertResult = manager.Insert(Context.ConnectionId, new HubInfo(key, userId));
            if (insertResult.IsErr())
            {
                await CoreUtils.Broadcast(Clients, insertResult.Err(), logger, platformClient);
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, key);
            await Clients.Group(key).SendAsync("players_count", session.UsersCount());
            logger.LogInformation("User added to SpinSession");
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Create)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("AddUser")
                .WithDescription("add user to SpinSession threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(ConnectToGroup));
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
            logger.LogDebug("User added a round to SpinSession");
            await Clients.Group(key).SendAsync("iterations", session.GetIterations());
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Create)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("AddRound")
                .WithDescription("Add round to SpinSession threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(AddRound));
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

            var result = await cache.Upsert(
                key,
                session => session.StartGame()
            );

            if (result.IsErr())
            {
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var session = result.Unwrap();
            var roundText = session.GetRoundText();

            await Task.WhenAll(
                Clients.Group(key).SendAsync("state", session.State),
                Clients.Group(key).SendAsync("signal_start", true),
                Clients.Caller.SendAsync("round", roundText),
                platformClient.PersistGame(GameType.Roulette, key, session) // GameType here does not matter if its Roulette or Duel
            );
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Update)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("StartGame")
                .WithDescription("Start SpinSession threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(StartGame));
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
                if (result.Err() == Error.GameFinished)
                {
                    await Clients.Group(key).SendAsync("state", SpinGameState.Finished);
                    return;
                }
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var session = result.Unwrap();
            await Clients.Group(key).SendAsync("state", SpinGameState.RoundInProgress);
            await Clients.Caller.SendAsync("round_text", session.GetRoundText());
            var userIds = session.GetUserIds();
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
                    await Task.Delay(500);
                }
            }

            await Clients.Group(key).SendAsync("selected", selected);
            await Clients.Group(key).SendAsync("state", SpinGameState.RoundFinished);
            logger.LogInformation("Round players selected for SpinSession");
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

    public async Task NextRound(string key)
    {
        try
        {
            if (string.IsNullOrEmpty(key))
            {
                logger.LogWarning("Key was empty");
                await CoreUtils.Broadcast(Clients, Error.NullReference, logger, platformClient);
                return;
            }

            var result = await cache.Upsert(
                key,
                session => session.NextRound()
            );

            if (result.IsErr())
            {
                if (result.Err() == Error.GameFinished)
                {
                    await Clients.Group(key).SendAsync("state", SpinGameState.Finished);
                    return;
                }
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var updatedSession = result.Unwrap();
            var round = updatedSession.GetRoundText();

            await Clients.Caller.SendAsync("round", round);
            await Clients.Group(key).SendAsync("state", updatedSession.State);

            logger.LogDebug("SpinSession round initialized");
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Update)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("NextRound")
                .WithDescription("Next SpinSession round threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(NextRound));
        }
    }
}