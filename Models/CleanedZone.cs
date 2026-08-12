namespace EsdCleaningSystem.Models;

public class CleanedZone
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AreaId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public double XPercent { get; set; }
    public double YPercent { get; set; }
    public double WidthPercent { get; set; }
    public double HeightPercent { get; set; }
    public DateTime CleanedDate { get; set; } = DateTime.UtcNow;
    public string CleanedBy { get; set; } = "Personal de Limpieza ESD";
    public string Notes { get; set; } = string.Empty;
}

public class CreateCleanedZoneDto
{
    public string AreaId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public double XPercent { get; set; }
    public double YPercent { get; set; }
    public double WidthPercent { get; set; }
    public double HeightPercent { get; set; }
    public DateTime CleanedDate { get; set; } = DateTime.UtcNow;
    public string CleanedBy { get; set; } = "Personal de Limpieza ESD";
    public string Notes { get; set; } = string.Empty;
}
