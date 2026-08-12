using System.Text.Json.Serialization;

namespace EsdCleaningSystem.Models;

public class EsdMeasurement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("area_id")]
    public string AreaId { get; set; } = string.Empty;

    [JsonPropertyName("point_id")]
    public string PointId { get; set; } = string.Empty;

    [JsonPropertyName("point_name")]
    public string PointName { get; set; } = string.Empty;

    [JsonPropertyName("coord_x")]
    public double CoordX { get; set; }

    [JsonPropertyName("coord_y")]
    public double CoordY { get; set; }

    [JsonPropertyName("resistance_ohms")]
    public double ResistanceOhms { get; set; } // e.g. 1.2e8 (120 M-Ohms)

    [JsonPropertyName("voltage_volts")]
    public double VoltageVolts { get; set; } // Body Voltage Generation < 100V limit

    [JsonPropertyName("measurement_date")]
    public DateTime MeasurementDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("inspector")]
    public string Inspector { get; set; } = "Inspector ESD";

    [JsonPropertyName("status")]
    public string Status => ResistanceOhms > 1e8 ? "ALTA_RESISTENCIA" : (ResistanceOhms <= 1e9 ? "CONFORME" : "NO_CONFORME");
}
