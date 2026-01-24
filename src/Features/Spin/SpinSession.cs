using System.Text.Json.Serialization;
using tero.session.src.Core;

namespace tero.session.src.Features.Spin;

public class SpinSession : IJoinableSession, ICleanuppable<SpinSession>
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
    public Dictionary<Guid, int> Users { get; init; } = [];

    [JsonConstructor]
    private SpinSession() { }

    public List<Guid> GetUserIds() => Users.Select(u => u.Key).ToList().Shuffle();

    public int GetIterations() => Rounds.Count;

    public int UsersCount() => Users.Count;

    public SpinSession RemoveUser(Guid userId)
    {
        Users.Remove(userId);
        if (userId == HostId)
        {
            HostId = Users.Keys.FirstOrDefault();
        }

        return this;
    }

    public Result<SpinSession, Error> AddUser(Guid userId)
    {
        if (State != SpinGameState.Initialized)
        {
            return Error.GameClosed;
        }

        if (Users.ContainsKey(userId))
        {
            return this;
        }

        if (Users.Count == 0)
        {
            HostId = userId;
        }

        Users.Add(userId, 0);
        return this;
    }

    public HashSet<Guid> GetSpinResult(int numPlayers)
    {
        if (Users.Count == 0)
        {
            return [];
        }

        var toSelect = Math.Min(numPlayers, Users.Count);
        var rnd = new Random();
        var selected = new HashSet<Guid>(toSelect);
        var userList = Users.Keys.ToList();
        var maxAttempts = Users.Count * 10;
        var attempts = 0;

        while (selected.Count < toSelect && attempts < maxAttempts)
        {
            attempts++;

            var availableUsers = userList.Where(u => !selected.Contains(u)).ToList();
            if (availableUsers.Count == 0) break;

            var totalWeight = 0.0;
            var weights = new Dictionary<Guid, double>();

            foreach (var userId in availableUsers)
            {
                var timesChosen = Users[userId];
                var playerWeight = GetIterations() > 0
                    ? 1.0 - (double)timesChosen / GetIterations()
                    : 1.0;
                weights[userId] = Math.Max(0.01, playerWeight);
                totalWeight += weights[userId];
            }

            var r = rnd.NextDouble() * totalWeight;
            var cumulative = 0.0;

            foreach (var userId in availableUsers)
            {
                cumulative += weights[userId];
                if (r <= cumulative)
                {
                    selected.Add(userId);
                    Users[userId] = Users[userId] + 1;
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
                Users[userId]++;
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
        if (State != SpinGameState.Initialized)
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
        Users.Remove(userId);
        return this;
    }
}
