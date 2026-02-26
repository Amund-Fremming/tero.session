using System.Text.Json.Serialization;
using tero.session.src.Core;

namespace tero.session.src.Features.Imposter;

public class ImposterSession
{
    [JsonPropertyName("game_id")]
    public Guid GameId { get; init; }

    [JsonPropertyName("host_id")]
    public Guid HostId { get; set; }

    [JsonPropertyName("current_iteration")]
    public int CurrentIteration { get; set; }

    [JsonPropertyName("rounds")]
    public List<string> Rounds { get; init; } = [];

    [JsonPropertyName("players")]
    public HashSet<string> Players { get; set; } = [];

    [JsonConstructor]
    private ImposterSession() { }

    public int PlayersCount() => Players.Count;

    public int GetIterations() => Rounds.Count;

    public Result<ImposterSession, Error> AddPlayers(HashSet<string> players)
    {
        Players = players;
        return this;
    }

    public Result<ImposterSession, Error> AddRound(string round)
    {
        Rounds.Add(round);
        return this;
    }

    public ImposterSession StartGame()
    {
        CurrentIteration = 0;
        Rounds.Shuffle();
        return this;
    }
}
