using tero.session.src.Core;
using tero.session.src.Features.Auth;
using tero.session.src.Features.Imposter;
using tero.session.src.Features.Platform;
using tero.session.src.Features.Quiz;
using tero.session.src.Features.Spin;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var config = builder.Configuration;

// Configure logging format
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.FormatterName = "simple";
});
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.IncludeScopes = false;
});

services.AddControllers();
services.AddSignalR();

// Add custom services
services.AddAuthServices(config);
services.AddPlatformServices(config);
services.AddCoreServices(config);

var app = builder.Build();
app.MapControllers();

// Add custom hubs
app.AddQuizHub();
app.AddSpinHub();
app.AddImposterHub();

// Health check for tero-platform
app.MapGet("/health", () => "OK");

app.Run();