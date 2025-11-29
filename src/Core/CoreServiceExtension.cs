using tero.session.src.Features.Quiz;
using tero.session.src.Features.Spin;

namespace tero.session.src.Core;

public static class CoreServiceExtension
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CacheTTLOptions>(configuration.GetSection("CacheTTL"));
        
        // Also register CacheTTLOptions directly for constructor injection
        var options = configuration.GetSection("CacheTTL").Get<CacheTTLOptions>() ?? new CacheTTLOptions();
        services.AddSingleton(options);

        services.AddSingleton<GameSessionCache<SpinSession>>();
        services.AddSingleton<GameSessionCache<QuizSession>>();

        services.AddSingleton<HubConnectionManager<SpinSession>>();
        services.AddSingleton<HubConnectionManager<QuizSession>>();

        services.AddHostedService<CacheCleanupJob>();

        return services;
    }
}