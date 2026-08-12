namespace EsdCleaningSystem.Models;

public class MapPoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Code { get; set; } = "1";
    public string Label { get; set; } = "Punto de Medición ESD";
    public double XPercent { get; set; }
    public double YPercent { get; set; }
    public string ZoneType { get; set; } = "SMT";
    public double LastResistanceOhms { get; set; } = 4.5e7;
    public DateTime? LastMeasurementDate { get; set; }
}

public class FloorMapConfig
{
    public string AreaId { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Criticality { get; set; } = "ALTA";
    public DateTime LastCleaningDate { get; set; } = DateTime.UtcNow.AddDays(-105);
    public DateTime NextCleaningDate => LastCleaningDate.AddDays(90);
    public int DaysSinceLastCleaning => Math.Max(0, (int)(DateTime.UtcNow - LastCleaningDate).TotalDays);
    public int DaysUntilNextCleaning => (int)(NextCleaningDate - DateTime.UtcNow).TotalDays;
    public List<MapPoint> Points { get; set; } = new();
    public List<CleanedZone> CleanedZones { get; set; } = new();
    public string FloorType { get; set; } = "Loseta Conductiva con Cera Antiestática";
    public string StandardCompliance { get; set; } = "ANSI/ESD S20.20-2021";
}
