namespace NATS.Client.JetStream.Models;

public record ErrorResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public ApiError Error { get; set; } = new ApiError();
}
