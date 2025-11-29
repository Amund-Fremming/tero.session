using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using tero.session.src.Core;
using tero.session.src.Features.Quiz;
using tero.session.src.Features.Spin;

namespace tero.session.tests.Core;

public class CacheCleanupJobTests
{
    private readonly Mock<ILogger<CacheCleanupJob>> _loggerMock;
    private readonly Mock<IHubContext<SpinHub>> _spinHubMock;
    private readonly Mock<IHubContext<QuizHub>> _quizHubMock;
    private readonly Mock<IHubClients> _hubClientsMock;
    private readonly Mock<IClientProxy> _clientProxyMock;
    private readonly Mock<IGroupManager> _groupManagerMock;
    private readonly CacheTTLOptions _options;

    public CacheCleanupJobTests()
    {
        _loggerMock = new Mock<ILogger<CacheCleanupJob>>();
        _spinHubMock = new Mock<IHubContext<SpinHub>>();
        _quizHubMock = new Mock<IHubContext<QuizHub>>();
        _hubClientsMock = new Mock<IHubClients>();
        _clientProxyMock = new Mock<IClientProxy>();
        _groupManagerMock = new Mock<IGroupManager>();

        // Setup hub context to return clients and groups
        _spinHubMock.Setup(h => h.Clients).Returns(_hubClientsMock.Object);
        _quizHubMock.Setup(h => h.Clients).Returns(_hubClientsMock.Object);
        _spinHubMock.Setup(h => h.Groups).Returns(_groupManagerMock.Object);
        _quizHubMock.Setup(h => h.Groups).Returns(_groupManagerMock.Object);

        // Setup clients to return client proxy
        _hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxyMock.Object);

        _options = new CacheTTLOptions { SessionMinuttes = 1, ManagerMinuttes = 1 };
    }

    [Fact]
    public void CacheCleanupJob_Constructor_ShouldNotThrow()
    {
        // Arrange & Act
        var spinCache = new GameSessionCache<SpinSession>(
            Mock.Of<ILogger<GameSessionCache<SpinSession>>>(),
            _options
        );
        var quizCache = new GameSessionCache<QuizSession>(
            Mock.Of<ILogger<GameSessionCache<QuizSession>>>(),
            _options
        );
        var spinManager = new HubConnectionManager<SpinSession>(_options);
        var quizManager = new HubConnectionManager<QuizSession>(_options);

        var job = new CacheCleanupJob(
            _loggerMock.Object,
            spinCache,
            quizCache,
            spinManager,
            quizManager,
            _spinHubMock.Object,
            _quizHubMock.Object
        );

        // Assert
        Assert.NotNull(job);
    }

    [Fact]
    public async Task CacheCleanupJob_StartAndStop_ShouldExecuteWithoutErrors()
    {
        // Arrange
        var spinCache = new GameSessionCache<SpinSession>(
            Mock.Of<ILogger<GameSessionCache<SpinSession>>>(),
            _options
        );
        var quizCache = new GameSessionCache<QuizSession>(
            Mock.Of<ILogger<GameSessionCache<QuizSession>>>(),
            _options
        );
        var spinManager = new HubConnectionManager<SpinSession>(_options);
        var quizManager = new HubConnectionManager<QuizSession>(_options);

        var job = new CacheCleanupJob(
            _loggerMock.Object,
            spinCache,
            quizCache,
            spinManager,
            quizManager,
            _spinHubMock.Object,
            _quizHubMock.Object
        );

        // Act - Start and immediately stop
        var cts = new CancellationTokenSource();
        await job.StartAsync(cts.Token);
        cts.Cancel();
        await job.StopAsync(CancellationToken.None);

        // Assert - No exception means success
        Assert.True(true);
    }

    [Fact]
    public async Task CacheCleanupJob_WithMultipleCaches_ShouldInitialize()
    {
        // Arrange
        var spinCache = new GameSessionCache<SpinSession>(
            Mock.Of<ILogger<GameSessionCache<SpinSession>>>(),
            _options
        );
        var quizCache = new GameSessionCache<QuizSession>(
            Mock.Of<ILogger<GameSessionCache<QuizSession>>>(),
            _options
        );
        var spinManager = new HubConnectionManager<SpinSession>(_options);
        var quizManager = new HubConnectionManager<QuizSession>(_options);

        // Act
        var job = new CacheCleanupJob(
            _loggerMock.Object,
            spinCache,
            quizCache,
            spinManager,
            quizManager,
            _spinHubMock.Object,
            _quizHubMock.Object
        );

        // Assert
        Assert.NotNull(job);
    }

    [Fact]
    public void CacheCleanupJob_UsesProvidedDependencies()
    {
        // Arrange
        var spinCache = new GameSessionCache<SpinSession>(
            Mock.Of<ILogger<GameSessionCache<SpinSession>>>(),
            _options
        );
        var quizCache = new GameSessionCache<QuizSession>(
            Mock.Of<ILogger<GameSessionCache<QuizSession>>>(),
            _options
        );
        var spinManager = new HubConnectionManager<SpinSession>(_options);
        var quizManager = new HubConnectionManager<QuizSession>(_options);

        // Act
        var job = new CacheCleanupJob(
            _loggerMock.Object,
            spinCache,
            quizCache,
            spinManager,
            quizManager,
            _spinHubMock.Object,
            _quizHubMock.Object
        );

        // Assert - Verify job was created successfully
        Assert.NotNull(job);
    }
}
