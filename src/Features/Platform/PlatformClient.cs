using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using tero.session.src.Core;
using tero.session.src.Features.Auth;

namespace tero.session.src.Features.Platform;

public class PlatformClient(IHttpClientFactory httpClientFactory, ILogger<PlatformClient> logger, Auth0Client auth0Client)
{
    private readonly HttpClient _client = httpClientFactory.CreateClient(nameof(PlatformClient));
    private readonly JsonSerializerOptions _jsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public async Task<Result<Error>> PersistGame<T>(string name, GameCategory category, GameType gameType, T session)
    {
        try
        {
            var result = await auth0Client.GetToken();
            if (result.IsErr())
            {
                var log = LogBuilder.New()
                    .WithAction(LogAction.Other)
                    .WithCeverity(LogCeverity.Critical)
                    .WithFunctionName("PersistGame: Comming from Auth0Client - GetToken")
                    .WithDescription("Failed to get Auth0 token")
                    .Build();

                CreateSystemLogAsync(log);
                return result.Err();
            }

            var token = result.Unwrap();
            var envelope = new InteractiveEnvelope<T>
            {
                Name = name,
                Category = category,
                Payload = session
            };
            var json = JsonSerializer.Serialize(envelope);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var uri = $"/games/session/persist/{gameType}";
            var response = await _client.PostAsync(uri, content);

            if (!response.IsSuccessStatusCode)
            {
                var log = LogBuilder.New()
                    .WithAction(LogAction.Other)
                    .WithCeverity(LogCeverity.Critical)
                    .WithFunctionName("PersistGame")
                    .WithDescription("Failed to use http to persist game")
                    .Build();

                CreateSystemLogAsync(log);
                logger.LogError("Failed to persist game, status code: {StatusCode}", response.StatusCode);
                return new Error(Error.ErrorType.Http, $"PersistGame failed: upstream returned status {(int)response.StatusCode}");
            }

            return Result<Error>.Ok;
        }
        catch (HttpRequestException error)
        {
            var log = LogBuilder.New()
                   .WithAction(LogAction.Other)
                   .WithCeverity(LogCeverity.Critical)
                   .WithFunctionName("PersistGame")
                   .WithDescription("Persist game threw an exception while persisting game")
                   .WithMetadata(error)
                   .Build();

            CreateSystemLogAsync(log);
            logger.LogError(error, nameof(PersistGame));
            return new Error(Error.ErrorType.Http, "PersistGame failed: HTTP request exception while persisting game");
        }
        catch (Exception error)
        {
            var log = LogBuilder.New()
                   .WithAction(LogAction.Other)
                   .WithCeverity(LogCeverity.Critical)
                   .WithFunctionName("PersistGame")
                   .WithDescription("Persist game threw an exception")
                   .Build();

            CreateSystemLogAsync(log);
            logger.LogError(error, nameof(PersistGame));
            return new Error(Error.ErrorType.System, "PersistGame failed: unexpected exception while persisting game");
        }
    }

    public void CreateSystemLogAsync(CreateSyslogRequest request)
    {
        _ = Task.Run(async () =>
            {
                try
                {
                    var result = await CreateSystemLog(request);
                    if (result.IsErr())
                    {
                        logger.LogWarning("Fire-and-forget log failed: {Error}", result.Err());
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Fire-and-forget log threw");
                }
            });
    }

    public async Task<Result<Error>> CreateSystemLog(CreateSyslogRequest request)
    {
        try
        {
            var result = await auth0Client.GetToken();
            if (result.IsErr())
            {
                var log = LogBuilder.New()
                    .WithAction(LogAction.Other)
                    .WithCeverity(LogCeverity.Critical)
                    .WithFunctionName("PersistGame: Comming from Auth0Client - GetToken")
                    .WithDescription("Failed to get Auth0 token")
                    .Build();

                CreateSystemLogAsync(log);
                return result.Err();
            }

            var token = result.Unwrap();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            logger.LogDebug("Sending system log request: {Json}", json);

            var content = new StringContent(
               json,
                Encoding.UTF8,
                "application/json"
            );

            var response = await _client.PostAsync("/logs", content);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Failed to create system log, status code: {StatusCode}", response.StatusCode);
                return new Error(Error.ErrorType.Http, $"CreateSystemLog failed: upstream returned status {(int)response.StatusCode}");
            }

            return Result<Error>.Ok;
        }
        catch (HttpRequestException error)
        {
            logger.LogError(error, nameof(CreateSystemLog));
            return new Error(Error.ErrorType.Http, "CreateSystemLog failed: HTTP request exception while sending system log");
        }
        catch (Exception error)
        {
            logger.LogError(error, "Error creating system log");
            return new Error(Error.ErrorType.System, "CreateSystemLog failed: unexpected exception while sending system log");
        }
    }

    /// <summary>
    /// Only to be used for cleanups
    /// </summary>
    public async Task<Result<Error>> FreeGameKey(string key)
    {
        try
        {
            var result = await auth0Client.GetToken();
            if (result.IsErr())
            {
                var log = LogBuilder.New()
                    .WithAction(LogAction.Update)
                    .WithCeverity(LogCeverity.Warning)
                    .WithDescription("Failed to free game key (done on cache cleanup)")
                    .Build();

                CreateSystemLogAsync(log);
                return result.Err();
            }

            var token = result.Unwrap();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var content = new StringContent(
                "application/json"
            );


            var encodedKey = Uri.EscapeDataString(key);
            var response = await _client.PatchAsync($"/games/general/free-key/{encodedKey}", content);
            if (!response.IsSuccessStatusCode)
            {
                var log = LogBuilder.New()
                    .WithAction(LogAction.Other)
                    .WithCeverity(LogCeverity.Critical)
                    .WithFunctionName("FreeGameKey")
                    .WithDescription("Failed to use http to free game key")
                    .Build();

                CreateSystemLogAsync(log);
                logger.LogError("Failed to free game key: {Key}, status code: {StatusCode}", key, response.StatusCode);
                return new Error(Error.ErrorType.Http, $"FreeGameKey failed: upstream returned status {(int)response.StatusCode} for key '{key}'");
            }

            logger.LogInformation("Game key released: {string}", key);
            return Result<Error>.Ok;

        }
        catch (Exception error)
        {
            logger.LogError(error, nameof(FreeGameKey));
            return new Error(Error.ErrorType.System, $"FreeGameKey failed: unexpected exception while freeing key '{key}'");
        }
    }
}
