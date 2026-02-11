using tero.session.src.Features.Imposter;
using tero.session.src.Features.Quiz;
using tero.session.src.Features.Spin;

namespace tero.session.src.Core;

public static class CoreServiceExtension
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("CacheTTL").Get<CacheTTLOptions>()!;
        services.AddSingleton(options);

        services.AddSingleton<GameSessionCache<SpinSession>>();
        services.AddSingleton<GameSessionCache<QuizSession>>();
        services.AddSingleton<GameSessionCache<ImposterSession>>();

        services.AddSingleton<HubConnectionManager<SpinSession>>();
        services.AddSingleton<HubConnectionManager<QuizSession>>();
        services.AddSingleton<HubConnectionManager<ImposterSession>>();

        services.AddHostedService<CacheCleanupJob>();

        return services;
    }
}