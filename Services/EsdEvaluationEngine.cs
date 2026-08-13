using EsdCleaningSystem.Models;

namespace EsdCleaningSystem.Services;

public class EsdEvaluationEngine
{
    private readonly SupabaseSettings _settings;

    public EsdEvaluationEngine(SupabaseSettings settings)
    {
        _settings = settings;
    }

    public CleaningRequest EvaluateRequest(CleaningRequest request, FloorMapConfig mapConfig, EsdMeasurement? nearestMeasurement)
    {
        var now = DateTime.UtcNow;
        request.AreaName = mapConfig.AreaName;
        request.AreaCriticality = mapConfig.Criticality;
        
        double resistanceOhms = nearestMeasurement?.ResistanceOhms ?? request.LastEsdResistanceOhms;
        if (resistanceOhms <= 0) resistanceOhms = 4.5e7;
        request.LastEsdResistanceOhms = resistanceOhms;

        // Search for matching recent cleaning zone (including 2% margin)
        var recentZone = FindMatchingCleanedZone(request.CoordXPercent, request.CoordYPercent, mapConfig.CleanedZones, marginPercent: 2.0);
        
        DateTime lastCleanDate = recentZone?.CleanedDate ?? mapConfig.LastCleaningDate;
        request.AreaLastCleaningDate = lastCleanDate;

        int daysSinceLastCleaning = (int)(now - lastCleanDate).TotalDays;
        if (daysSinceLastCleaning < 0) daysSinceLastCleaning = 0;
        request.DaysSinceLastCleaning = daysSinceLastCleaning;

        bool hasHighResistanceOverride = resistanceOhms > _settings.HighResistanceThresholdOhms; // > 1e8 ohms
        bool isHighCriticalityArea = string.Equals(mapConfig.Criticality, "ALTA", StringComparison.OrdinalIgnoreCase);

        request.HasHighResistanceOverride = hasHighResistanceOverride;
        request.Meets3MonthRule = daysSinceLastCleaning >= 90;

        string summary;
        string priority;
        string status;

        // REGLA 1: Override por Resistencia Crítica (> 1e8 ohms) -> SIEMPRE AUTORIZADA
        if (hasHighResistanceOverride)
        {
            priority = "ALTA";
            status = "AUTORIZADA";
            summary = $"AUTORIZADA (REGLA 1 - RESISTENCIA CRÍTICA): Resistencia medida ({resistanceOhms:1.0E+00} Ω) excede el máximo permitido (1.0E+08 Ω) según ANSI/ESD S20.20-2021. Se autoriza mantenimiento inmediato.";
        }
        // REGLA 3: Excepción de Criticidad ALTA (> 2 meses / 60 días y resistencia > 1e6 ohms)
        else if (isHighCriticalityArea && daysSinceLastCleaning > 60 && resistanceOhms > 1e6)
        {
            priority = "ALTA";
            status = "AUTORIZADA";
            summary = $"AUTORIZADA (REGLA 3 - CRITICIDAD ALTA): Zona de criticidad ALTA. Han transcurrido {daysSinceLastCleaning} días (> 2 meses) y la resistencia es {resistanceOhms:1.0E+00} Ω (> 1.0E+06 Ω). Se pre-aprueba mantenimiento por protección de componentes sensibles.";
        }
        // REGLA 2: Proteccion por Limpieza Reciente en Recuadro o Proximidad <= 2% (< 3 meses / 90 días)
        else if (recentZone != null && daysSinceLastCleaning < 90)
        {
            priority = "BAJA";
            status = "DENEGADA_PERIODO_MINIMO";
            int remainingDays = 90 - daysSinceLastCleaning;
            summary = $"DENEGADA (REGLA 2 - RECUADRO LIMPIADO RECIENTEMENTE): La ubicación seleccionada está dentro o a un margen de 2% de un área limpiada hace {daysSinceLastCleaning} días (< 3 meses). Resistencia en rango conforme ({resistanceOhms:1.0E+00} Ω). Faltan {remainingDays} días para autorizar.";
        }
        // REGLA 4: Ciclo Regular por Periodicidad (>= 90 días)
        else if (daysSinceLastCleaning >= 90)
        {
            status = "AUTORIZADA";
            priority = isHighCriticalityArea ? "ALTA" : (string.Equals(mapConfig.Criticality, "MEDIA", StringComparison.OrdinalIgnoreCase) ? "MEDIA" : "BAJA");
            summary = $"AUTORIZADA (REGLA 4 - CICLO REGULAR): Cumplido ciclo regular de 3 meses ({daysSinceLastCleaning} días transcurridos). Aprobada según criticidad {mapConfig.Criticality}.";
        }
        else
        {
            priority = "BAJA";
            status = "DENEGADA_PERIODO_MINIMO";
            int remainingDays = 90 - daysSinceLastCleaning;
            summary = $"DENEGADA POR PERIODICIDAD: Han transcurrido {daysSinceLastCleaning} días en el área (< 3 meses). Resistencia en rango conforme ({resistanceOhms:1.0E+00} Ω). Faltan {remainingDays} días.";
        }

        request.Priority = priority;
        request.AuthorizationStatus = status;
        request.Status = status;
        request.EvaluationSummary = summary;

        return request;
    }

    private CleanedZone? FindMatchingCleanedZone(double requestX, double requestY, List<CleanedZone> zones, double marginPercent = 2.0)
    {
        if (zones == null || !zones.Any()) return null;

        foreach (var z in zones.OrderByDescending(z => z.CleanedDate))
        {
            double minX = z.XPercent - marginPercent;
            double maxX = z.XPercent + z.WidthPercent + marginPercent;
            double minY = z.YPercent - marginPercent;
            double maxY = z.YPercent + z.HeightPercent + marginPercent;

            if (requestX >= minX && requestX <= maxX && requestY >= minY && requestY <= maxY)
            {
                return z;
            }
        }

        return null;
    }
}
