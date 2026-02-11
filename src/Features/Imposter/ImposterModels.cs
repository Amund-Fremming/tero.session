using System.Text.Json.Serialization;

namespace tero.session.src.Features.Imposter;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImposterGameState
{
    [JsonPropertyName("created")]
    Created,

    [JsonPropertyName("initialized")]
    Initialized,

    [JsonPropertyName("started")]
    Started,

    [JsonPropertyName("finished")]
    Finished,
}

