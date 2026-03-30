using Microsoft.AspNetCore.SignalR;
using tero.session.src.Features.Platform;
using tero.session.src.Features.Quiz;
using tero.session.src.Features.Spin;

namespace tero.session.src.Core;

public class CacheCleanupJob(
    PlatformClient platformClient,
    ILogger<CacheCleanupJob> logger,
    GameSessionCache<SpinSession> spinCache,
    GameSessionCache<QuizSession> quizCache,
    HubConnectionManager<SpinSession> spinManager,
    HubConnectionManager<QuizSession> quizManager,
    IHubContext<SpinHub> spinHub,
    IHubContext<QuizHub> quizHub
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Cache cleanup service started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                logger.LogDebug("Running cache cleanup");

                var spinSessionCleanup = CleanupCache(spinHub, spinCache);
                var quizSessionCleanup = CleanupCache(quizHub, quizCache);

                var spinManagerCleanup = CleanupManager(spinHub, spinManager, spinCache);
                var quizManagerCleanup = CleanupManager(quizHub, quizManager);

                await Task.WhenAll(
                    spinSessionCleanup,
                    quizSessionCleanup,
                    spinManagerCleanup,
                    quizManagerCleanup
                );
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Cache cleanup service stopping (cancellation requested)");
                break;
            }
            catch (Exception error)
            {
                var log = LogBuilder.New()
                    .WithAction(LogAction.Other)
                    .WithCeverity(LogCeverity.Warning)
                    .WithFunctionName("ExecuteAsync")
                    .WithDescription("Cache cleanup job catched a error")
                    .WithMetadata(error)
                    .Build();

                platformClient.CreateSystemLogAsync(log);
                logger.LogError(error, "CacheCleanupJob");
            }
        }

        logger.LogInformation("Cache cleanup service finished");
    }

    private async Task CleanupCache<TSession, THub>(IHubContext<THub> hub, GameSessionCache<TSession> cache) where THub : Hub
    {
        try
        {
            foreach (var (key, value) in cache.GetCopy())
            {
                if (value.HasExpired())
                {
                    var result = await cache.Remove(key);
                    if (result.IsErr())
                    {
                        var log = LogBuilder.New()
                            .WithAction(LogAction.Delete)
                            .WithCeverity(LogCeverity.Warning)
                            .WithFunctionName("CleanupCache")
                            .WithDescription($"Failed to remove expired cache entry: {key}")
                            .Build();

                        platformClient.CreateSystemLogAsync(log);
                        logger.LogError("Background cleanup failed to remove entry from cache");
                    }

                    await hub.Clients.Groups(key).SendAsync("disconnect", "Spillet har blitt avsluttet");
                    await platformClient.FreeGameKey(key);
                }

            }
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Other)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("CleanupCache")
                .WithDescription("Cache cleanup encountered critical error")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(CleanupCache));
        }
    }

    private async Task CleanupManager<THub, TSession, TCleanup>(IHubContext<THub> hub, HubConnectionManager<TSession> manager, GameSessionCache<TCleanup> cache) where TCleanup : ICleanuppable<TSession> where THub : Hub
    {
        try
        {
            foreach (var (connId, info) in manager.GetCopy())
            {
                if (info.HasExpired())
                {
                    // Fire and forget here?
                    var result = await cache.Upsert(info.GameKey, session => session.Cleanup(info.UserId));
                    if (result.IsErr())
                    {
                        var log = LogBuilder.New()
                            .WithAction(LogAction.Update)
                            .WithCeverity(LogCeverity.Warning)
                            .WithFunctionName("CleanupManager")
                            .WithDescription($"Failed to cleanup session for user")
                            .Build();

                        platformClient.CreateSystemLogAsync(log);
                        logger.LogError("Failed to cleanup session for user id {Guid} - {Error}", info.UserId, result.Err());
                    }

                    await hub.Groups.RemoveFromGroupAsync(connId, info.GameKey);
                    var removeResult = manager.Remove(connId);

                    if (removeResult.IsErr())
                    {
                        var log = LogBuilder.New()
                            .WithAction(LogAction.Delete)
                            .WithCeverity(LogCeverity.Warning)
                            .WithFunctionName(nameof(CleanupManager))
                            .WithDescription("Failed to remove entry in connection manager")
                            .Build();

                        platformClient.CreateSystemLogAsync(log);
                        logger.LogError("Failed to remove entry in conneciton manager: {Error}", result.Err());
                    }
                }
            }
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Other)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("CleanupManager")
                .WithDescription("Manager cleanup with ICleanuppable encountered critical error")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, "CleanupManager: ICleanuppable");
        }
    }

    private async Task CleanupManager<THub, TSession>(IHubContext<THub> hub, HubConnectionManager<TSession> manager) where THub : Hub
    {
        try
        {
            foreach (var (connId, info) in manager.GetCopy())
            {
                if (info.HasExpired())
                {
                    await hub.Groups.RemoveFromGroupAsync(connId, info.GameKey);
                }
            }
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Other)
                .WithCeverity(LogCeverity.Critical)
                .WithFunctionName("CleanupManager")
                .WithDescription("Manager cleanup encountered critical error")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(CleanupManager));
        }
    }
}