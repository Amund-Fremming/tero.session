using Microsoft.AspNetCore.Mvc;
using tero.session.src.Core;
using tero.session.src.Features.Imposter;
using tero.session.src.Features.Quiz;
using tero.session.src.Features.Spin;

namespace tero.session.src.Features.Platform;

[ApiController]
[Route("session")]
public class PlatformController(
    ILogger<PlatformController> logger,
    PlatformClient platformClient,
    GameSessionCache<SpinSession> spinCache,
    HubConnectionManager<SpinSession> spinManager,
    GameSessionCache<QuizSession> quizCache,
    HubConnectionManager<QuizSession> quizManager,
    GameSessionCache<ImposterSession> imposterCache,
    HubConnectionManager<ImposterSession> imposterManager
) : ControllerBase
{
    [HttpPost("initiate/{gameType}")]
    public IActionResult InitiateGameSession(GameType gameType, [FromBody] InitiateGameRequest request)
    {
        try
        {
            var key = request.Key;
            logger.LogInformation("Recieved request for {GameType} with key: {string}", gameType, key);
            var (statusCode, response) = gameType switch
            {
                GameType.Roulette or GameType.Duel => CoreUtils.InsertPayload(platformClient, spinCache, key, request.Value),
                GameType.Quiz => CoreUtils.InsertPayload(platformClient, quizCache, key, request.Value),
                GameType.Imposter => CoreUtils.InsertPayload(platformClient, imposterCache, key, request.Value),
                _ => (400, "Not supported game type")
            };

            return StatusCode(statusCode, response);
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Other)
                .WithCeverity(LogCeverity.Warning)
                .WithFunctionName("InitiateGameSession")
                .WithDescription("PlatformController catched a error")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(InitiateGameSession));
            return StatusCode(500, "Internal server error");
        }
    }

    // TODO - add this endpoint to admin dashboard
    [HttpGet]
    public IActionResult CacheInfo()
    {
        try
        {
            var payload = new CacheInfo
            {
                SpinSessionSize = spinCache.Size(),
                SpinManagerSize = spinManager.Size(),
                QuizSessionSize = quizCache.Size(),
                QuizManagerSize = quizManager.Size(),
                ImposterSessionSize = imposterCache.Size(),
                ImposterManagerSize = imposterManager.Size(),
            };

            return Ok(payload);
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                .WithAction(LogAction.Other)
                .WithCeverity(LogCeverity.Warning)
                .WithFunctionName("CacheInfo")
                .WithDescription("CacheInfo catched a error")
                .WithMetadata(error)
                .Build();

            platformClient.CreateSystemLogAsync(log);
            logger.LogError(error, nameof(CacheInfo));
            return StatusCode(500, "Internal server error");
        }
    }

}