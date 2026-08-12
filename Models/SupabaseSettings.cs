namespace EsdCleaningSystem.Models;

public class SupabaseSettings
{
    public string Url { get; set; } = "https://fxzqrentvhjrmuxeufns.supabase.co";
    public string AnonKey { get; set; } = "sb_publishable_A7D6S1ojPGaRz2FhRryDhQ_CnZF0X75";
    public string TableName { get; set; } = "validacion_piso";
    public bool UseMockFallback { get; set; } = false;
    public int MinimumCleaningIntervalDays { get; set; } = 90; // 3 months
    public double HighResistanceThresholdOhms { get; set; } = 1e8; // 1.0 x 10^8 ohms (ANSI/ESD S20.20-2021)
    public double MaxAllowedResistanceOhms { get; set; } = 1e9; // 1.0 x 10^9 ohms upper norm boundary
}
