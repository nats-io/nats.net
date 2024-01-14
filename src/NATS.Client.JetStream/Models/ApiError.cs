namespace NATS.Client.JetStream.Models;

public record ApiError
{
    /// <summary>
    /// HTTP like error code in the 300 to 500 range
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("code")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(300, 699)]
    public int Code { get; set; }

    /// <summary>
    /// A human friendly description of the error
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("description")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Description { get; set; }

    /// <summary>
    /// The NATS error code unique to each kind of error
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("err_code")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0, 65535)]
    public int ErrCode { get; set; }
}
