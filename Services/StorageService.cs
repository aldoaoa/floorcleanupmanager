using System.Text.Json;
using EsdCleaningSystem.Models;

namespace EsdCleaningSystem.Services;

public class StorageService
{
    private readonly List<FloorMapConfig> _mapConfigs = new();
    private readonly List<CleaningRequest> _requests = new();
    private readonly string _uploadDirectory;
    private readonly string _dataFilePath;
    private readonly SupabaseService? _supabaseService;

    public StorageService(IWebHostEnvironment env, SupabaseService? supabaseService = null)
    {
        _supabaseService = supabaseService;
        _uploadDirectory = Path.Combine(env.WebRootPath, "uploads");
        if (!Directory.Exists(_uploadDirectory))
        {
            Directory.CreateDirectory(_uploadDirectory);
        }

        string dataDir = Path.Combine(env.ContentRootPath, "Data");
        if (!Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }
        _dataFilePath = Path.Combine(dataDir, "esd_data_store.json");

        LoadFromDisk();
    }

    private void LoadFromDisk()
    {
        lock (_mapConfigs)
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    string json = File.ReadAllText(_dataFilePath);
                    var payload = JsonSerializer.Deserialize<StorageDataPayload>(json);
                    if (payload != null && payload.Maps.Any())
                    {
                        _mapConfigs.Clear();
                        _mapConfigs.AddRange(payload.Maps);
                        _requests.Clear();
                        _requests.AddRange(payload.Requests);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Storage Load Error] {ex.Message}. Initializing seeds.");
            }

