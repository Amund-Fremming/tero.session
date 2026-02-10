using System.Text.Json.Serialization;

namespace tero.session.src.Features.Spin;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpinGameState
{
    [JsonPropertyName("created")]
    Created,

    [JsonPropertyName("initialized")]
    Initialized,

    [JsonPropertyName("round_started")]
    RoundStarted,

    [JsonPropertyName("rount_in_progress")]
    RoundInProgress,

    [JsonPropertyName("round_finishe")]
    RoundFinished,

    [JsonPropertyName("finished")]
    Finished,
}