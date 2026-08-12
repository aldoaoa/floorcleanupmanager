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

    [JsonPropertyName("x_percent")]
    public double XPercent { get; set; } = 10.0;

    [JsonPropertyName("y_percent")]
    public double YPercent { get; set; } = 10.0;

    [JsonPropertyName("width_percent")]
    public double WidthPercent { get; set; } = 20.0;

    [JsonPropertyName("height_percent")]
    public double HeightPercent { get; set; } = 20.0;
}
