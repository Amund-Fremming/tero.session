namespace tero.session.src.Features.Imposter;

public static class ImposterServiceExtension
{
    public static WebApplication AddImposterHub(this WebApplication app)
    {
        app.MapHub<ImposterHub>("hubs/imposter");
        return app;
    }
}
