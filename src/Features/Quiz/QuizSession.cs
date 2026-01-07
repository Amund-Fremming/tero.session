using System.Text.Json.Serialization;
using tero.session.src.Core;

namespace tero.session.src.Features.Quiz;

public class QuizSession
{
    [JsonPropertyName("game_id")]
    public Guid GameId { get; init; }

    [JsonPropertyName("current_iteration")]
    public int CurrentIteration { get; init; }

    [JsonPropertyName("questions")]
    public List<string> Questions { get; init; } = new();


    [JsonConstructor]
    private QuizSession() { }

    public QuizSession AddQuesiton(string question)
    {
        Questions.Add(question);
        return this;
    }

    public int GetIterations() => Questions.Count;

    public QuizSession Start()
    {
        Questions.Shuffle();
        return this;
    }
}