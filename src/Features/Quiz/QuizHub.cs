using System.Threading.Tasks.Dataflow;
using Microsoft.AspNetCore.SignalR;
using tero.session.src.Core;
using tero.session.src.Features.Platform;

namespace tero.session.src.Features.Quiz;

public class QuizHub(GameSessionCache<QuizSession> cache, HubConnectionManager<QuizSession> manager, ILogger<QuizHub> logger, PlatformClient platformClient) : Hub
{
    public override async Task OnConnectedAsync()
    {
        try
        {
            await base.OnConnectedAsync();
            logger.LogDebug("Client connected to QuizSession");
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Other)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("OnConnectedAsync")
                .WithDescription("QuizHub OnConnectedAsync failed")
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
            var result = manager.Get(Context.ConnectionId);
            if (result.IsErr())
            {
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var option = result.Unwrap();
            if (option.IsNone())
            {
                // HERE IT FAILS
                var log = LogBuilder.New()
                    .WithAction(LogAction.Delete)
                    .WithCeverity(LogCeverity.Warning)
                    .WithFunctionName("OnDisconnectedAsync")
                    .WithDescription("Failed to get disconnecting user's data to gracefully remove")
                    .Build();

                platformClient.CreateSystemLogAsync(log);
                logger.LogError("Failed to get diconnecting users data to gracefully remove");

                await base.OnDisconnectedAsync(exception);
                return;
            }

            var hubInfo = option.Unwrap();
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, hubInfo.GameKey);
            await base.OnDisconnectedAsync(exception);
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Delete)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("OnDisconnectedAsync")
                .WithDescription("QuizHub OnDisconnectedAsync threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(OnDisconnectedAsync));
        }
    }

    public async Task ConnectToGroup(string key)
    {
        try
        {
            if (string.IsNullOrEmpty(key))
            {
                logger.LogWarning("Key was empty");
                await CoreUtils.Broadcast(Clients, Error.NullReference, logger, platformClient);
                return;
            }

            var removeOldResult = manager.Remove(Context.ConnectionId);
            if (removeOldResult.IsOk())
            {
                var removeOldOption = removeOldResult.Unwrap();
                if (removeOldOption.IsSome())
                {
                    logger.LogWarning("New connection had old connection in cache");
                    var entry = removeOldOption.Unwrap();
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, entry.GameKey);
                }
            }
            else
            {
                var log = LogBuilder.New()
                    .WithAction(LogAction.Create)
                    .WithCeverity(LogCeverity.Critical)
                    .WithFunctionName("ConnectToGroup - QuizHub")
                    .WithDescription("Failed to remove old entry from manager cache")
                    .Build();

                platformClient.CreateSystemLogAsync(log);
                logger.LogError("ConnectToGroup: Failed to remove old entry from manager cache");
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

            logger.LogInformation("User added to group: {string}", key);
            await Groups.AddToGroupAsync(Context.ConnectionId, key);
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Other)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("ConnectToGroup")
                .WithDescription("Failed to Conenct user to SpinSession Hub group")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(AddQuestion));
        }
    }

    public async Task AddQuestion(string key, string question)
    {
        try
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(question))
            {
                logger.LogWarning("Key or question was empty");
                await CoreUtils.Broadcast(Clients, Error.NullReference, logger, platformClient);
                return;
            }

            var result = await cache.Upsert(key, session => session.AddQuesiton(question));
            if (result.IsErr())
            {
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var session = result.Unwrap();
            logger.LogInformation("Adding question: {string}, to game: {string}", question, key);
            await Clients.Groups(key).SendAsync("iterations", session.GetIterations());
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Create)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("AddQuestion")
                .WithDescription("Add Question to QuizSession threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(AddQuestion));
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

            var result = await cache.Upsert(key, session => session.Start());
            if (result.IsErr())
            {
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var session = result.Unwrap();
            await Clients.Caller.SendAsync("session", session);
            await Clients.OthersInGroup(key).SendAsync("state", "Game has started");

            var removeResult = await cache.Remove(key);
            if (removeResult.IsErr())
            {
                logger.LogError("Failed to remove game");
                await CoreUtils.Broadcast(Clients, removeResult.Err(), logger, platformClient);
                return;
            }

            var persistResult = await platformClient.PersistGame(GameType.Quiz, session);
            if (persistResult.IsErr())
            {
                logger.LogError("Failed to persist game after starting");
            }

            await platformClient.FreeGameKey(key);
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Update)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("StartGame")
                .WithDescription("Start QuizSession threw an exception")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "StartGame");
        }
    }
}