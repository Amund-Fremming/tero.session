using System.Text.Json.Serialization;
using tero.session.src.Core;

namespace tero.session.src.Features.Imposter;

public class ImposterSession : IJoinableSession<ImposterSession>, ICleanuppable<ImposterSession>
{
    [JsonPropertyName("game_id")]
    public Guid GameId { get; init; }

    [JsonPropertyName("host_id")]
    public Guid HostId { get; set; }

    [JsonPropertyName("state")]
    public ImposterGameState State { get; set; }

    [JsonPropertyName("current_iteration")]
    public int CurrentIteration { get; set; }

    [JsonPropertyName("rounds")]
    public List<string> Rounds { get; init; } = [];

    [JsonPropertyName("players")]
    public Dictionary<Guid, int> Players { get; init; } = [];

    [JsonConstructor]
    private ImposterSession() { }

    public int PlayersCount() => Players.Count;

    public int UsersCount() => Players.Count;

    public int GetIterations() => Rounds.Count;

    public List<Guid> GetUserIds() => Players.Select(p => p.Key).ToList();

    public bool IsHost(Guid userId) => HostId == userId;

    public Result<ImposterSession, Error> AddPlayer(Guid userId)
    {
        if (State != ImposterGameState.Initialized && State != ImposterGameState.Created)
        {
            return Error.GameClosed;
        }

        if (Players.ContainsKey(userId))
        {
            return this;
        }

        if (Players.Count == 0)
        {
            HostId = userId;
        }

        Players.Add(userId, 0);
        return this;
    }

    public ImposterSession RemovePlayer(Guid userId)
    {
        Players.Remove(userId);
        if (userId == HostId)
        {
            HostId = Players.Keys.FirstOrDefault();
        }

        return this;
    }

    public Result<ImposterSession, Error> AddRound(string round)
    {
        if (State != ImposterGameState.Created)
        {
            return Error.GameClosed;
        }

        Rounds.Add(round);
        return this;
    }

    public string GetRoundWord() => Rounds.ElementAt(CurrentIteration);

    public ImposterSession StartGame()
    {
        CurrentIteration = 0;
        State = ImposterGameState.Started;
        Rounds.Shuffle();
        return this;
    }

    public Result<ImposterSession, Error> NextRound()
    {
        if (CurrentIteration >= GetIterations() - 1)
        {
            State = ImposterGameState.Finished;
            return Error.GameFinished;
        }

        State = ImposterGameState.Started;
        CurrentIteration++;
        return this;
    }

    public Guid GetImposter()
    {
        if (Players.Count == 0)
        {
            return Guid.Empty;
        }

        if (Players.Count == 1)
        {
            var singlePlayer = Players.Keys.First();
            Players[singlePlayer] = Players[singlePlayer] + 1;
            return singlePlayer;
        }

        var rnd = new Random();
        var userList = Players.Keys.ToList();
        var totalWeight = 0.0;
        var weights = new Dictionary<Guid, double>();

        foreach (var userId in userList)
        {
            var timesChosen = Players[userId];
            var playerWeight = GetIterations() > 0
                ? 1.0 - (double)timesChosen / GetIterations()
                : 1.0;
            weights[userId] = Math.Max(0.01, playerWeight);
            totalWeight += weights[userId];
        }

        var r = rnd.NextDouble() * totalWeight;
        var cumulative = 0.0;

        foreach (var userId in userList)
        {
            cumulative += weights[userId];
            if (r <= cumulative)
            {
                Players[userId] = Players[userId] + 1;
                return userId;
            }
        }

        var fallback = userList.Last();
        Players[fallback] = Players[fallback] + 1;
        return fallback;
    }

    public ImposterSession Cleanup(Guid userId)
    {
        Players.Remove(userId);
        return this;
    }
}
