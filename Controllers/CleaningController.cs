using Microsoft.AspNetCore.Mvc;
using EsdCleaningSystem.Models;
using EsdCleaningSystem.Services;

namespace EsdCleaningSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CleaningController : ControllerBase
{
    private readonly StorageService _storageService;
    private readonly SupabaseService _supabaseService;
    private readonly EsdEvaluationEngine _evaluationEngine;
    private readonly SupabaseSettings _supabaseSettings;

    public CleaningController(
        StorageService storageService,
        SupabaseService supabaseService,
        EsdEvaluationEngine evaluationEngine,
        SupabaseSettings supabaseSettings)
    {
        _storageService = storageService;
        _supabaseService = supabaseService;
        _evaluationEngine = evaluationEngine;
        _supabaseSettings = supabaseSettings;
    }

    [HttpGet("maps")]
    public async Task<IActionResult> GetMaps()
    {
        var maps = await _storageService.GetMapsAsync();
        return Ok(maps);
    }

    private EsdMeasurement? FindMatchingMeasurement(MapPoint point, List<EsdMeasurement> measurements)
    {
        if (measurements == null || !measurements.Any()) return null;

        string Normalize(string str) => new string((str ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        string normCode = Normalize(point.Code);
        string normLabel = Normalize(point.Label);
        string normId = Normalize(point.Id);

        // 1. Exact match
        var match = measurements.FirstOrDefault(m => 
            m.PointId.Equals(point.Code, StringComparison.OrdinalIgnoreCase) || 
            m.PointId.Equals(point.Id, StringComparison.OrdinalIgnoreCase) ||
            m.PointName.Equals(point.Code, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        // 2. Normalized match (ignoring hyphens, spaces, leading zeroes)
        match = measurements.FirstOrDefault(m => {
            string normPointId = Normalize(m.PointId);
            string normPointName = Normalize(m.PointName);
            return normPointId == normCode || normPointId == normId || normPointName == normCode || normPointName == normLabel;
        });
        if (match != null) return match;

        // 3. Substring match
        match = measurements.FirstOrDefault(m => 
            m.PointId.Contains(point.Code, StringComparison.OrdinalIgnoreCase) ||
            point.Code.Contains(m.PointId, StringComparison.OrdinalIgnoreCase));
        
        return match;
    }

    [HttpGet("debug/supabase")]
    public async Task<IActionResult> DebugSupabaseConnection([FromQuery] string? areaId)
    {
        string targetArea = areaId ?? "Cuarto 1";
        var (statusCode, rawJson, items) = await _supabaseService.GetRawTableResponseAsync(targetArea);
        var maps = _storageService.GetMaps();

        return Ok(new
        {
            supabaseUrl = _supabaseSettings.Url,
            tableName = _supabaseSettings.TableName,
            targetArea = targetArea,
            httpStatusCode = statusCode,
            rawJsonResponse = rawJson,
            totalRowsFetched = items.Count,
            measurements = items,
            configuredMaps = maps.Select(m => new {
                areaId = m.AreaId,
                areaName = m.AreaName,
                pointsCount = m.Points?.Count ?? 0,
                points = m.Points
            })
        });
    }

    [HttpPost("maps/upload")]
    public IActionResult UploadMapImage([FromBody] UploadMapDto dto)
    {
        if (string.IsNullOrEmpty(dto.AreaId) || string.IsNullOrEmpty(dto.ImageBase64))
        {
            return BadRequest(new { message = "Se requiere AreaId y la imagen en Base64." });
        }

        string fileName = string.IsNullOrEmpty(dto.FileName) ? $"{dto.AreaId}_map.png" : dto.FileName;
        string relativeUrl = _storageService.SaveEvidenceFile(fileName, dto.ImageBase64, "mapas_piso");

        var existingMap = _storageService.GetMapByAreaId(dto.AreaId);
        if (existingMap != null)
        {
            existingMap.ImageUrl = relativeUrl;
            if (!string.IsNullOrEmpty(dto.AreaName)) existingMap.AreaName = dto.AreaName;
            if (!string.IsNullOrEmpty(dto.Criticality)) existingMap.Criticality = dto.Criticality;
        }
        else
        {
            existingMap = new FloorMapConfig
            {
                AreaId = dto.AreaId,
                AreaName = string.IsNullOrEmpty(dto.AreaName) ? $"Área {dto.AreaId}" : dto.AreaName,
                ImageUrl = relativeUrl,
                Criticality = string.IsNullOrEmpty(dto.Criticality) ? "MEDIA" : dto.Criticality,
                LastCleaningDate = DateTime.UtcNow.AddDays(-100)
            };
            _storageService.SaveOrUpdateMap(existingMap);
        }

        return Ok(new { message = "Mapa cargado correctamente", map = existingMap });
    }

    [HttpPost("maps/points")]
    public async Task<IActionResult> SaveMapPoints([FromBody] SaveMapPointsDto dto)
    {
        var map = _storageService.GetMapByAreaId(dto.AreaId);
        if (map == null) return NotFound(new { message = $"Área '{dto.AreaId}' no encontrada." });

        map.Points = dto.Points;
        if (!string.IsNullOrEmpty(dto.Criticality)) map.Criticality = dto.Criticality;
        if (dto.LastCleaningDate.HasValue) map.LastCleaningDate = dto.LastCleaningDate.Value;

        // 1. Save locally to JSON disk storage
        _storageService.SaveOrUpdateMap(map);

        // 2. Explicitly sync points to Supabase table 'puntos_medicion_esd'
        await _supabaseService.SyncPointsToSupabaseAsync(map.AreaId, dto.Points);

        return Ok(new { message = "Coordenadas y puntos de medición actualizados y guardados en Supabase.", map });
    }

    [HttpDelete("maps/{areaId}")]
    public IActionResult DeleteMap(string areaId)
    {
        bool deleted = _storageService.DeleteMap(areaId);
        if (!deleted) return NotFound(new { message = $"No se encontró el área '{areaId}'." });
        return Ok(new { message = $"Área '{areaId}' eliminada correctamente." });
    }

    [HttpPut("maps/{areaId}")]
    public IActionResult UpdateMap(string areaId, [FromBody] UploadMapDto dto)
    {
        var existingMap = _storageService.GetMapByAreaId(areaId);
        if (existingMap == null) return NotFound(new { message = $"Área '{areaId}' no encontrada." });

        if (!string.IsNullOrEmpty(dto.AreaName)) existingMap.AreaName = dto.AreaName;
        if (!string.IsNullOrEmpty(dto.Criticality)) existingMap.Criticality = dto.Criticality;
        if (!string.IsNullOrEmpty(dto.ImageBase64))
        {
            string fileName = string.IsNullOrEmpty(dto.FileName) ? $"{areaId}_map.png" : dto.FileName;
            string relativeUrl = _storageService.SaveEvidenceFile(fileName, dto.ImageBase64, "mapas_piso");
            existingMap.ImageUrl = relativeUrl;
        }

        _storageService.SaveOrUpdateMap(existingMap);
        return Ok(new { message = $"Área '{areaId}' actualizada correctamente.", map = existingMap });
    }

    [HttpGet("measurements/{areaId}")]
    public async Task<IActionResult> GetMeasurements(string areaId)
    {
        var measurements = await _supabaseService.GetMeasurementsForAreaAsync(areaId);
        return Ok(measurements);
    }

    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests()
    {
        var requests = await _storageService.GetRequestsAsync();
        return Ok(requests);
    }

    [HttpPost("requests")]
    public async Task<IActionResult> CreateRequest([FromBody] CreateCleaningRequestDto dto)
    {
        if (string.IsNullOrEmpty(dto.AreaId))
            return BadRequest(new { message = "Debe seleccionar un área en el mapa." });

        if (string.IsNullOrEmpty(dto.Reason))
            return BadRequest(new { message = "Debe especificar el motivo de la solicitud de limpieza." });

        if (string.IsNullOrEmpty(dto.EvidenceBase64))
            return BadRequest(new { message = "Es obligatorio adjuntar evidencia (imagen, documento PDF o fotografía)." });

        var mapConfig = _storageService.GetMapByAreaId(dto.AreaId);
        if (mapConfig == null)
            return BadRequest(new { message = $"El área '{dto.AreaId}' no existe en el sistema." });

        // Save evidence file
        string fileName = string.IsNullOrEmpty(dto.EvidenceFileName) ? "evidencia.png" : dto.EvidenceFileName;
        string evidenceUrl = _storageService.SaveEvidenceFile(fileName, dto.EvidenceBase64);

        // Fetch nearest ESD measurement from Supabase
        var nearestMeasurement = await _supabaseService.GetNearestMeasurementAsync(dto.AreaId, dto.CoordXPercent, dto.CoordYPercent);

        var request = new CleaningRequest
        {
            AreaId = dto.AreaId,
            CoordXPercent = dto.CoordXPercent,
            CoordYPercent = dto.CoordYPercent,
            Reason = dto.Reason,
            DetailedNotes = dto.DetailedNotes,
            RequestedBy = string.IsNullOrEmpty(dto.RequestedBy) ? "Operador Planta" : dto.RequestedBy,
            EvidenceFileName = fileName,
            EvidenceFileType = string.IsNullOrEmpty(dto.EvidenceFileType) ? "IMAGE" : dto.EvidenceFileType.ToUpper(),
            EvidenceUrl = evidenceUrl,
            NearestPointId = nearestMeasurement?.PointId ?? "GENERAL"
        };

        // Apply ANSI/ESD S20.20-2021 & 3-month rule evaluation engine
        var evaluatedRequest = _evaluationEngine.EvaluateRequest(request, mapConfig, nearestMeasurement);

        // Store request and sync to Supabase
        _storageService.SaveRequest(evaluatedRequest);

        return Ok(new
        {
            message = "Solicitud creada y evaluada exitosamente",
            request = evaluatedRequest
        });
    }

    [HttpPost("requests/{id}/status")]
    public IActionResult UpdateRequestStatus(string id, [FromBody] UpdateRequestStatusDto dto)
    {
        if (string.IsNullOrEmpty(dto.NewStatus))
            return BadRequest(new { message = "Debe proporcionar el nuevo estado." });

        bool updated = _storageService.UpdateRequestStatus(id, dto, out var request);
        if (!updated || request == null)
            return NotFound(new { message = $"No se encontró la solicitud con ID '{id}'." });

        return Ok(new
        {
            message = $"Estado de la solicitud actualizado a '{request.Status}'.",
            request = request
        });
    }

    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        return Ok(_supabaseSettings);
    }

    [HttpGet("zones/{areaId}")]
    public async Task<IActionResult> GetCleanedZones(string areaId)
    {
        var zones = await _storageService.GetCleanedZonesAsync(areaId);
        return Ok(zones);
    }

    [HttpPost("zones")]
    public async Task<IActionResult> AddCleanedZone([FromBody] CreateCleanedZoneDto dto)
    {
        if (string.IsNullOrEmpty(dto.AreaId))
            return BadRequest(new { message = "El área es obligatoria." });

        try
        {
            var zone = await _storageService.AddCleanedZoneAsync(dto);
            return Ok(new { message = "Recuadro de área limpiada guardado correctamente.", zone });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetCleaningHistory()
    {
        var history = await _supabaseService.GetCleaningHistoryFromSupabaseAsync();
        return Ok(history);
    }

    [HttpPost("settings")]
    public IActionResult UpdateSettings([FromBody] SupabaseSettings newSettings)
    {
        _supabaseSettings.Url = newSettings.Url;
        _supabaseSettings.AnonKey = newSettings.AnonKey;
        _supabaseSettings.TableName = newSettings.TableName;
        _supabaseSettings.UseMockFallback = newSettings.UseMockFallback;
        _supabaseSettings.MinimumCleaningIntervalDays = newSettings.MinimumCleaningIntervalDays;
        _supabaseSettings.HighResistanceThresholdOhms = newSettings.HighResistanceThresholdOhms;

        return Ok(new { message = "Configuración de Supabase y ANSI/ESD actualizada correctamente", settings = _supabaseSettings });
    }

    // ==========================================
    // AUTHENTICATION & USER MANAGEMENT ENDPOINTS
    // ==========================================
    [HttpPost("/api/auth/login")]
    public IActionResult Login([FromBody] LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
        {
            return BadRequest(new { message = "Debe ingresar usuario y contraseña." });
        }

        var user = _storageService.AuthenticateUser(dto.Username, dto.Password);
        if (user == null)
        {
            return Unauthorized(new { message = "Usuario o contraseña incorrectos." });
        }

        return Ok(new
        {
            message = "Inicio de sesión exitoso.",
            user = new
            {
                id = user.Id,
                username = user.Username,
                displayName = user.DisplayName,
                role = user.Role,
                department = user.Department
            }
        });
    }

    [HttpGet("/api/auth/users")]
    public IActionResult GetUsers()
    {
        var users = _storageService.GetUsers();
        return Ok(users);
    }

    [HttpPost("/api/auth/users")]
    public IActionResult CreateUser([FromBody] CreateUserDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password) || string.IsNullOrWhiteSpace(dto.DisplayName))
        {
            return BadRequest(new { message = "Nombre de usuario, nombre completo y contraseña son obligatorios." });
        }

        try
        {
            var newUser = _storageService.CreateUser(dto);
            return Ok(new { message = $"Usuario '{newUser.Username}' creado con éxito.", user = newUser });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("/api/auth/users/{username}")]
    public IActionResult DeleteUser(string username)
    {
        bool deleted = _storageService.DeleteUser(username);
        if (deleted)
        {
            return Ok(new { message = $"Usuario '{username}' eliminado correctamente." });
        }
        return NotFound(new { message = $"El usuario '{username}' no existe." });
    }
}

public class UploadMapDto
{
    public string AreaId { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;
    public string ImageBase64 { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Criticality { get; set; } = "MEDIA";
}

public class SaveMapPointsDto
{
    public string AreaId { get; set; } = string.Empty;
    public string Criticality { get; set; } = "MEDIA";
    public DateTime? LastCleaningDate { get; set; }
    public List<MapPoint> Points { get; set; } = new();
}
