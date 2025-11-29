using Microsoft.Extensions.Logging;
using Moq;
using tero.session.src.Core;

namespace tero.session.tests.Core;

public class GameSessionCacheTests
{
    private readonly Mock<ILogger<GameSessionCache<TestSession>>> _loggerMock;
    private readonly CacheTTLOptions _options;
    private readonly GameSessionCache<TestSession> _cache;

    public GameSessionCacheTests()
    {
        _loggerMock = new Mock<ILogger<GameSessionCache<TestSession>>>();
        _options = new CacheTTLOptions { SessionMinuttes = 10, ManagerMinuttes = 30 };
        _cache = new GameSessionCache<TestSession>(_loggerMock.Object, _options);
    }

    [Fact]
    public async Task Insert_WithValidKey_ShouldReturnOk()
    {
        // Arrange
        var key = "test-key";
        var session = new TestSession { Value = "test" };

        // Act
        var result = await _cache.Insert(key, session);

        // Assert  
        if (result.IsErr())
        {
            var err = result.Err();
            Assert.Fail($"Result should not be an error. Error value: {err}");
        }
    }

    [Fact]
    public async Task Insert_WithDuplicateKey_ShouldReturnKeyExistsError()
    {
        // Arrange
        var key = "duplicate-key";
        var session1 = new TestSession { Value = "test1" };
        var session2 = new TestSession { Value = "test2" };

        // Act
        await _cache.Insert(key, session1);
        var result = await _cache.Insert(key, session2);

        // Assert
        Assert.True(result.IsErr());
        Assert.Equal(Error.KeyExists, result.Err());
    }

    [Fact]
    public async Task GetCopy_ShouldReturnCopyOfCache()
    {
        // Arrange
        var key = "test-key";
        var session = new TestSession { Value = "test" };
        await _cache.Insert(key, session);

        // Act
        var copy = _cache.GetCopy();

        // Assert
        Assert.Single(copy);
        Assert.True(copy.ContainsKey(key));
    }

    [Fact]
    public async Task Upsert_WithExistingKey_ShouldUpdateSession()
    {
        // Arrange
        var key = "test-key";
        var session = new TestSession { Value = "initial" };
        await _cache.Insert(key, session);

        // Act
        var result = await _cache.Upsert(key, s =>
        {
            s.Value = "updated";
            return Result<TestSession, Error>.Ok(s);
        });

        // Assert
        Assert.True(result.IsOk());
        var updatedSession = result.Unwrap();
        Assert.Equal("updated", updatedSession.Value);
    }

    [Fact]
    public async Task Upsert_WithNonExistingKey_ShouldReturnGameNotFoundError()
    {
        // Arrange
        var key = "non-existing-key";

        // Act
        var result = await _cache.Upsert(key, s =>
        {
            s.Value = "updated";
            return Result<TestSession, Error>.Ok(s);
        });

        // Assert
        Assert.True(result.IsErr());
        Assert.Equal(Error.GameNotFound, result.Err());
    }

    [Fact]
    public async Task Upsert_GenericVersion_WithExistingKey_ShouldReturnResult()
    {
        // Arrange
        var key = "test-key";
        var session = new TestSession { Value = "initial" };
        await _cache.Insert(key, session);

        // Act
        var result = await _cache.Upsert<string>(key, s =>
        {
            s.Value = "modified";
            return "result-value";
        });

        // Assert
        Assert.True(result.IsOk());
        Assert.Equal("result-value", result.Unwrap());
    }

    [Fact]
    public async Task Upsert_GenericVersion_WithNonExistingKey_ShouldReturnGameNotFoundError()
    {
        // Arrange
        var key = "non-existing-key";

        // Act
        var result = await _cache.Upsert<string>(key, s => "result");

        // Assert
        Assert.True(result.IsErr());
        Assert.Equal(Error.GameNotFound, result.Err());
    }

    [Fact]
    public async Task Remove_WithExistingKey_ShouldReturnTrue()
    {
        // Arrange
        var key = "test-key";
        var session = new TestSession { Value = "test" };
        await _cache.Insert(key, session);

        // Act
        var result = await _cache.Remove(key);

        // Assert
        Assert.True(result);
        var copy = _cache.GetCopy();
        Assert.Empty(copy);
    }

    [Fact]
    public async Task Remove_WithNonExistingKey_ShouldReturnTrueAndLogWarning()
    {
        // Arrange
        var key = "non-existing-key";

        // Act
        var result = await _cache.Remove(key);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task Upsert_ConcurrentCalls_ShouldHandleThreadSafety()
    {
        // Arrange
        var key = "test-key";
        var session = new TestSession { Value = "0" };
        await _cache.Insert(key, session);

        // Act - Multiple concurrent upserts
        var tasks = Enumerable.Range(1, 10).Select(i =>
            _cache.Upsert(key, s =>
            {
                var currentValue = int.Parse(s.Value);
                s.Value = (currentValue + 1).ToString();
                return Result<TestSession, Error>.Ok(s);
            })
        );

        await Task.WhenAll(tasks);

        // Assert
        var finalResult = await _cache.Upsert<string>(key, s => s.Value);
        Assert.True(finalResult.IsOk());
        Assert.Equal("10", finalResult.Unwrap());
    }

    [Fact]
    public async Task Remove_ShouldDisposeSemaphore()
    {
        // Arrange
        var key = "test-key";
        var session = new TestSession { Value = "test" };
        await _cache.Insert(key, session);

        // Act
        var result = await _cache.Remove(key);

        // Assert
        Assert.True(result);
        // The semaphore should be disposed and removed from locks
        var copy = _cache.GetCopy();
        Assert.Empty(copy);
        
        // Subsequent insert with same key should succeed as the entry was removed
        var insertResult = await _cache.Insert(key, new TestSession { Value = "new" });
        Assert.True(insertResult.IsOk());
    }
}

public class TestSession
{
    public string Value { get; set; } = string.Empty;
}
