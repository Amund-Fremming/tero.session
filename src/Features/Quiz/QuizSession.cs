using System.Text.Json.Serialization;
using tero.session.src.Core;

namespace tero.session.src.Features.Quiz;

public class QuizSession
{
    [JsonPropertyName("game_id")]
    public Guid GameId { get; init; }

    [JsonPropertyName("current_iteration")]
    public int CurrentIteration { get; init; }

    [JsonPropertyName("rounds")]
    public List<string> Rounds { get; init; } = [];

    [JsonConstructor]
    private QuizSession() { }

    public QuizSession AddQuesiton(string question)
    {
        Rounds.Add(question);
        return this;
    }

    public int GetIterations() => Rounds.Count;

    public QuizSession Start()
    {
        Rounds.Shuffle();
        return this;
    }
}