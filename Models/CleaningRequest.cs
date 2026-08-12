namespace EsdCleaningSystem.Models;

public class CleaningRequest
{
    public string Id { get; set; } = "REQ-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
    public string AreaId { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;
    public double CoordXPercent { get; set; }
    public double CoordYPercent { get; set; }
    public string NearestPointId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string DetailedNotes { get; set; } = string.Empty;
    
    // Evidence data
    public string EvidenceFileName { get; set; } = string.Empty;
    public string EvidenceFileType { get; set; } = string.Empty; // "IMAGE", "PDF", "WEBCAM_PHOTO"
    public string EvidenceUrl { get; set; } = string.Empty; // Relative URL or Data URI
    
    public DateTime RequestDate { get; set; } = DateTime.UtcNow;
    public string RequestedBy { get; set; } = "Operador de Planta";
    
    // Calculated evaluation fields (ANSI/ESD S20.20-2021)
    public DateTime AreaLastCleaningDate { get; set; }
    public int DaysSinceLastCleaning { get; set; }
    public double LastEsdResistanceOhms { get; set; }
    public string AreaCriticality { get; set; } = "MEDIA";
    
    public bool Meets3MonthRule { get; set; }
    public bool HasHighResistanceOverride { get; set; } // > 1e8 ohms
    
    public string Priority { get; set; } = "MEDIA"; // ALTA, MEDIA, BAJA
    public string AuthorizationStatus { get; set; } = "AUTORIZADA"; // AUTORIZADA, DENEGADA_PERIODO_MINIMO, REVISION_REQUERIDA
    public string Status { get; set; } = "AUTORIZADA"; // AUTORIZADA, EN_PROCESO, LIMPIEZA_COMPLETADA, CANCELADA
    public string StatusNotes { get; set; } = string.Empty;
    public DateTime? CompletedDate { get; set; }
    public string CleanedBy { get; set; } = string.Empty;
    public string EvaluationSummary { get; set; } = string.Empty;
}

public class CreateCleaningRequestDto
{
    public string AreaId { get; set; } = string.Empty;
    public double CoordXPercent { get; set; }
    public double CoordYPercent { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string DetailedNotes { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = "Operador ESD";
    public string EvidenceFileName { get; set; } = string.Empty;
    public string EvidenceFileType { get; set; } = string.Empty; // "IMAGE", "PDF", "WEBCAM"
    public string EvidenceBase64 { get; set; } = string.Empty; // Base64 content of image or PDF
}

public class UpdateRequestStatusDto
{
    public string NewStatus { get; set; } = string.Empty; // "LIMPIEZA_COMPLETADA", "EN_PROCESO", "CANCELADA", "AUTORIZADA"
    public string Notes { get; set; } = string.Empty;
    public string PerformedBy { get; set; } = "Personal de Limpieza ESD";
}
