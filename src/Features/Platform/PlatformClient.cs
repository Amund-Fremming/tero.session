using Microsoft.Extensions.Options;
using tero.session.src.Core;
using tero.session.src.Features.Auth;

namespace tero.session.src.Features.Platform;

public class PlatformClient(IHttpClientFactory httpClientFactory, ILogger<PlatformClient> logger, Auth0Client auth0Client, IOptions<PlatformOptions> options)
{
    private readonly HttpClient _client = httpClientFactory.CreateClient(nameof(PlatformClient));
    private readonly PlatformOptions _options = options.Value;

    public async Task<Result<Exception>> PersistGame()
    {
        try
        {
            var result = await auth0Client.GetToken();
            if (result.IsErr())
            {
                return result.Err();
            }

            var token = result.Unwrap();
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PostAsync("/api/games/persist", null);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Failed to persist game, status code: {StatusCode}", response.StatusCode);
                return new HttpRequestException($"Status code was {response.StatusCode}");
            }

            return Result<Exception>.Ok;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Error persisting game");
            return error;
        }
    }

    public async Task<Result<Exception>> FreeGameKey(string key)
    {
        try
        {
            var result = await auth0Client.GetToken();
            if (result.IsErr())
            {
                return result.Err();
            }

            var token = result.Unwrap();
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _client.DeleteAsync($"/api/games/keys/{key}");
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Failed to free game key {Key}, status code: {StatusCode}", key, response.StatusCode);
                return new HttpRequestException($"Status code was {response.StatusCode}");
            }

            return Result<Exception>.Ok;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Error freeing game key {Key}", key);
            return error;
        }
    }

    public async Task<Result<Exception>> CreateSystemLog(SystemLogRequest request)
    {
        try
        {
            var result = await auth0Client.GetToken();
            if (result.IsErr())
            {
                return result.Err();
            }

            var token = result.Unwrap();
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(request),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var response = await _client.PostAsync("/api/logs/system", content);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Failed to create system log, status code: {StatusCode}", response.StatusCode);
                return new HttpRequestException($"Status code was {response.StatusCode}");
            }

            return Result<Exception>.Ok;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Error creating system log");
            return error;
        }
    } 
}
