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
        request.AreaLastCleaningDate = mapConfig.LastCleaningDate;
        
        var daysSinceLastCleaning = (int)(now - mapConfig.LastCleaningDate).TotalDays;
        if (daysSinceLastCleaning < 0) daysSinceLastCleaning = 0;
        request.DaysSinceLastCleaning = daysSinceLastCleaning;

        double resistanceOhms = nearestMeasurement?.ResistanceOhms ?? 4.5e7; // Default 45 M-Ohms if missing
        request.LastEsdResistanceOhms = resistanceOhms;

        // ANSI/ESD S20.20-2021 Rules
        bool meets3MonthRule = daysSinceLastCleaning >= _settings.MinimumCleaningIntervalDays; // >= 90 days
        bool hasHighResistance = resistanceOhms > _settings.HighResistanceThresholdOhms; // > 1e8 ohms

        request.Meets3MonthRule = meets3MonthRule;
        request.HasHighResistanceOverride = hasHighResistance;

        string summary;
        string priority;
        string status;

        if (hasHighResistance)
        {
            // If resistance > 1e8 ohms, wax layer has degraded or accumulated insulator dust.
            // Under ANSI/ESD S20.20-2021, cleaning & ESD re-waxing is immediately authorized and given HIGH priority.
            priority = "ALTA";
            status = "AUTORIZADA";
            summary = $"AUTORIZADA (PRIORIDAD ALTA): La resistencia medida en la loseta ({resistanceOhms:1.0E+00} Ω) excede el umbral de 1.0E+08 Ω según ANSI/ESD S20.20-2021. Se autoriza la aplicación de cera antiestática sin esperar el ciclo regular de 3 meses.";
        }
        else if (!meets3MonthRule)
        {
            // Less than 3 months and normal resistance -> DENIED by period rule
            priority = "BAJA";
            status = "DENEGADA_PERIODO_MINIMO";
            int remainingDays = _settings.MinimumCleaningIntervalDays - daysSinceLastCleaning;
            summary = $"DENEGADA POR REGLA DE PERIODICIDAD: Han transcurrido {daysSinceLastCleaning} días desde la última limpieza (mínimo 90 días / 3 meses). La resistencia actual ({resistanceOhms:1.0E+00} Ω) está dentro del rango seguro (< 1.0E+08 Ω). Falta {remainingDays} día(s) para habilitar el mantenimiento programado.";
        }
        else
        {
            // >= 3 months elapsed and safe resistance -> Authorized according to criticality
            status = "AUTORIZADA";
            switch (mapConfig.Criticality.ToUpper())
            {
                case "ALTA":
                    priority = "ALTA";
                    summary = $"AUTORIZADA (PRIORIDAD ALTA): Ha cumplido el ciclo de 3 meses ({daysSinceLastCleaning} días transcurridos) en una zona de ALTA criticidad ESD (Línea SMT/Cuarto Limpio).";
                    break;
                case "MEDIA":
                    priority = "MEDIA";
                    summary = $"AUTORIZADA (PRIORIDAD MEDIA): Ha cumplido el ciclo de 3 meses ({daysSinceLastCleaning} días transcurridos) en área de criticidad media.";
                    break;
                default:
                    priority = "BAJA";
                    summary = $"AUTORIZADA (PRIORIDAD BAJA): Ha cumplido el ciclo de 3 meses ({daysSinceLastCleaning} días transcurridos) en área de baja criticidad.";
                    break;
            }
        }

        request.Priority = priority;
        request.AuthorizationStatus = status;
        request.EvaluationSummary = summary;

        return request;
    }
}
