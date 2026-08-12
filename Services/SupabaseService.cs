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
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("apikey", _settings.AnonKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AnonKey);
            httpRequest.Headers.Add("Prefer", "resolution=merge-duplicates");

            var payload = new
            {
                id = record.Id,
                cuarto = record.AreaId,
                area_name = record.AreaName,
                punto = record.PointId,
                fecha_limpieza = record.FechaLimpieza,
                fecha_proxima_limpieza = record.FechaProximaLimpieza,
                limpiado_por = record.LimpiadoPor,
                observaciones = record.Observaciones,
                request_id = record.RequestId
            };

            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.SendAsync(httpRequest);
            Console.WriteLine($"[Supabase limpiezas_piso Sync] ID '{record.Id}' -> Status {response.StatusCode}");
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
}