            SeedDefaultMaps();
            SeedDefaultRequests();
            SaveToDisk();
        }
    }

    public void SaveToDisk()
    {
        lock (_mapConfigs)
        {
            try
            {
                var payload = new StorageDataPayload
                {
                    Maps = _mapConfigs,
                    Requests = _requests
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(payload, options);
                File.WriteAllText(_dataFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Storage Save Error] {ex.Message}");
            }
        }
    }

    private void SeedDefaultMaps()
    {
        _mapConfigs.Add(new FloorMapConfig
        {
            AreaId = "SMT-01",
            AreaName = "Línea 1 SMT (Montaje Superficial)",
            ImageUrl = "/uploads/smt_floor_plan.svg",
            Criticality = "ALTA",
            FloorType = "Loseta Conductiva ESD + Cera Antiestática",
            StandardCompliance = "ANSI/ESD S20.20-2021",
            LastCleaningDate = DateTime.UtcNow.AddDays(-105),
            Points = new List<MapPoint>
            {
                new MapPoint { Id = "1", Code = "1", Label = "Punto 1", XPercent = 25.5, YPercent = 30.0, ZoneType = "SMT", LastResistanceOhms = 4.5e7, LastMeasurementDate = DateTime.UtcNow.AddDays(-5) },
                new MapPoint { Id = "2", Code = "2", Label = "Punto 2", XPercent = 58.0, YPercent = 45.0, ZoneType = "SMT", LastResistanceOhms = 2.4e8, LastMeasurementDate = DateTime.UtcNow.AddDays(-2) },
                new MapPoint { Id = "3", Code = "3", Label = "Punto 3", XPercent = 80.0, YPercent = 70.0, ZoneType = "SMT", LastResistanceOhms = 3.8e7, LastMeasurementDate = DateTime.UtcNow.AddDays(-10) }
            }
        });

        _mapConfigs.Add(new FloorMapConfig
        {
            AreaId = "ASSY-01",
            AreaName = "Área de Ensamble y Prueba de Tarjetas",
            ImageUrl = "/uploads/assembly_floor_plan.svg",
            Criticality = "MEDIA",
            FloorType = "Loseta Conductiva ESD + Cera Antiestática",
            StandardCompliance = "ANSI/ESD S20.20-2021",
            LastCleaningDate = DateTime.UtcNow.AddDays(-45),
            Points = new List<MapPoint>
            {
                new MapPoint { Id = "1", Code = "1", Label = "Punto 1", XPercent = 30.0, YPercent = 40.0, ZoneType = "Assembly", LastResistanceOhms = 5.2e7, LastMeasurementDate = DateTime.UtcNow.AddDays(-12) },
                new MapPoint { Id = "2", Code = "2", Label = "Punto 2", XPercent = 70.0, YPercent = 60.0, ZoneType = "Testing", LastResistanceOhms = 1.8e8, LastMeasurementDate = DateTime.UtcNow.AddDays(-1) }
            }
        });
    }

    private void SeedDefaultRequests()
    {
        _requests.Add(new CleaningRequest
        {
            Id = "REQ-20260810-001",
            AreaId = "SMT-01",
            AreaName = "Línea 1 SMT (Montaje Superficial)",
            CoordXPercent = 58.0,
            CoordYPercent = 45.0,
            NearestPointId = "P-102",
            Reason = "Derramamiento de polvo aislante y lecturas de resistencia superiores a 1.0e8 Ohms",
            DetailedNotes = "Medición en punto P-02 arrojó 2.4e8 Ω. La capa de cera antiestática muestra desgaste crítico.",
            EvidenceFileName = "evidencia_medicion_esd.jpg",
            EvidenceFileType = "IMAGE",
            EvidenceUrl = "/uploads/smt_floor_plan.svg",
            RequestDate = DateTime.UtcNow.AddHours(-3),
            RequestedBy = "Ing. Roberto Gómez (ESD Champion)",
            AreaLastCleaningDate = DateTime.UtcNow.AddDays(-105),
            DaysSinceLastCleaning = 105,
            LastEsdResistanceOhms = 2.4e8,
            AreaCriticality = "ALTA",
            Meets3MonthRule = true,
            HasHighResistanceOverride = true,
            Priority = "ALTA",
            AuthorizationStatus = "AUTORIZADA",
            Status = "AUTORIZADA",
            EvaluationSummary = "AUTORIZADA (PRIORIDAD ALTA): Resistencia medida excede 1.0E+08 Ω (ANSI/ESD S20.20-2021). Se autoriza aplicación de cera antiestática."
        });
    }

    public List<FloorMapConfig> GetMaps() => _mapConfigs;

    public FloorMapConfig? GetMapByAreaId(string areaId) => 
        _mapConfigs.FirstOrDefault(m => m.AreaId.Equals(areaId, StringComparison.OrdinalIgnoreCase));

    public void SaveOrUpdateMap(FloorMapConfig config)
    {
        var existing = GetMapByAreaId(config.AreaId);
        if (existing != null)
        {
            _mapConfigs.Remove(existing);
        }
        _mapConfigs.Add(config);
        SaveToDisk();
        _ = _supabaseService?.SyncMapToSupabaseAsync(config);
        if (config.Points != null && config.Points.Any())
        {
            _ = _supabaseService?.SyncPointsToSupabaseAsync(config.AreaId, config.Points);
        }
    }

    public bool DeleteMap(string areaId)
    {
        var existing = GetMapByAreaId(areaId);
        if (existing == null) return false;

        _mapConfigs.Remove(existing);
        SaveToDisk();
        _ = _supabaseService?.DeleteMapFromSupabaseAsync(areaId);
        return true;
    }

    public List<CleaningRequest> GetRequests() => _requests.OrderByDescending(r => r.RequestDate).ToList();

    public CleaningRequest? GetRequestById(string id) => _requests.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void SaveRequest(CleaningRequest request)
    {
        _requests.Add(request);
        SaveToDisk();
        _ = _supabaseService?.SyncRequestToSupabaseAsync(request);
    }

    public bool UpdateRequestStatus(string requestId, UpdateRequestStatusDto dto, out CleaningRequest? updatedRequest)
    {
        var req = GetRequestById(requestId);
        if (req == null)
        {
            updatedRequest = null;
            return false;
        }

        req.Status = dto.NewStatus.ToUpper();
        if (!string.IsNullOrEmpty(dto.Notes)) req.StatusNotes = dto.Notes;
        if (!string.IsNullOrEmpty(dto.PerformedBy)) req.CleanedBy = dto.PerformedBy;

        if (req.Status == "LIMPIEZA_COMPLETADA")
        {
            req.CompletedDate = DateTime.UtcNow;
            
            var map = GetMapByAreaId(req.AreaId);
            if (map != null)
            {
                map.LastCleaningDate = DateTime.UtcNow;
            }

            var cleaningRecord = new FloorCleaningRecord
            {
                Id = Guid.NewGuid().ToString(),
                AreaId = req.AreaId,
                AreaName = req.AreaName,
                PointId = req.NearestPointId ?? "ALL",
                FechaLimpieza = DateTime.UtcNow,
                FechaProximaLimpieza = DateTime.UtcNow.AddDays(90),
                LimpiadoPor = req.CleanedBy ?? "Personal ESD",
                Observaciones = req.StatusNotes ?? "Limpieza completada y cera antiestática aplicada.",
                RequestId = req.Id
            };

            _ = _supabaseService?.SyncCleaningRecordToSupabaseAsync(cleaningRecord);
        }

        SaveToDisk();
        _ = _supabaseService?.SyncRequestToSupabaseAsync(req);

        updatedRequest = req;
        return true;
    }

    public string SaveEvidenceFile(string fileName, string base64Data, string bucketName = "evidencias_limpieza")
    {
        try
        {
            if (base64Data.Contains(","))
            {
                base64Data = base64Data.Split(',')[1];
            }

            byte[] bytes = Convert.FromBase64String(base64Data);
            string safeFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
            string filePath = Path.Combine(_uploadDirectory, safeFileName);
            File.WriteAllBytes(filePath, bytes);
            string localUrl = $"/uploads/{safeFileName}";

            // Upload directly to Supabase Storage bucket
            if (_supabaseService != null)
            {
                string ext = Path.GetExtension(fileName).ToLower();
                string contentType = ext == ".pdf" ? "application/pdf" : (ext == ".png" ? "image/png" : "image/jpeg");
                var supabaseTask = _supabaseService.UploadFileToSupabaseStorageAsync(bucketName, safeFileName, bytes, contentType);
                var supabaseUrl = supabaseTask.GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(supabaseUrl))
                {
                    return supabaseUrl;
                }
            }

            return localUrl;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Save File Error] {ex.Message}");
            return string.Empty;
        }
    }
}
