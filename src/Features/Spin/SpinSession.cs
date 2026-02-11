using System.Text.Json.Serialization;
using tero.session.src.Core;

namespace tero.session.src.Features.Spin;

public class SpinSession : IJoinableSession<SpinSession>, ICleanuppable<SpinSession>
{
    [JsonPropertyName("game_id")]
    public Guid GameId { get; init; }

    [JsonPropertyName("host_id")]
    public Guid HostId { get; set; }

    [JsonPropertyName("state")]
    public SpinGameState State { get; set; }

    [JsonPropertyName("current_iteration")]
    public int CurrentIteration { get; set; }

    [JsonPropertyName("selection_size")]
    public int SelectionSize { get; set; }

    [JsonPropertyName("rounds")]
    public List<string> Rounds { get; init; } = [];

    [JsonPropertyName("players")]
    public Dictionary<Guid, int> Players { get; init; } = [];

    [JsonConstructor]
    private SpinSession() { }

    public List<Guid> GetPlayerIds() => Players.Select(u => u.Key).ToList().Shuffle();

    public int GetIterations() => Rounds.Count;

    public int PlayersCount() => Players.Count;

    public SpinSession RemovePlayer(Guid userId)
    {
        Players.Remove(userId);
        if (userId == HostId)
        {
            HostId = Players.Keys.FirstOrDefault();
        }

        return this;
    }

    public Result<SpinSession, Error> ReconnectPlayer(Guid userId)
    {
        if (State == SpinGameState.Finished)
        {
            return Error.GameFinished;
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

    public Result<SpinSession, Error> AddPlayer(Guid userId)
    {
        if (State != SpinGameState.Initialized && State != SpinGameState.Created)
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

    public HashSet<Guid> GetSpinResult(int numPlayers)
    {
        if (Players.Count == 0)
        {
            return [];
        }

        var toSelect = Math.Min(numPlayers, Players.Count);
        var rnd = new Random();
        var selected = new HashSet<Guid>(toSelect);
        var userList = Players.Keys.ToList();
        var maxAttempts = Players.Count * 10;
        var attempts = 0;

        while (selected.Count < toSelect && attempts < maxAttempts)
        {
            attempts++;

            var availablePlayers = userList.Where(u => !selected.Contains(u)).ToList();
            if (availablePlayers.Count == 0) break;

            var totalWeight = 0.0;
            var weights = new Dictionary<Guid, double>();

            foreach (var userId in availablePlayers)
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

            foreach (var userId in availablePlayers)
            {
                cumulative += weights[userId];
                if (r <= cumulative)
                {
                    selected.Add(userId);
                    Players[userId] = Players[userId] + 1;
                    break;
                }
            }
        }

        if (selected.Count < toSelect)
        {
            var remaining = userList.Where(u => !selected.Contains(u)).ToList();
            foreach (var userId in remaining)
            {
                selected.Add(userId);
                Players[userId]++;
                if (selected.Count >= toSelect) break;
            }
        }

        return selected;
    }

    public bool IsHost(Guid userId) => HostId == userId;

    public Result<SpinSession, Error> NextRound()
    {
        if (CurrentIteration >= GetIterations() - 1)
        {
            State = SpinGameState.Finished;
            return Error.GameFinished;
        }

        State = SpinGameState.RoundStarted;
        CurrentIteration++;
        return this;
    }

    public string GetRoundText() => Rounds.ElementAt(CurrentIteration);

    public Result<SpinSession, Error> AddRound(string round)
    {
        if (State != SpinGameState.Created)
        {
            return Error.GameClosed;
        }

        Rounds.Add(round);
        return this;
    }

    public SpinSession StartGame()
    {
        CurrentIteration = 0;
        State = SpinGameState.RoundStarted;
        Rounds.Shuffle();
        return this;
    }

    public SpinSession Cleanup(Guid userId)
    {
        Players.Remove(userId);
        return this;
    }
}
