using System.Text.Json.Serialization;

namespace EsdCleaningSystem.Models;

public class FloorCleaningRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("cuarto")]
    public string AreaId { get; set; } = string.Empty;

    [JsonPropertyName("area_name")]
    public string AreaName { get; set; } = string.Empty;

    [JsonPropertyName("punto")]
    public string PointId { get; set; } = string.Empty;

    [JsonPropertyName("fecha_limpieza")]
    public DateTime FechaLimpieza { get; set; } = DateTime.UtcNow.AddDays(-100);

    [JsonPropertyName("fecha_proxima_limpieza")]
    public DateTime FechaProximaLimpieza { get; set; } = DateTime.UtcNow.AddDays(-10);

    [JsonPropertyName("limpiado_por")]
    public string LimpiadoPor { get; set; } = "Personal ESD";

    [JsonPropertyName("observaciones")]
    public string Observaciones { get; set; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;
}
