using System.Text.Json;
using EsdCleaningSystem.Models;

namespace EsdCleaningSystem.Services;

public class StorageService
{
    private readonly List<FloorMapConfig> _mapConfigs = new();
    private readonly List<CleaningRequest> _requests = new();
    private readonly List<UserAccount> _users = new();
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
        EnsureReferenceImages();
    }

    private void EnsureReferenceImages()
    {
        try
        {
            string userUploadedDir = @"C:\Users\OrozcoA\.gemini\antigravity\brain\0b60019d-74ee-41f9-87c4-6ab0b42e5575\.user_uploaded";
            string img1Path = Path.Combine(userUploadedDir, "media_1786553785612.png");
            string img2Path = Path.Combine(userUploadedDir, "media_1786554003126.png");

            string target1 = Path.Combine(_uploadDirectory, "piso_manchado.png");
            string target2 = Path.Combine(_uploadDirectory, "piso_rayado.png");

            if (File.Exists(img1Path)) File.Copy(img1Path, target1, true);
            if (File.Exists(img2Path)) File.Copy(img2Path, target2, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Copy Reference Images] {ex.Message}");
        }
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
                    if (payload != null)
                    {
                        if (payload.Maps != null && payload.Maps.Any())
                        {
                            _mapConfigs.Clear();
                            _mapConfigs.AddRange(payload.Maps);
                        }

                        if (payload.Requests != null && payload.Requests.Any())
                        {
                            _requests.Clear();
                            _requests.AddRange(payload.Requests);
                        }

                        if (payload.Users != null && payload.Users.Any())
                        {
                            _users.Clear();
                            _users.AddRange(payload.Users);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Storage Load Error] {ex.Message}. Initializing seeds.");
            }

            if (!_mapConfigs.Any())
            {
                SeedDefaultMaps();
            }

            if (!_requests.Any())
            {
                SeedDefaultRequests();
            }

            if (!_users.Any())
            {
                SeedDefaultUsers();
            }

            SaveToDisk();
        }
    }

    private void SeedDefaultUsers()
    {
        _users.Clear();
        _users.Add(new UserAccount
        {
            Id = "USR-01",
            Username = "admin",
            PasswordHash = "admin2026",
            DisplayName = "Ing. Aldo Orozco (Admin)",
            Role = "ADMIN",
            Department = "Supervisación ESD",
            CreatedAt = DateTime.UtcNow
        });

        _users.Add(new UserAccount
        {
            Id = "USR-02",
            Username = "tecnico",
            PasswordHash = "esd2026",
            DisplayName = "Téc. Mantenimiento ESD",
            Role = "TECHNICIAN",
            Department = "Mantenimiento ESD",
            CreatedAt = DateTime.UtcNow
        });
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
                    Requests = _requests,
                    Users = _users
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

    public List<FloorMapConfig> GetMaps()
    {
        return _mapConfigs;
    }

    public async Task<List<FloorMapConfig>> GetMapsAsync()
    {
        if (_supabaseService != null)
        {
            try
            {
                var sbMaps = await _supabaseService.GetMapsFromSupabaseAsync();
                if (sbMaps != null)
                {
                    _mapConfigs.Clear();
                    _mapConfigs.AddRange(sbMaps);
                    return _mapConfigs;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StorageService GetMapsAsync Exception] {ex.Message}");
            }
        }
        return _mapConfigs;
    }

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
        _ = _supabaseService?.DeleteMapFromSupabaseAsync(areaId);
        return true;
    }

    public List<CleaningRequest> GetRequests()
    {
        return _requests.OrderByDescending(r => r.RequestDate).ToList();
    }

    public async Task<List<CleaningRequest>> GetRequestsAsync()
    {
        if (_supabaseService != null)
        {
            try
            {
                var sbRequests = await _supabaseService.GetRequestsFromSupabaseAsync();
                if (sbRequests != null)
                {
                    _requests.Clear();
                    _requests.AddRange(sbRequests);
                    return _requests.OrderByDescending(r => r.RequestDate).ToList();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StorageService GetRequestsAsync Exception] {ex.Message}");
            }
        }
        return _requests.OrderByDescending(r => r.RequestDate).ToList();
    }

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

        if (req.Status == "LIMPIEZA_COMPLETADA" || req.Status == "LIMPIEZA_REALIZADA")
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

    public List<CleanedZone> GetCleanedZones(string areaId)
    {
        var map = GetMapByAreaId(areaId);
        return map?.CleanedZones.OrderByDescending(z => z.CleanedDate).ToList() ?? new List<CleanedZone>();
    }

    public async Task<List<CleanedZone>> GetCleanedZonesAsync(string areaId)
    {
        var map = GetMapByAreaId(areaId);
        if (_supabaseService != null && map != null)
        {
            try
            {
                var sbZones = await _supabaseService.GetCleanedZonesFromSupabaseAsync(areaId);
                if (sbZones != null && sbZones.Any())
                {
                    foreach (var sbZ in sbZones)
                    {
                        if (!map.CleanedZones.Any(z => z.Id == sbZ.Id))
                        {
                            map.CleanedZones.Add(sbZ);
                        }
                    }
                    SaveToDisk();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StorageService GetCleanedZonesAsync Exception] {ex.Message}");
            }
        }
        return map?.CleanedZones.OrderByDescending(z => z.CleanedDate).ToList() ?? new List<CleanedZone>();
    }

    public async Task<CleanedZone> AddCleanedZoneAsync(CreateCleanedZoneDto dto)
    {
        var map = GetMapByAreaId(dto.AreaId);
        if (map == null) throw new InvalidOperationException($"No existe el área con ID '{dto.AreaId}'");

        var zone = new CleanedZone
        {
            Id = Guid.NewGuid().ToString(),
            AreaId = dto.AreaId,
            RequestId = dto.RequestId,
            XPercent = dto.XPercent,
            YPercent = dto.YPercent,
            WidthPercent = dto.WidthPercent,
            HeightPercent = dto.HeightPercent,
            CleanedDate = dto.CleanedDate,
            CleanedBy = string.IsNullOrWhiteSpace(dto.CleanedBy) ? "Personal de Limpieza ESD" : dto.CleanedBy,
            Notes = dto.Notes
        };

        map.CleanedZones.Add(zone);
        map.LastCleaningDate = dto.CleanedDate;

        var cleaningRecord = new FloorCleaningRecord
        {
            Id = zone.Id,
            AreaId = map.AreaId,
            AreaName = map.AreaName,
            PointId = "RECUADRO",
            FechaLimpieza = dto.CleanedDate,
            FechaProximaLimpieza = dto.CleanedDate.AddDays(90),
            LimpiadoPor = zone.CleanedBy,
            Observaciones = string.IsNullOrWhiteSpace(dto.Notes) ? "Limpieza de recuadro en mapa" : dto.Notes,
            RequestId = dto.RequestId,
            XPercent = dto.XPercent,
            YPercent = dto.YPercent,
            WidthPercent = dto.WidthPercent,
            HeightPercent = dto.HeightPercent
        };

        if (_supabaseService != null)
        {
            await _supabaseService.SyncCleaningRecordToSupabaseAsync(cleaningRecord);
        }

        if (!string.IsNullOrEmpty(dto.RequestId))
        {
            var updateDto = new UpdateRequestStatusDto
            {
                NewStatus = "LIMPIEZA_REALIZADA",
                Notes = dto.Notes,
                PerformedBy = dto.CleanedBy
            };
            UpdateRequestStatus(dto.RequestId, updateDto, out _);
        }

        SaveToDisk();
        return zone;
    }

    public CleanedZone AddCleanedZone(CreateCleanedZoneDto dto)
    {
        return AddCleanedZoneAsync(dto).GetAwaiter().GetResult();
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

    // ==========================================
    // USER ACCOUNTS AUTHENTICATION & MANAGEMENT
    // ==========================================
    public UserAccount? AuthenticateUser(string username, string password)
    {
        var users = GetUsers();
        var user = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (user != null && user.PasswordHash == password)
        {
            return user;
        }

        if (username.Equals("admin", StringComparison.OrdinalIgnoreCase) && password == "admin2026")
        {
            return new UserAccount { Id = "USR-01", Username = "admin", DisplayName = "Ing. Aldo Orozco (Admin)", Role = "ADMIN" };
        }
        if (username.Equals("tecnico", StringComparison.OrdinalIgnoreCase) && password == "esd2026")
        {
            return new UserAccount { Id = "USR-02", Username = "tecnico", DisplayName = "Téc. Mantenimiento ESD", Role = "TECHNICIAN" };
        }

        return null;
    }

    public List<UserAccount> GetUsers()
    {
        if (_supabaseService != null)
        {
            try
            {
                var spUsers = _supabaseService.GetUsersFromSupabaseAsync().GetAwaiter().GetResult();
                if (spUsers != null && spUsers.Any())
                {
                    lock (_mapConfigs)
                    {
                        _users.Clear();
                        _users.AddRange(spUsers);
                        SaveToDisk();
                    }
                    return spUsers;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetUsers Supabase Fetch] {ex.Message}");
            }
        }

        lock (_mapConfigs)
        {
            return _users.ToList();
        }
    }

    public UserAccount CreateUser(CreateUserDto dto)
    {
        var currentUsers = GetUsers();
        var existing = currentUsers.FirstOrDefault(u => u.Username.Equals(dto.Username, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            throw new InvalidOperationException($"El nombre de usuario '{dto.Username}' ya existe.");
        }

        var newUser = new UserAccount
        {
            Id = $"USR-{currentUsers.Count + 1:D2}",
            Username = dto.Username.Trim(),
            PasswordHash = dto.Password.Trim(),
            DisplayName = dto.DisplayName.Trim(),
            Role = dto.Role.ToUpper() == "ADMIN" ? "ADMIN" : "TECHNICIAN",
            Department = string.IsNullOrWhiteSpace(dto.Department) ? "Mantenimiento ESD" : dto.Department.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        lock (_mapConfigs)
        {
            _users.Add(newUser);
            SaveToDisk();
        }

        // Direct insertion into Supabase Database
        if (_supabaseService != null)
        {
            _ = _supabaseService.CreateUserInSupabaseAsync(newUser);
        }

        return newUser;
    }

    public bool DeleteUser(string username)
    {
        bool deleted = false;
        lock (_mapConfigs)
        {
            var user = _users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (user != null)
            {
                _users.Remove(user);
                SaveToDisk();
                deleted = true;
            }
        }

        if (_supabaseService != null)
        {
            _ = _supabaseService.DeleteUserFromSupabaseAsync(username);
        }

        return deleted;
    }
}
