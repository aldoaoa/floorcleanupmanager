using System.Net.Http.Headers;
using System.Text.Json;
using EsdCleaningSystem.Models;

namespace EsdCleaningSystem.Services;

public class SupabaseService
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseSettings _settings;
    private readonly List<EsdMeasurement> _mockDatabase = new();

    public SupabaseService(HttpClient httpClient, SupabaseSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;

        InitializeMockData();
    }

    private void InitializeMockData()
    {
        _mockDatabase.AddRange(new[]
        {
            new EsdMeasurement { Id = "M-101", AreaId = "Cuarto 1", PointId = "1", PointName = "Punto 1", CoordX = 25.5, CoordY = 30.0, ResistanceOhms = 896000.0, VoltageVolts = 35, MeasurementDate = DateTime.UtcNow.AddDays(-5), Inspector = "Aldo Orozco" },
            new EsdMeasurement { Id = "M-102", AreaId = "Cuarto 1", PointId = "2", PointName = "Punto 2", CoordX = 58.0, CoordY = 45.0, ResistanceOhms = 2.4e8, VoltageVolts = 85, MeasurementDate = DateTime.UtcNow.AddDays(-2), Inspector = "Aldo Orozco" },
            new EsdMeasurement { Id = "M-103", AreaId = "Cuarto 1", PointId = "3", PointName = "Punto 3", CoordX = 80.0, CoordY = 70.0, ResistanceOhms = 3.8e7, VoltageVolts = 28, MeasurementDate = DateTime.UtcNow.AddDays(-10), Inspector = "Aldo Orozco" }
        });
    }

    public async Task<FloorCleaningRecord?> GetLastCleaningForAreaAsync(string areaId)
    {
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project")) return null;

        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/limpiezas_piso?select=*&order=fecha_limpieza.desc&limit=10";
            var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);

            var response = await _httpClient.SendAsync(httpRequest);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        string c = GetPropertyString(elem, "cuarto", "area_id", "area") ?? "";
                        if (CleanString(c) == CleanString(areaId) || string.IsNullOrEmpty(areaId))
                        {
                            var rec = new FloorCleaningRecord();
                            rec.Id = GetPropertyString(elem, "id") ?? Guid.NewGuid().ToString();
                            rec.AreaId = c;
                            rec.AreaName = GetPropertyString(elem, "area_name", "cuarto") ?? c;
                            rec.PointId = GetPropertyString(elem, "punto", "point_id") ?? "ALL";
                            rec.LimpiadoPor = GetPropertyString(elem, "limpiado_por", "tecnico", "usuario") ?? "Personal ESD";

                            string? dateClean = GetPropertyString(elem, "fecha_limpieza", "fecha");
                            if (!string.IsNullOrEmpty(dateClean) && DateTimeOffset.TryParse(dateClean, out var dtoClean))
                            {
                                rec.FechaLimpieza = dtoClean.UtcDateTime;
                            }

                            string? dateNext = GetPropertyString(elem, "fecha_proxima_limpieza", "proxima_limpieza");
                            if (!string.IsNullOrEmpty(dateNext) && DateTimeOffset.TryParse(dateNext, out var dtoNext))
                            {
                                rec.FechaProximaLimpieza = dtoNext.UtcDateTime;
                            }
                            else
                            {
                                rec.FechaProximaLimpieza = rec.FechaLimpieza.AddDays(90);
                            }

                            return rec;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase limpiezas_piso Get Error] {ex.Message}");
        }

        return null;
    }

    public async Task SyncCleaningRecordToSupabaseAsync(FloorCleaningRecord record)
    {
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project")) return;

        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/limpiezas_piso";
            
            // Try payload with rectangle coordinates
            var payload = new Dictionary<string, object?>
            {
                ["id"] = record.Id,
                ["cuarto"] = record.AreaId,
                ["area_name"] = record.AreaName,
                ["punto"] = record.PointId,
                ["fecha_limpieza"] = record.FechaLimpieza,
                ["fecha_proxima_limpieza"] = record.FechaProximaLimpieza,
                ["limpiado_por"] = record.LimpiadoPor,
                ["observaciones"] = record.Observaciones,
                ["request_id"] = record.RequestId,
                ["x_percent"] = record.XPercent,
                ["y_percent"] = record.YPercent,
                ["width_percent"] = record.WidthPercent,
                ["height_percent"] = record.HeightPercent
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);
            httpRequest.Headers.Add("Prefer", "resolution=merge-duplicates");
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest);
            Console.WriteLine($"[Supabase limpiezas_piso Primary Sync] ID '{record.Id}' -> Status {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                // Fallback payload without rectangle columns (embedded in observaciones)
                string fullObs = string.IsNullOrWhiteSpace(record.Observaciones) 
                    ? $"[Recuadro X:{record.XPercent}%, Y:{record.YPercent}%, Ancho:{record.WidthPercent}%, Alto:{record.HeightPercent}%]" 
                    : $"{record.Observaciones} [Recuadro X:{record.XPercent}%, Y:{record.YPercent}%, Ancho:{record.WidthPercent}%, Alto:{record.HeightPercent}%]";

                var fallbackPayload = new
                {
                    id = record.Id,
                    cuarto = record.AreaId,
                    area_name = record.AreaName,
                    punto = record.PointId,
                    fecha_limpieza = record.FechaLimpieza,
                    fecha_proxima_limpieza = record.FechaProximaLimpieza,
                    limpiado_por = record.LimpiadoPor,
                    observaciones = fullObs,
                    request_id = record.RequestId
                };

                var fallbackRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
                fallbackRequest.Headers.Add("apikey", _settings.AnonKey);
                fallbackRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);
                fallbackRequest.Headers.Add("Prefer", "resolution=merge-duplicates");
                fallbackRequest.Content = new StringContent(JsonSerializer.Serialize(fallbackPayload), System.Text.Encoding.UTF8, "application/json");

                var fallbackResponse = await _httpClient.SendAsync(fallbackRequest);
                Console.WriteLine($"[Supabase limpiezas_piso Fallback Sync] ID '{record.Id}' -> Status {fallbackResponse.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase limpiezas_piso Sync Exception] {ex.Message}");
        }
    }

    public async Task<(int statusCode, string rawJson, List<EsdMeasurement> items)> GetRawTableResponseAsync(string areaId)
    {
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project"))
        {
            var mock = GetMockForArea(areaId);
            return (200, JsonSerializer.Serialize(mock), mock);
        }

        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/{_settings.TableName}?select=*";
            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Add("apikey", _settings.AnonKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);

            var response = await _httpClient.SendAsync(request);
            string content = await response.Content.ReadAsStringAsync();
            int statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                using var jsonDoc = JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    var parsedList = new List<EsdMeasurement>();
                    int index = 0;
                    string cleanAreaId = CleanString(areaId);

                    foreach (var elem in root.EnumerateArray())
                    {
                        var m = ParseElement(elem, index++);
                        string cleanMId = CleanString(m.AreaId);

                        if (string.IsNullOrEmpty(areaId) || 
                            cleanMId == cleanAreaId ||
                            cleanMId.Contains(cleanAreaId) ||
                            cleanAreaId.Contains(cleanMId) ||
                            string.IsNullOrEmpty(cleanMId))
                        {
                            if (string.IsNullOrEmpty(m.AreaId)) m.AreaId = areaId;
                            parsedList.Add(m);
                        }
                    }

                    return (statusCode, content, parsedList);
                }
            }

            return (statusCode, content, new List<EsdMeasurement>());
        }
        catch (Exception ex)
        {
            return (500, ex.Message, GetMockForArea(areaId));
        }
    }

    public async Task<List<EsdMeasurement>> GetMeasurementsForAreaAsync(string areaId)
    {
        var (_, _, items) = await GetRawTableResponseAsync(areaId);
        return items;
    }

    private string CleanString(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return new string(input.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private List<EsdMeasurement> GetMockForArea(string areaId)
    {
        return _mockDatabase.Where(m => CleanString(m.AreaId) == CleanString(areaId)).ToList();
    }

    private JsonElement? GetPropertyCaseInsensitive(JsonElement elem, params string[] propertyNames)
    {
        if (elem.ValueKind != JsonValueKind.Object) return null;

        foreach (var prop in elem.EnumerateObject())
        {
            string cleanPropName = CleanString(prop.Name);
            foreach (var name in propertyNames)
            {
                if (cleanPropName == CleanString(name))
                {
                    return prop.Value;
                }
            }
        }
        return null;
    }

    private EsdMeasurement ParseElement(JsonElement elem, int index)
    {
        var m = new EsdMeasurement();
        
        m.Id = GetPropertyString(elem, "id") ?? $"SB-{index + 1}";
        m.AreaId = GetPropertyString(elem, "cuarto", "area_id", "area", "linea", "zona", "id_area") ?? "Cuarto 1";
        m.PointId = GetPropertyString(elem, "punto", "point_id", "id_punto", "codigo", "ubicacion", "punto_medicion") ?? $"{index + 1}";
        m.PointName = GetPropertyString(elem, "punto", "point_name", "nombre", "descripcion", "etiqueta") ?? $"Punto {m.PointId}";
        
        m.ResistanceOhms = GetPropertyDouble(elem, "medicion_ohms", "resistance_ohms", "resistencia", "resistencia_ohms", "medicion", "valor_ohms", "lectura") ?? 4.5e7;
        m.VoltageVolts = GetPropertyDouble(elem, "voltage_volts", "voltaje", "voltios", "carga_generada") ?? 30.0;
        
        m.CoordX = GetPropertyDouble(elem, "coord_x", "x", "pos_x") ?? (25.0 + (index * 25) % 60);
        m.CoordY = GetPropertyDouble(elem, "coord_y", "y", "pos_y") ?? (30.0 + (index * 20) % 50);

        m.Inspector = GetPropertyString(elem, "inspector", "operador", "usuario", "auditor", "usuario_creacion", "creado_por") ?? "Aldo Orozco";

        string? dateStr = GetPropertyString(elem, "fecha_medicion", "fecha", "measurement_date", "created_at", "timestamp");
        if (!string.IsNullOrEmpty(dateStr))
        {
            if (DateTimeOffset.TryParse(dateStr, out var dto))
            {
                m.MeasurementDate = dto.UtcDateTime;
            }
            else if (DateTime.TryParse(dateStr, out var d))
            {
                m.MeasurementDate = d;
            }
        }

        return m;
    }

    private string? GetPropertyString(JsonElement elem, params string[] propertyNames)
    {
        var propVal = GetPropertyCaseInsensitive(elem, propertyNames);
        if (propVal.HasValue)
        {
            var p = propVal.Value;
            if (p.ValueKind == JsonValueKind.String) return p.GetString();
            if (p.ValueKind == JsonValueKind.Number) return p.GetRawText();
            if (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False) return p.GetRawText();
        }
        return null;
    }

    private double? GetPropertyDouble(JsonElement elem, params string[] propertyNames)
    {
        var propVal = GetPropertyCaseInsensitive(elem, propertyNames);
        if (propVal.HasValue)
        {
            var p = propVal.Value;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var val)) return val;
            if (p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), out var parsedVal)) return parsedVal;
        }
        return null;
    }

    public async Task<EsdMeasurement?> GetNearestMeasurementAsync(string areaId, double coordX, double coordY)
    {
        var measurements = await GetMeasurementsForAreaAsync(areaId);
        if (!measurements.Any()) return null;

        return measurements
            .OrderBy(m => Math.Pow(m.CoordX - coordX, 2) + Math.Pow(m.CoordY - coordY, 2))
            .FirstOrDefault();
    }

    public void AddOrUpdateMeasurement(EsdMeasurement measurement)
    {
        var existing = _mockDatabase.FirstOrDefault(m => m.Id == measurement.Id || (m.AreaId == measurement.AreaId && m.PointId == measurement.PointId));
        if (existing != null)
        {
            _mockDatabase.Remove(existing);
        }
        _mockDatabase.Add(measurement);
    }

    public async Task<string?> UploadFileToSupabaseStorageAsync(string bucketName, string fileName, byte[] fileBytes, string contentType = "image/png")
    {
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project")) return null;

        try
        {
            string safeFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
            string endpoint = $"{_settings.Url.TrimEnd('/')}/storage/v1/object/{bucketName}/{safeFileName}";
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);

            var byteContent = new ByteArrayContent(fileBytes);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            httpRequest.Content = byteContent;

            var response = await _httpClient.SendAsync(httpRequest);
            if (response.IsSuccessStatusCode)
            {
                string publicUrl = $"{_settings.Url.TrimEnd('/')}/storage/v1/object/public/{bucketName}/{safeFileName}";
                Console.WriteLine($"[Supabase Storage Upload OK] Bucket: '{bucketName}', URL: {publicUrl}");
                return publicUrl;
            }
            else
            {
                Console.WriteLine($"[Supabase Storage Upload Fail] Bucket: '{bucketName}', Status: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase Storage Exception] {ex.Message}");
        }
        return null;
    }

    public async Task SyncRequestToSupabaseAsync(CleaningRequest request)
    {
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project")) return;

        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/solicitudes_limpieza";
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);
            httpRequest.Headers.Add("Prefer", "resolution=merge-duplicates");

            var payload = new
            {
                id = request.Id,
                area_id = request.AreaId,
                area_name = request.AreaName,
                coord_x = request.CoordXPercent,
                coord_y = request.CoordYPercent,
                reason = request.Reason,
                notes = request.DetailedNotes,
                requested_by = request.RequestedBy,
                priority = request.Priority,
                status = request.Status,
                evidence_url = request.EvidenceUrl,
                evidence_file_type = request.EvidenceFileType,
                evidence_file_name = request.EvidenceFileName,
                resistance_ohms = request.LastEsdResistanceOhms,
                request_date = request.RequestDate,
                completed_date = request.CompletedDate,
                cleaned_by = request.CleanedBy
            };

            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.SendAsync(httpRequest);
            Console.WriteLine($"[Supabase Request Sync] ID '{request.Id}' -> {response.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase Request Sync Exception] {ex.Message}");
        }
    }

    public async Task DeleteMapFromSupabaseAsync(string areaId)
    {
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project")) return;

        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/configuracion_mapas?area_id=eq.{Uri.EscapeDataString(areaId)}";
            var httpRequest = new HttpRequestMessage(HttpMethod.Delete, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);

            var response = await _httpClient.SendAsync(httpRequest);
            Console.WriteLine($"[Supabase Delete Map] Area '{areaId}' -> Status {response.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase Delete Map Exception] {ex.Message}");
        }
    }

    public async Task SyncMapToSupabaseAsync(FloorMapConfig map)
    {
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project")) return;

        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/configuracion_mapas";
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);
            httpRequest.Headers.Add("Prefer", "resolution=merge-duplicates");

            var payload = new
            {
                area_id = map.AreaId,
                area_name = map.AreaName,
                image_url = map.ImageUrl,
                criticality = map.Criticality,
                floor_type = map.FloorType,
                standard_compliance = map.StandardCompliance,
                last_cleaning_date = map.LastCleaningDate
            };

            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.SendAsync(httpRequest);
            Console.WriteLine($"[Supabase Map Sync] Area '{map.AreaId}' -> {response.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase Map Sync Exception] {ex.Message}");
        }
    }

    public async Task SyncPointsToSupabaseAsync(string areaId, List<MapPoint> points)
    {
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project") || points == null || !points.Any()) return;

        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/puntos_medicion_esd";
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);
            httpRequest.Headers.Add("Prefer", "resolution=merge-duplicates");

            var payloadList = points.Select(p => new
            {
                id = p.Id,
                area_id = areaId,
                code = p.Code,
                label = p.Label,
                x_percent = p.XPercent,
                y_percent = p.YPercent,
                zone_type = p.ZoneType,
                last_resistance_ohms = p.LastResistanceOhms,
                last_measurement_date = p.LastMeasurementDate ?? DateTime.UtcNow
            }).ToList();

            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payloadList), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.SendAsync(httpRequest);
            Console.WriteLine($"[Supabase Points Sync] Area '{areaId}' ({points.Count} pts) -> {response.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase Points Sync Exception] {ex.Message}");
        }
    }

    public async Task<List<CleaningRequest>> GetRequestsFromSupabaseAsync()
    {
        var result = new List<CleaningRequest>();
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project")) return result;

        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/solicitudes_limpieza?select=*&order=request_date.desc";
            var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);

            var response = await _httpClient.SendAsync(httpRequest);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        var req = new CleaningRequest
                        {
                            Id = GetPropertyString(elem, "id") ?? Guid.NewGuid().ToString(),
                            AreaId = GetPropertyString(elem, "area_id", "cuarto") ?? "",
                            AreaName = GetPropertyString(elem, "area_name") ?? "",
                            CoordXPercent = GetPropertyDouble(elem, "coord_x") ?? 0,
                            CoordYPercent = GetPropertyDouble(elem, "coord_y") ?? 0,
                            Reason = GetPropertyString(elem, "reason") ?? "",
                            DetailedNotes = GetPropertyString(elem, "notes") ?? "",
                            RequestedBy = GetPropertyString(elem, "requested_by") ?? "Operador ESD",
                            Priority = GetPropertyString(elem, "priority") ?? "MEDIA",
                            Status = GetPropertyString(elem, "status") ?? "AUTORIZADA",
                            AuthorizationStatus = GetPropertyString(elem, "status") ?? "AUTORIZADA",
                            EvidenceUrl = GetPropertyString(elem, "evidence_url") ?? "",
                            EvidenceFileType = GetPropertyString(elem, "evidence_file_type") ?? "",
                            EvidenceFileName = GetPropertyString(elem, "evidence_file_name") ?? "",
                            LastEsdResistanceOhms = GetPropertyDouble(elem, "resistance_ohms") ?? 4.5e7,
                            CleanedBy = GetPropertyString(elem, "cleaned_by") ?? ""
                        };

                        string? reqDateStr = GetPropertyString(elem, "request_date");
                        if (!string.IsNullOrEmpty(reqDateStr) && DateTimeOffset.TryParse(reqDateStr, out var dtoReq))
                            req.RequestDate = dtoReq.UtcDateTime;

                        string? compDateStr = GetPropertyString(elem, "completed_date");
                        if (!string.IsNullOrEmpty(compDateStr) && DateTimeOffset.TryParse(compDateStr, out var dtoComp))
                            req.CompletedDate = dtoComp.UtcDateTime;

                        result.Add(req);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase solicitudes_limpieza Fetch Error] {ex.Message}");
        }

        return result;
    }

    public async Task<List<FloorMapConfig>> GetMapsFromSupabaseAsync()
    {
        var mapDict = new Dictionary<string, FloorMapConfig>(StringComparer.OrdinalIgnoreCase);

        // 1. Fetch maps from configuracion_mapas in Supabase
        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/configuracion_mapas?select=*";
            var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);

            var response = await _httpClient.SendAsync(httpRequest);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        string areaId = GetPropertyString(elem, "area_id") ?? "";
                        if (string.IsNullOrEmpty(areaId)) continue;

                        var map = new FloorMapConfig
                        {
                            AreaId = areaId,
                            AreaName = GetPropertyString(elem, "area_name") ?? GetAreaDisplayName(areaId),
                            ImageUrl = GetPropertyString(elem, "image_url") ?? GetImageUrlForArea(areaId),
                            Criticality = GetPropertyString(elem, "criticality") ?? "MEDIA",
                            FloorType = GetPropertyString(elem, "floor_type") ?? "Loseta Conductiva con Cera Antiestática",
                            StandardCompliance = GetPropertyString(elem, "standard_compliance") ?? "ANSI/ESD S20.20-2021"
                        };

                        string? cleanDateStr = GetPropertyString(elem, "last_cleaning_date");
                        if (!string.IsNullOrEmpty(cleanDateStr) && DateTimeOffset.TryParse(cleanDateStr, out var dtoClean))
                            map.LastCleaningDate = dtoClean.UtcDateTime;

                        mapDict[areaId] = map;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase configuracion_mapas Fetch Error] {ex.Message}");
        }

        // 2. Fetch points from puntos_medicion_esd in Supabase
        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/puntos_medicion_esd?select=*";
            var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);

            var response = await _httpClient.SendAsync(httpRequest);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        string areaId = GetPropertyString(elem, "area_id", "cuarto") ?? "";
                        if (string.IsNullOrEmpty(areaId)) continue;

                        if (!mapDict.TryGetValue(areaId, out var map))
                        {
                            map = new FloorMapConfig
                            {
                                AreaId = areaId,
                                AreaName = GetAreaDisplayName(areaId),
                                ImageUrl = GetImageUrlForArea(areaId)
                            };
                            mapDict[areaId] = map;
                        }

                        var pt = new MapPoint
                        {
                            Id = GetPropertyString(elem, "id", "code") ?? Guid.NewGuid().ToString(),
                            Code = GetPropertyString(elem, "code", "id") ?? "1",
                            Label = GetPropertyString(elem, "label", "nombre") ?? "Punto de Medición ESD",
                            XPercent = GetPropertyDouble(elem, "x_percent", "coord_x") ?? 10.0,
                            YPercent = GetPropertyDouble(elem, "y_percent", "coord_y") ?? 10.0,
                            ZoneType = GetPropertyString(elem, "zone_type") ?? "SMT",
                            LastResistanceOhms = GetPropertyDouble(elem, "last_resistance_ohms", "medicion_ohms") ?? 4.5e7
                        };

                        if (map.Points == null) map.Points = new List<MapPoint>();
                        if (!map.Points.Any(p => p.Id == pt.Id || p.Code == pt.Code))
                        {
                            map.Points.Add(pt);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase puntos_medicion_esd Fetch Error] {ex.Message}");
        }

        // 3. Link real measurements from validacion_piso to mapped points
        try
        {
            var measurements = await GetMeasurementsForAreaAsync("");
            if (measurements != null && measurements.Any())
            {
                foreach (var map in mapDict.Values)
                {
                    if (map.Points != null)
                    {
                        var areaMeasurements = measurements.Where(m => string.Equals(CleanString(m.AreaId), CleanString(map.AreaId), StringComparison.OrdinalIgnoreCase)).ToList();
                        foreach (var pt in map.Points)
                        {
                            var match = areaMeasurements
                                .Where(m => CleanString(m.PointId) == CleanString(pt.Code) || CleanString(m.PointId) == CleanString(pt.Label))
                                .OrderByDescending(m => m.MeasurementDate)
                                .FirstOrDefault();

                            if (match != null)
                            {
                                pt.LastResistanceOhms = match.ResistanceOhms;
                                pt.LastMeasurementDate = match.MeasurementDate;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase Link Measurements Error] {ex.Message}");
        }

        // Set criticality from points
        foreach (var m in mapDict.Values)
        {
            if (m.Points != null && m.Points.Any(p => p.LastResistanceOhms > 1e8))
            {
                m.Criticality = "ALTA";
            }
        }

        return mapDict.Values.ToList();
    }

    private string GetImageUrlForArea(string areaId)
    {
        string clean = CleanString(areaId);
        if (clean.Contains("cuarto1") || clean.Contains("room1")) return "/uploads/a225e05c-e4ff-4a94-a17e-d6ae699e834e_1.png";
        if (clean.Contains("cuarto2") || clean.Contains("room2")) return "/uploads/4b1fa962-3a75-45b3-bee2-f96002ecd826_2.png";
        if (clean.Contains("cuarto3") || clean.Contains("room3")) return "/uploads/00e6a74a-acea-4d68-b0fb-641cf8aa69a7_3.png";
        if (clean.Contains("cuarto4") || clean.Contains("room4")) return "/uploads/d9257417-3ce3-4f46-98ae-c311efab3e5c_4.png";
        if (clean.Contains("cuarto5") || clean.Contains("room5")) return "/uploads/664465c8-c058-4b43-9a69-bc178cc564ef_5.png";
        if (clean.Contains("cuarto6") || clean.Contains("room6")) return "/uploads/89abad46-98c0-45ca-a1eb-999007fc0bc6_6.png";
        if (clean.Contains("smt")) return "/uploads/smt_floor_plan.svg";
        if (clean.Contains("assy") || clean.Contains("ensamble")) return "/uploads/assembly_floor_plan.svg";
        return "/uploads/smt_floor_plan.svg";
    }

    private string GetAreaDisplayName(string areaId)
    {
        string clean = CleanString(areaId);
        if (clean.Contains("cuarto1")) return "Clean Room 1";
        if (clean.Contains("cuarto2")) return "Clean Room 2";
        if (clean.Contains("cuarto3")) return "Clean Room 3";
        if (clean.Contains("cuarto4")) return "Clean Room 4";
        if (clean.Contains("cuarto5")) return "Clean Room 5";
        if (clean.Contains("cuarto6")) return "Clean Room 6";
        if (clean.Contains("smt")) return "Línea 1 SMT (Montaje Superficial)";
        if (clean.Contains("assy") || clean.Contains("ensamble")) return "Área de Ensamble y Prueba de Tarjetas";
        return $"Área {areaId}";
    }

    private (double x, double y, double w, double h)? ExtractRectangleFromNotes(string notes)
    {
        if (string.IsNullOrEmpty(notes)) return null;

        var match = System.Text.RegularExpressions.Regex.Match(
            notes,
            (@"X[:=]\s*([0-9.]+)\s*%?,?\s*Y[:=]\s*([0-9.]+)\s*%?,?\s*(?:Ancho|W)[:=]\s*([0-9.]+)\s*%?,?\s*(?:Alto|H)[:=]\s*([0-9.]+)\s*%"),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        if (match.Success &&
            double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out double x) &&
            double.TryParse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture, out double y) &&
            double.TryParse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture, out double w) &&
            double.TryParse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture, out double h))
        {
            return (x, y, w, h);
        }

        return null;
    }

    public async Task<List<CleanedZone>> GetCleanedZonesFromSupabaseAsync(string areaId)
    {
        var result = new List<CleanedZone>();
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project")) return result;

        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/limpiezas_piso?select=*&order=fecha_limpieza.desc";
            var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);

            var response = await _httpClient.SendAsync(httpRequest);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        string cuarto = GetPropertyString(elem, "cuarto", "area_id") ?? "";
                        if (!string.IsNullOrEmpty(areaId) && CleanString(cuarto) != CleanString(areaId))
                        {
                            continue;
                        }

                        double? x = GetPropertyDouble(elem, "x_percent", "coord_x");
                        double? y = GetPropertyDouble(elem, "y_percent", "coord_y");
                        double? w = GetPropertyDouble(elem, "width_percent", "ancho");
                        double? h = GetPropertyDouble(elem, "height_percent", "alto");
                        string obs = GetPropertyString(elem, "observaciones", "notas") ?? "";

                        if ((!x.HasValue || !w.HasValue || w.Value <= 0) && !string.IsNullOrEmpty(obs))
                        {
                            var extracted = ExtractRectangleFromNotes(obs);
                            if (extracted.HasValue)
                            {
                                x = extracted.Value.x;
                                y = extracted.Value.y;
                                w = extracted.Value.w;
                                h = extracted.Value.h;
                            }
                        }

                        if (!x.HasValue || !y.HasValue || !w.HasValue || !h.HasValue || (w.Value <= 0 && h.Value <= 0))
                        {
                            continue;
                        }

                        var zone = new CleanedZone
                        {
                            Id = GetPropertyString(elem, "id") ?? Guid.NewGuid().ToString(),
                            AreaId = string.IsNullOrEmpty(cuarto) ? areaId : cuarto,
                            RequestId = GetPropertyString(elem, "request_id") ?? "",
                            XPercent = x.Value,
                            YPercent = y.Value,
                            WidthPercent = w.Value,
                            HeightPercent = h.Value,
                            CleanedBy = GetPropertyString(elem, "limpiado_por", "tecnico") ?? "Personal de Limpieza ESD",
                            Notes = obs
                        };

                        string? dateStr = GetPropertyString(elem, "fecha_limpieza");
                        if (!string.IsNullOrEmpty(dateStr) && DateTimeOffset.TryParse(dateStr, out var dto))
                            zone.CleanedDate = dto.UtcDateTime;

                        result.Add(zone);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase GetCleanedZones Exception] {ex.Message}");
        }

        return result;
    }

    public async Task<List<CleaningHistoryDto>> GetCleaningHistoryFromSupabaseAsync()
    {
        var result = new List<CleaningHistoryDto>();
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project")) return result;

        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/limpiezas_piso?select=*&order=fecha_limpieza.desc";
            var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);

            var response = await _httpClient.SendAsync(httpRequest);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        string areaId = GetPropertyString(elem, "cuarto", "area_id") ?? "Desconocido";
                        string reqId = GetPropertyString(elem, "request_id") ?? "";
                        
                        double? x = GetPropertyDouble(elem, "x_percent", "coord_x");
                        double? y = GetPropertyDouble(elem, "y_percent", "coord_y");
                        double? w = GetPropertyDouble(elem, "width_percent", "ancho");
                        double? h = GetPropertyDouble(elem, "height_percent", "alto");
                        string obs = GetPropertyString(elem, "observaciones", "notas") ?? "";

                        if ((!x.HasValue || !w.HasValue || w.Value <= 0) && !string.IsNullOrEmpty(obs))
                        {
                            var extracted = ExtractRectangleFromNotes(obs);
                            if (extracted.HasValue)
                            {
                                x = extracted.Value.x;
                                y = extracted.Value.y;
                                w = extracted.Value.w;
                                h = extracted.Value.h;
                            }
                        }

                        string punto = GetPropertyString(elem, "punto", "point_id") ?? "";

                        string section = "Toda el área";
                        if (x.HasValue && y.HasValue && w.HasValue && h.HasValue && (w.Value > 0 || h.Value > 0))
                        {
                            section = $"Recuadro (X: {x.Value}%, Y: {y.Value}%, Ancho: {w.Value}%, Alto: {h.Value}%)";
                        }
                        else if (obs.Contains("[Recuadro"))
                        {
                            int startIdx = obs.IndexOf("[Recuadro");
                            section = obs.Substring(startIdx).Trim('[', ']');
                        }
                        else if (!string.IsNullOrEmpty(punto) && punto != "ALL")
                        {
                            section = $"Punto de medición {punto}";
                        }

                        string reasonStr = !string.IsNullOrEmpty(reqId) 
                            ? $"Por Solicitud (Folio: {reqId})" 
                            : "Limpieza Programada / Directa";

                        var item = new CleaningHistoryDto
                        {
                            Id = GetPropertyString(elem, "id") ?? Guid.NewGuid().ToString(),
                            AreaId = areaId,
                            AreaName = GetPropertyString(elem, "area_name") ?? GetAreaDisplayName(areaId),
                            Reason = reasonStr,
                            CleanedBy = GetPropertyString(elem, "limpiado_por", "tecnico") ?? "Personal ESD",
                            Notes = GetPropertyString(elem, "observaciones", "notas") ?? "Sin observaciones",
                            MapSection = section,
                            XPercent = x ?? 0,
                            YPercent = y ?? 0,
                            WidthPercent = w ?? 0,
                            HeightPercent = h ?? 0
                        };

                        string? dateClean = GetPropertyString(elem, "fecha_limpieza");
                        if (!string.IsNullOrEmpty(dateClean) && DateTimeOffset.TryParse(dateClean, out var dtoClean))
                        {
                            item.CleanedDate = dtoClean.UtcDateTime;
                        }
                        else
                        {
                            item.CleanedDate = DateTime.UtcNow;
                        }

                        string? dateNext = GetPropertyString(elem, "fecha_proxima_limpieza");
                        if (!string.IsNullOrEmpty(dateNext) && DateTimeOffset.TryParse(dateNext, out var dtoNext))
                        {
                            item.NextCleaningDate = dtoNext.UtcDateTime;
                        }
                        else
                        {
                            item.NextCleaningDate = item.CleanedDate.AddDays(90);
                        }

                        result.Add(item);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase Fetch Cleaning History Error] {ex.Message}");
        }

        return result;
    }

    // ==========================================
    // SUPABASE USER ACCOUNTS PERSISTENCE METHODS
    // ==========================================
    public async Task<List<UserAccount>> GetUsersFromSupabaseAsync()
    {
        var users = new List<UserAccount>();
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project")) return users;

        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/usuarios?select=*";
            var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);

            var response = await _httpClient.SendAsync(httpRequest);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        var u = new UserAccount
                        {
                            Id = GetPropertyString(elem, "id") ?? Guid.NewGuid().ToString(),
                            Username = GetPropertyString(elem, "username", "usuario") ?? "",
                            PasswordHash = GetPropertyString(elem, "password_hash", "password") ?? "",
                            DisplayName = GetPropertyString(elem, "display_name", "nombre") ?? "",
                            Role = (GetPropertyString(elem, "role", "rol") ?? "TECHNICIAN").ToUpper(),
                            Department = GetPropertyString(elem, "department", "departamento") ?? "Mantenimiento ESD"
                        };

                        string? dateStr = GetPropertyString(elem, "created_at", "fecha_creacion");
                        if (!string.IsNullOrEmpty(dateStr) && DateTimeOffset.TryParse(dateStr, out var dtoDate))
                        {
                            u.CreatedAt = dtoDate.UtcDateTime;
                        }

                        if (!string.IsNullOrWhiteSpace(u.Username))
                        {
                            users.Add(u);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase GetUsers Error] {ex.Message}");
        }

        return users;
    }

    public async Task<UserAccount?> CreateUserInSupabaseAsync(UserAccount user)
    {
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project")) return user;

        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/usuarios";
            var payload = new Dictionary<string, object>
            {
                { "id", user.Id },
                { "username", user.Username },
                { "password_hash", user.PasswordHash },
                { "display_name", user.DisplayName },
                { "role", user.Role },
                { "department", user.Department },
                { "created_at", user.CreatedAt.ToString("o") }
            };

            string bodyJson = JsonSerializer.Serialize(payload);
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);
            httpRequest.Headers.Add("Prefer", "return=representation");
            httpRequest.Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Supabase User Created] Successfully saved '{user.Username}' to Supabase database.");
                return user;
            }
            else
            {
                string errText = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[Supabase User Create Error] {response.StatusCode}: {errText}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase CreateUser Exception] {ex.Message}");
        }

        return user;
    }

    public async Task<bool> DeleteUserFromSupabaseAsync(string username)
    {
        if (string.IsNullOrEmpty(_settings.Url) || _settings.Url.Contains("your-project")) return false;

        try
        {
            string endpoint = $"{_settings.Url.TrimEnd('/')}/rest/v1/usuarios?username=eq.{Uri.EscapeDataString(username)}";
            var httpRequest = new HttpRequestMessage(HttpMethod.Delete, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);

            var response = await _httpClient.SendAsync(httpRequest);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Supabase DeleteUser Exception] {ex.Message}");
            return false;
        }
    }
}
