using System.Threading.Tasks.Dataflow;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.ObjectPool;
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
            logger.LogError(error, nameof(OnConnectedAsync));
            CoreUtils.LogCriticalError(platformClient, "OnConnectedAsync", "QuizHub OnConnectedAsync failed", error);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var result = manager.Get(Context.ConnectionId);
            if (result.IsErr())
            {
                logger.LogError("Failed to get connection from manager cache");
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
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
            await base.OnDisconnectedAsync(exception);
        }
        catch (Exception error)
        {
            logger.LogError(error, nameof(OnDisconnectedAsync));
            CoreUtils.LogCriticalError(platformClient, "OnDisconnectedAsync", "QuizHub OnDisconnectedAsync threw an exception", error);
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
                logger.LogWarning("ConnectToGroup: Failed to remove old entry from manager cache");
            }

            var result = await cache.Get(key);
            if (result.IsErr())
            {
                logger.LogError("Failed to get game from game cache");
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var session = result.Unwrap();
            await Clients.Caller.SendAsync("iterations", session.GetIterations());

            var managerResult = manager.Insert(Context.ConnectionId, new HubInfo(key));
            if (managerResult.IsErr())
            {
                logger.LogError("Failed to insert hub info into game manager");
                await CoreUtils.Broadcast(Clients, managerResult.Err(), logger, platformClient);
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, key);
            logger.LogInformation("Connected user to group {string}", key);
        }
        catch (Exception error)
        {
            logger.LogError(error, nameof(ConnectToGroup));
            CoreUtils.LogCriticalError(platformClient, "ConnectToGroup", "Failed to connect user to QuizSession Hub group", error);
        }
    }

    public async Task AddQuestion(string key, string question)
    {
        try
        {
            question = question.Trim();
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(question))
            {
                logger.LogWarning("Key or question was empty");
                await CoreUtils.Broadcast(Clients, Error.NullReference, logger, platformClient);
                return;
            }

            if (question.Length > 80)
            {
                await Clients.Caller.SendAsync("error", "Teksten kan ikke være lengre enn 80 tegn.");
                return;
            }

            var result = await cache.Upsert(key, session => session.AddQuesiton(question));
            if (result.IsErr())
            {
                logger.LogError("Failed to add question to game: {string}", key);
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var session = result.Unwrap();
            logger.LogInformation("Adding question: {string}, to game: {string}", question, key);
            await Clients.Groups(key).SendAsync("iterations", session.GetIterations());
        }
        catch (Exception error)
        {
            logger.LogError(error, "AddQuestion");
            CoreUtils.LogCriticalError(platformClient, "AddQuestion", "Add Question to QuizSession threw an exception", error);
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

            var result = await cache.Upsert(key, session => session.StartGame());
            if (result.IsErr())
            {
                logger.LogError("Failed to start game with key: {string}", key);
                await CoreUtils.Broadcast(Clients, result.Err(), logger, platformClient);
                return;
            }

            var session = result.Unwrap();
            await Clients.Caller.SendAsync("session", session);
            await Clients.OthersInGroup(key).SendAsync("state", "Game has started");

            var removeResult = await cache.Remove(key);
            if (removeResult.IsErr())
            {
                logger.LogError("Failed to remove starting game from cache: {string}", key);
                await CoreUtils.Broadcast(Clients, removeResult.Err(), logger, platformClient);
            }

            await platformClient.FreeGameKey(key);
        }
        catch (Exception error)
        {
            logger.LogError(error, "StartGame");
            CoreUtils.LogCriticalError(platformClient, "StartGame", "Start QuizSession threw an exception", error);
        }
    }
}