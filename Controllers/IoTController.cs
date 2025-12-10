using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using AygazSmartEnergy.Data;
using AygazSmartEnergy.Models;
using AygazSmartEnergy.Hubs;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using AygazSmartEnergy.Services;
using AygazSmartEnergy.Configuration;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

// IoT verisini alır, kaydeder; SignalR, RabbitMQ ve ML akışını tetikler.
namespace AygazSmartEnergy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class IoTController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<IoTController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHubContext<EnergyHub> _hubContext;
        private readonly IMessageBus _messageBus;
        private readonly RabbitMqOptions _rabbitOptions;
        private readonly IAlertService _alertService;
        private readonly HttpClient _httpClient;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public IoTController(
            AppDbContext context,
            ILogger<IoTController> logger,
            IConfiguration configuration,
            IHubContext<EnergyHub> hubContext,
            IMessageBus messageBus,
            IOptions<RabbitMqOptions> rabbitOptions,
            IAlertService alertService,
            HttpClient httpClient,
            IServiceScopeFactory serviceScopeFactory)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
            _hubContext = hubContext;
            _messageBus = messageBus;
            _rabbitOptions = rabbitOptions.Value;
            _alertService = alertService;
            _httpClient = httpClient;
            _serviceScopeFactory = serviceScopeFactory;
        }

        [HttpPost("sensor-data")]
        public async Task<IActionResult> PostSensorData([FromBody] SensorDataRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var sensorData = new SensorData
                {
                    SensorName = request.SensorName,
                    SensorType = request.SensorType,
                    Temperature = request.Temperature,
                    GasLevel = request.GasLevel,
                    EnergyUsage = request.EnergyUsage,
                    Voltage = request.Voltage,
                    Current = request.Current,
                    PowerFactor = request.PowerFactor,
                    Location = request.Location,
                    Status = request.Status ?? "Active",
                    RawData = JsonSerializer.Serialize(request.RawData),
                    FirmwareVersion = request.FirmwareVersion,
                    SignalStrength = request.SignalStrength,
                    DeviceId = request.DeviceId,
                    RecordedAt = DateTime.UtcNow
                };

                _context.SensorDatas.Add(sensorData);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Sensor data received from {request.SensorName} at {sensorData.RecordedAt}");

                await _hubContext.NotifySensorDataUpdate(sensorData);

                EnergyConsumption? energyConsumption = null;
                if (request.DeviceId.HasValue)
                {
                    energyConsumption = await CreateEnergyConsumptionRecord(request, sensorData.Id);
                    
                    if (energyConsumption != null)
                    {
                        await _hubContext.NotifyEnergyConsumptionUpdate(energyConsumption);
                        try
                        {
                            _ = _messageBus.PublishAsync(
                                _rabbitOptions.SensorQueue ?? "sensor-data",
                                new
                                {
                                    deviceId = request.DeviceId.Value,
                                    sensorName = request.SensorName,
                                    temperature = request.Temperature,
                                    gasLevel = request.GasLevel,
                                    voltage = request.Voltage,
                                    current = request.Current,
                                    energyUsed = energyConsumption.EnergyUsed,
                                    powerConsumption = energyConsumption.PowerConsumption,
                                    powerFactor = request.PowerFactor,
                                    recordedAt = energyConsumption.RecordedAt
                                });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "RabbitMQ gönderimi başarısız, HTTP fallback devrede");
                        }
                    }
                }
                
                if (request.DeviceId.HasValue && energyConsumption != null)
                {
                    // ML servisi ile anomali kontrolü (asenkron, fire-and-forget)
                    // Yeni scope oluşturarak DbContext thread safety sorununu önle
                    _ = Task.Run(async () =>
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var scopedContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var scopedAlertService = scope.ServiceProvider.GetRequiredService<IAlertService>();
                        var scopedHubContext = scope.ServiceProvider.GetRequiredService<IHubContext<EnergyHub>>();
                        
                        try
                        {
                            await CheckAnomaliesAndCreateAlertsAsyncScoped(request, energyConsumption, scopedContext, scopedAlertService, scopedHubContext);
                            await scopedAlertService.CheckAndCreateAlertsAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in background alert checking for DeviceId {DeviceId}", request.DeviceId);
                        }
                    });
                }
                else
                {
                    var defaultUser = await _context.Users.FirstOrDefaultAsync();
                    if (defaultUser != null)
                    {
                        var fakeDevice = new Device { Id = 0, DeviceName = request.SensorName ?? "Bilinmeyen Cihaz", UserId = defaultUser.Id };
                        var fakeEnergyConsumption = new EnergyConsumption 
                        { 
                            EnergyUsed = CalculateEnergyUsed(request.EnergyUsage, request.Voltage, request.Current),
                            PowerConsumption = request.EnergyUsage
                        };
                        
                        await PerformSimpleAnomalyChecksWithoutDevice(request, fakeEnergyConsumption, fakeDevice, new HashSet<string>());
                    }
                }

                return Ok(new { 
                    success = true,
                    message = "Sensor data received successfully",
                    id = sensorData.Id,
                    timestamp = sensorData.RecordedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing sensor data");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// PUT /api/IoT/device-status/{deviceId}
        /// Cihaz durumunu günceller (aktif/pasif, bakım tarihi)
        /// </summary>
        [HttpPut("device-status/{deviceId}")]  // HTTP PUT isteği: /api/IoT/device-status/1
        public async Task<IActionResult> UpdateDeviceStatus(int deviceId, [FromBody] DeviceStatusRequest request)
        {
            try
            {
                // 🔹 Cihaz Bulma: Veritabanından cihazı ID ile bul
                var device = await _context.Devices.FindAsync(deviceId);  // Primary key ile hızlı arama
                if (device == null)
                {
                    return NotFound(new { success = false, message = "Device not found" });  // Cihaz bulunamadı: 404
                }

                // 🔹 Cihaz Durumu Güncelleme: İstekten gelen yeni değerleri ata
                device.IsActive = request.IsActive;                    // Cihaz aktif/pasif durumu
                device.LastMaintenanceAt = request.LastMaintenanceAt;  // Son bakım tarihi

                // 🔹 Veritabanına Kaydetme: Değişiklikleri veritabanına yaz
                await _context.SaveChangesAsync();  // UPDATE SQL komutu çalıştır

                return Ok(new { success = true, message = "Device status updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating device status");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// GET /api/IoT/sensor-data/latest
        /// Son sensör verilerini getirir (dashboard için)
        /// Query Parametreleri:
        /// - deviceId: Belirli bir cihazın verilerini getir (opsiyonel)
        /// - count: Kaç kayıt getirilecek (varsayılan: 10)
        /// </summary>
        [HttpGet("sensor-data/latest")]  // HTTP GET isteği: /api/IoT/sensor-data/latest?deviceId=1&count=20
        public async Task<IActionResult> GetLatestSensorData([FromQuery] int? deviceId = null, [FromQuery] int count = 10)
        {
            try
            {
                // 🔹 Query Oluşturma: Veritabanı sorgusu başlat (AsNoTracking = sadece okuma, değişiklik takibi yok)
                IQueryable<SensorData> query = _context.SensorDatas
                    .AsNoTracking();  // Döngüsel referansı önlemek ve performans için tracking'i kapat

                // 🔹 Filtreleme: Belirli bir cihazın verilerini getir (opsiyonel)
                if (deviceId.HasValue)
                {
                    query = query.Where(s => s.DeviceId == deviceId);  // WHERE DeviceId = @deviceId
                }

                // 🔹 Veri Çekme: En son kayıtlardan belirtilen sayıda getir
                var sensorData = await query
                    .OrderByDescending(s => s.RecordedAt)  // En yeni kayıtlar önce (ORDER BY RecordedAt DESC)
                    .Take(count)                            // Belirtilen sayıda kayıt al (TOP count)
                    .Select(s => new                        // Sadece gerekli alanları seç (performans için)
                    {
                        s.Id,
                        s.SensorName,
                        s.SensorType,
                        s.Temperature,
                        s.GasLevel,
                        s.EnergyUsage,
                        s.Voltage,
                        s.Current,
                        s.PowerFactor,
                        s.Location,
                        s.Status,
                        s.RecordedAt,
                        s.DeviceId,
                        s.FirmwareVersion,
                        s.SignalStrength
                    })
                    .ToListAsync();  // SQL sorgusunu çalıştır ve sonuçları listeye al

                return Ok(new { success = true, data = sensorData });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sensor data");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// GET /api/IoT/devices
        /// Cihaz listesini getirir (dashboard cihaz sayfası için)
        /// Query Parametresi:
        /// - userId: Belirli bir kullanıcının cihazlarını getir (opsiyonel)
        /// </summary>
        [HttpGet("devices")]  // HTTP GET isteği: /api/IoT/devices?userId=123
        public async Task<IActionResult> GetDevices([FromQuery] string? userId = null)
        {
            try
            {
                // 🔹 Query Oluşturma: Tüm cihazları getir, User bilgisini de dahil et (JOIN)
                var query = _context.Devices.Include(d => d.User).AsQueryable();  // LEFT JOIN Users

                // 🔹 Kullanıcı Filtresi: Belirli kullanıcının cihazlarını getir (opsiyonel)
                if (!string.IsNullOrEmpty(userId))
                {
                    query = query.Where(d => d.UserId == userId);  // WHERE UserId = @userId
                }

                // 🔹 Veri Çekme: SQL sorgusunu çalıştır
                var devices = await query.ToListAsync();  // SELECT * FROM Devices [WHERE UserId = ...]

                return Ok(new { success = true, data = devices });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving devices");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// POST /api/IoT/devices/{deviceId}/activate
        /// Cihazı aktif hale getirir (cihaz veri göndermeye başlar)
        /// </summary>
        [HttpPost("devices/{deviceId}/activate")]  // HTTP POST isteği: /api/IoT/devices/1/activate
        public async Task<IActionResult> ActivateDevice(int deviceId)
        {
            try
            {
                // 🔹 Cihaz Bulma: Veritabanından cihazı bul
                var device = await _context.Devices.FindAsync(deviceId);
                if (device == null)
                {
                    return NotFound(new { success = false, message = "Cihaz bulunamadı" });
                }

                // 🔹 Cihazı Aktif Et: IsActive = true yap
                device.IsActive = true;
                await _context.SaveChangesAsync();  // UPDATE Devices SET IsActive = 1 WHERE Id = @deviceId

                // 🔹 SignalR Broadcast: Dashboard'a cihaz durumu değişikliğini bildir
                await _hubContext.NotifyDeviceStatusChanged(device);  // Dashboard'da cihaz durumu güncellenir

                _logger.LogInformation($"Device {deviceId} activated");

                return Ok(new { success = true, message = "Cihaz aktif edildi" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error activating device {DeviceId}", deviceId);
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// POST /api/IoT/devices/{deviceId}/deactivate
        /// Cihazı pasif hale getirir (cihaz veri göndermeyi durdurur)
        /// </summary>
        [HttpPost("devices/{deviceId}/deactivate")]  // HTTP POST isteği: /api/IoT/devices/1/deactivate
        public async Task<IActionResult> DeactivateDevice(int deviceId)
        {
            try
            {
                // 🔹 Cihaz Bulma: Veritabanından cihazı bul
                var device = await _context.Devices.FindAsync(deviceId);
                if (device == null)
                {
                    return NotFound(new { success = false, message = "Cihaz bulunamadı" });
                }

                // 🔹 Cihazı Pasif Et: IsActive = false yap
                device.IsActive = false;
                await _context.SaveChangesAsync();  // UPDATE Devices SET IsActive = 0 WHERE Id = @deviceId

                // 🔹 SignalR Broadcast: Dashboard'a cihaz durumu değişikliğini bildir
                await _hubContext.NotifyDeviceStatusChanged(device);  // Dashboard'da cihaz durumu güncellenir

                _logger.LogInformation($"Device {deviceId} deactivated");

                return Ok(new { success = true, message = "Cihaz pasif edildi" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating device {DeviceId}", deviceId);
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// POST /api/IoT/devices
        /// Yeni cihaz oluşturur (dashboard'dan veya API'den)
        /// Request Body: CreateDeviceRequest (DeviceName zorunlu, diğerleri opsiyonel)
        /// </summary>
        [HttpPost("devices")]  // HTTP POST isteği: /api/IoT/devices
        public async Task<IActionResult> CreateDevice([FromBody] CreateDeviceRequest request)
        {
            try
            {
                // 🔹 Model Validation: Gelen verinin geçerli olup olmadığını kontrol et
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // 🔹 Kullanıcı Bulma: İlk kullanıcıyı al (basitleştirilmiş - production'da auth'dan alınmalı)
                var user = await _context.Users.FirstOrDefaultAsync();  // SELECT TOP 1 * FROM Users
                if (user == null)
                {
                    return BadRequest(new { success = false, message = "Kullanıcı bulunamadı. Lütfen önce veritabanını seed edin." });
                }

                // 🔹 Cihaz Oluşturma: Request'ten gelen verileri Device entity'sine dönüştür
                var device = new Device
                {
                    DeviceName = request.DeviceName,                           // Cihaz adı (zorunlu)
                    DeviceType = request.DeviceType ?? "Other",               // Cihaz tipi (varsayılan: "Other")
                    Location = request.Location ?? "Belirtilmedi",            // Konum (varsayılan: "Belirtilmedi")
                    Description = request.Description,                        // Açıklama (opsiyonel)
                    SerialNumber = request.SerialNumber,                      // Seri numarası (opsiyonel)
                    Model = request.Model,                                    // Model (opsiyonel)
                    Manufacturer = request.Manufacturer,                      // Üretici (opsiyonel)
                    MaxPowerConsumption = request.MaxPowerConsumption,        // Maksimum güç tüketimi (W)
                    MinPowerConsumption = request.MinPowerConsumption,        // Minimum güç tüketimi (W)
                    IsActive = request.IsActive ?? true,                      // Aktif durumu (varsayılan: true)
                    InstalledAt = DateTime.UtcNow,                             // Kurulum tarihi (şu an, UTC)
                    UserId = user.Id                                         // Kullanıcı ID'si (ilk kullanıcı)
                };

                // 🔹 Veritabanına Kaydetme: Yeni cihazı Devices tablosuna ekle
                _context.Devices.Add(device);                  // EF Core Change Tracker'a ekle
                await _context.SaveChangesAsync();             // INSERT INTO Devices (...) VALUES (...)

                _logger.LogInformation($"Device created: {device.DeviceName} (ID: {device.Id})");

                return Ok(new { 
                    success = true, 
                    message = "Device created successfully",
                    data = new {
                        id = device.Id,                    // Oluşturulan cihazın ID'si
                        deviceName = device.DeviceName,     // Cihaz adı
                        deviceType = device.DeviceType,     // Cihaz tipi
                        location = device.Location          // Konum
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating device");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// Enerji Tüketimi Kaydı Oluşturma: SensorData'dan EnergyConsumption kaydı oluşturur
        /// </summary>
        private async Task<EnergyConsumption?> CreateEnergyConsumptionRecord(SensorDataRequest request, int sensorDataId)
        {
            try
            {
                // 🔹 Enerji Tüketimi Kaydı: SensorData'dan enerji tüketimi kaydı oluştur
                var energyConsumption = new EnergyConsumption
                {
                    DeviceId = request.DeviceId!.Value,     // Cihaz ID'si (null olamaz - zaten kontrol edildi)
                    PowerConsumption = request.EnergyUsage, // Güç tüketimi (W - Watt)
                    EnergyUsed = CalculateEnergyUsed(request.EnergyUsage, request.Voltage, request.Current), // Enerji tüketimi (kWh - kilowatt-saat)
                    Voltage = request.Voltage,              // Voltaj (V)
                    Current = request.Current,              // Akım (A)
                    PowerFactor = request.PowerFactor,      // Güç faktörü (0.0-1.0)
                    Temperature = request.Temperature,      // Sıcaklık (°C)
                    GasLevel = request.GasLevel,            // Gaz seviyesi (%)
                    RecordedAt = DateTime.UtcNow,           // Kayıt zamanı (UTC)
                    WeatherCondition = request.WeatherCondition  // Hava durumu (opsiyonel)
                };

                // 🔹 Veritabanına Kaydetme: EnergyConsumptions tablosuna ekle
                _context.EnergyConsumptions.Add(energyConsumption);  // EF Core Change Tracker'a ekle
                await _context.SaveChangesAsync();                   // INSERT INTO EnergyConsumptions (...) VALUES (...)
                return energyConsumption;                            // Oluşturulan kaydı döndür
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating energy consumption record");
                return null;  // Hata durumunda null döndür
            }
        }

        /// <summary>
        /// Enerji Hesaplama: Güç tüketiminden (W) enerji tüketimini (kWh) hesaplar
        /// Formül: Power (W) × Time (hour) / 1000 = Energy (kWh)
        /// Not: Basitleştirilmiş hesaplama - gerçek uygulamada zaman aralığı dikkate alınmalı
        /// </summary>
        private double CalculateEnergyUsed(double powerConsumption, double voltage, double current)
        {
            // Basit hesaplama: Power (W) * Time (1 hour) / 1000 = kWh
            // Gerçek uygulamada zaman aralığı dikkate alınmalı (örn: son okumadan bu yana geçen süre)
            return (powerConsumption * 1.0) / 1000.0;  // 1 saatlik tüketim (varsayılan)
        }

        /// <summary>
        /// ML Servisi Anomali Kontrolü: Python ML servisine HTTP ile anomali kontrolü yapar ve alert oluşturur
        /// Bu metod RabbitMQ kullanılmadığında (Docker olmadan test için) fallback olarak çalışır
        /// İşlem Akışı:
        /// 1. Python ML servisine HTTP POST isteği gönder (/detect-anomalies)
        /// 2. ML servisi anomali tespiti yapar ve sonuç döndürür
        /// 3. Anomali varsa Alert oluşturulur ve SignalR ile bildirilir
        /// 4. ML servisi çalışmıyorsa basit anomali kontrolleri yapılır
        /// </summary>
        private async Task CheckAnomaliesAndCreateAlertsAsyncScoped(
            SensorDataRequest request, 
            EnergyConsumption energyConsumption,
            AppDbContext context,
            IAlertService alertService,
            IHubContext<EnergyHub> hubContext)
        {
            try
            {
                // DbContext bağlantı sorunlarını önlemek için FirstOrDefaultAsync kullan (FindAsync yerine)
                Device? device = null;
                try
                {
                    device = await context.Devices
                        .AsNoTracking()  // Read-only sorgu, daha performanslı
                        .FirstOrDefaultAsync(d => d.Id == request.DeviceId!.Value);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("connection is closed") || ex.Message.Contains("disposed"))
                {
                    _logger.LogWarning(ex, "Database connection issue, retrying device query for DeviceId {DeviceId}", request.DeviceId);
                    // Retry: Yeni bir sorgu dene
                    try
                    {
                        await Task.Delay(100); // Kısa bir bekleme
                        device = await context.Devices
                            .AsNoTracking()
                            .FirstOrDefaultAsync(d => d.Id == request.DeviceId!.Value);
                    }
                    catch (Exception retryEx)
                    {
                        _logger.LogError(retryEx, "Failed to retrieve device after retry for DeviceId {DeviceId}", request.DeviceId);
                        return; // Device bulunamazsa anomali kontrolü yapılamaz
                    }
                }
                
                if (device == null) return;

                // ML servisi URL'i (Docker olmadan port 5002)
                var mlServiceUrl = _configuration["PythonMLService:BaseUrl"] ?? "http://localhost:5002";
                
                // Anomali kontrolü için veri hazırla
                var anomalyCheckData = new
                {
                    DeviceId = request.DeviceId.Value,
                    Data = new[]
                    {
                        new
                        {
                            Date = DateTime.UtcNow,
                            EnergyConsumption = energyConsumption.EnergyUsed,
                            PowerConsumption = energyConsumption.PowerConsumption,
                            Temperature = request.Temperature,
                            Voltage = request.Voltage,
                            Current = request.Current,
                            PowerFactor = request.PowerFactor
                        }
                    }
                };

                var jsonContent = JsonSerializer.Serialize(anomalyCheckData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // ML servisine anomali kontrolü isteği gönder (10 saniye timeout ile)
                bool mlCheckSucceeded = false;
                var mlDetectedAnomalyTypes = new HashSet<string>(); // ML servisinin tespit ettiği anomali tipleri
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var response = await _httpClient.PostAsync($"{mlServiceUrl}/detect-anomalies", content, cts.Token);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        mlCheckSucceeded = true;
                        var responseContent = await response.Content.ReadAsStringAsync();
                        _logger.LogDebug($"ML servisi yanıtı alındı: {responseContent}");
                        
                        // JSON deserializasyonu - case-insensitive property matching için JsonElement kullan
                        var anomalies = JsonSerializer.Deserialize<List<JsonElement>>(responseContent);
                        
                        if (anomalies != null && anomalies.Count > 0)
                        {
                            _logger.LogInformation($"ML servisi {anomalies.Count} anomali tespit etti. DeviceId: {device.Id}");
                            
                            // Anomali bulundu - Alert oluştur
                            foreach (var anomalyObj in anomalies)
                            {
                                try
                                {
                                    // ML servisi "AnomalyType" (büyük A) döndürüyor, case-insensitive kontrol
                                    string? anomalyType = null;
                                    if (anomalyObj.TryGetProperty("AnomalyType", out JsonElement typeProp))
                                    {
                                        anomalyType = typeProp.GetString();
                                    }
                                    else if (anomalyObj.TryGetProperty("anomalyType", out JsonElement typePropLower))
                                    {
                                        anomalyType = typePropLower.GetString();
                                    }
                                    anomalyType ??= "Unknown";
                                    
                                    // ML servisinin tespit ettiği anomali tipini kaydet (duplicate önleme için)
                                    mlDetectedAnomalyTypes.Add(anomalyType);
                                    
                                    // Severity - ML servisi 0-1 arası skor döndürüyor
                                    string severity = "Medium";
                                    if (anomalyObj.TryGetProperty("Severity", out JsonElement sevProp))
                                    {
                                        var severityScore = sevProp.GetDouble();
                                        severity = severityScore > 0.8 ? "Critical" 
                                                  : severityScore > 0.6 ? "High" 
                                                  : severityScore > 0.4 ? "Medium" 
                                                  : "Low";
                                    }
                                    else if (anomalyObj.TryGetProperty("severity", out JsonElement sevPropLower))
                                    {
                                        var severityScore = sevPropLower.GetDouble();
                                        severity = severityScore > 0.8 ? "Critical" 
                                                  : severityScore > 0.6 ? "High" 
                                                  : severityScore > 0.4 ? "Medium" 
                                                  : "Low";
                                    }
                                    
                                    // Description
                                    string description = "Anomali tespit edildi";
                                    if (anomalyObj.TryGetProperty("Description", out JsonElement descProp))
                                    {
                                        description = descProp.GetString() ?? description;
                                    }
                                    else if (anomalyObj.TryGetProperty("description", out JsonElement descPropLower))
                                    {
                                        description = descPropLower.GetString() ?? description;
                                    }
                                    
                                    // Alert oluştur
                                    var anomalyJson = anomalyObj.GetRawText();
                                    _logger.LogInformation($"ML Anomali Alert oluşturuluyor: Type={anomalyType}, Severity={severity}, DeviceId={device.Id}");
                                    
                                    await alertService.CreateAlertAsync(
                                        device.UserId,
                                        $"ML Anomali Tespit Edildi: {anomalyType}",
                                        $"{device.DeviceName} cihazında {description}",
                                        anomalyType,
                                        severity,
                                        device.Id,
                                        anomalyJson
                                    );
                                    
                                    _logger.LogInformation($"✓ ML Anomali Alert başarıyla oluşturuldu: Type={anomalyType}, DeviceId={device.Id}");
                                }
                                catch (Exception alertEx)
                                {
                                    _logger.LogError(alertEx, $"ML anomali alert'i oluşturulurken hata oluştu. DeviceId: {device.Id}, Anomaly: {anomalyObj.GetRawText()}");
                                }
                            }
                        }
                        else
                        {
                            _logger.LogDebug($"ML servisi anomali tespit etmedi (normal veri). DeviceId: {device.Id}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"ML servisi yanıt hatası: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    }
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning("ML servisi anomali kontrolü timeout oldu, basit kontroller yapılacak");
                }
                catch (Exception mlEx)
                {
                    _logger.LogWarning(mlEx, "ML servisi anomali kontrolü başarısız oldu, basit kontroller yapılacak");
                }
                
                // Basit anomali kontrolleri (ML servisi çalışmıyorsa veya anomali tespit etmediyse)
                // NOT: ML servisi anomali tespit ettiyse, aynı tip anomali için basit kontrolleri atla (duplicate önleme)
                try
                {
                    await PerformSimpleAnomalyChecksScoped(request, energyConsumption, device, context, alertService, mlDetectedAnomalyTypes);
                }
                catch (Exception simpleCheckEx)
                {
                    _logger.LogError(simpleCheckEx, "Basit anomali kontrolleri başarısız oldu. DeviceId: {DeviceId}", request.DeviceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ML servisi anomali kontrolü yapılamadı, basit kontroller yapılıyor");
                // ML servisi çalışmıyorsa basit kontroller yap
                if (request.DeviceId.HasValue && energyConsumption != null)
                {
                    Device? fallbackDevice = null;
                    try
                    {
                        // Connection sorunlarını önlemek için AsNoTracking ve FirstOrDefaultAsync kullan
                        fallbackDevice = await context.Devices
                            .AsNoTracking()
                            .FirstOrDefaultAsync(d => d.Id == request.DeviceId.Value);
                    }
                    catch (Exception dbEx)
                    {
                        _logger.LogError(dbEx, "Device sorgusu başarısız oldu, basit kontroller atlanıyor. DeviceId: {DeviceId}", request.DeviceId);
                        return; // Device bulunamazsa basit kontroller yapılamaz
                    }
                    
                    if (fallbackDevice != null)
                    {
                        try
                        {
                            // ML servisi çalışmadığı için hiçbir anomali tipi tespit edilmedi
                            await PerformSimpleAnomalyChecksScoped(request, energyConsumption, fallbackDevice, context, alertService, new HashSet<string>());
                        }
                        catch (Exception checkEx)
                        {
                            _logger.LogError(checkEx, "Basit anomali kontrolleri başarısız oldu. DeviceId: {DeviceId}", request.DeviceId);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Basit Anomali Kontrolleri: ML servisi olmadığında basit eşik değer kontrolleri yapar
        /// Kontrol Edilen Durumlar:
        /// 1. Yüksek Enerji Tüketimi (>300 kWh)
        /// 2. Yüksek Sıcaklık (>40°C, kritik: >50°C)
        /// 3. Voltaj Anomalisi (<200V veya >250V, kritik: <180V veya >260V)
        /// 4. Düşük Güç Faktörü (<0.7, kritik: <0.5)
        /// Her durum için uygun severity (Low/Medium/High/Critical) ile Alert oluşturulur
        /// NOT: Aynı cihaz için son 1 saatte aynı tip alert oluşturulmuşsa, yeniden oluşturmaz (duplicate önleme)
        /// NOT: ML servisi zaten anomali tespit ettiyse, aynı tip anomali için alert oluşturma (duplicate önleme)
        /// </summary>
        private async Task PerformSimpleAnomalyChecksScoped(
            SensorDataRequest request, 
            EnergyConsumption energyConsumption, 
            Device device,
            AppDbContext context,
            IAlertService alertService,
            HashSet<string> mlDetectedAnomalyTypes)
        {
            _logger.LogDebug($"Basit anomali kontrolleri başlatıldı. DeviceId: {device.Id}, Temperature: {request.Temperature}, Voltage: {request.Voltage}, EnergyUsed: {energyConsumption.EnergyUsed}, PowerFactor: {request.PowerFactor}");
            
            // 🔹 Duplicate Kontrolü: Son 5 dakika içinde aynı tip alert var mı kontrol et
            // 5 saniyede bir veri geldiği için duplicate alert'leri önlemek için 5 dakika kullanıyoruz
            var oneHourAgo = DateTime.UtcNow.AddMinutes(-5);

            // 🔹 Yüksek Tüketim Kontrolü: Enerji tüketimi 300 kWh'yi aştı mı?
            if (energyConsumption.EnergyUsed > 300)
            {
                // Son 1 saatte aynı cihaz için yüksek tüketim alert'i var mı kontrol et
                var existingAlert = await context.Alerts
                    .FirstOrDefaultAsync(a => a.DeviceId == device.Id &&
                                               a.AlertType == "HighConsumption" &&
                                               !a.IsResolved &&
                                               a.CreatedAt >= oneHourAgo);

                // Alert yoksa yeni alert oluştur
                if (existingAlert == null)
                {
                    _logger.LogInformation($"Yüksek enerji tüketimi tespit edildi! DeviceId: {device.Id}, EnergyUsed: {energyConsumption.EnergyUsed:F2} kWh (Eşik: 300 kWh)");
                    await alertService.CreateAlertAsync(
                        device.UserId,
                        "Yüksek Enerji Tüketimi",
                        $"{device.DeviceName} cihazında yüksek enerji tüketimi tespit edildi: {energyConsumption.EnergyUsed:F2} kWh",
                        "HighConsumption",
                        "High",
                        device.Id,
                        JsonSerializer.Serialize(new { EnergyUsed = energyConsumption.EnergyUsed, Threshold = 300 })
                    );
                    _logger.LogInformation($"Yüksek enerji tüketimi alert'i oluşturuldu. DeviceId: {device.Id}");
                }
                else
                {
                    _logger.LogInformation($"Yüksek tüketim alert'i zaten mevcut (AlertId: {existingAlert.Id}), yeni alert oluşturulmadı");
                }
            }

            // 🔹 Yüksek Sıcaklık Kontrolü: Sıcaklık 40°C'yi aştı mı?
            // ML servisi zaten "TemperatureAnomaly" tespit ettiyse atla
            if (request.Temperature > 40 && !mlDetectedAnomalyTypes.Contains("TemperatureAnomaly"))
            {
                // Son 1 saatte aynı cihaz için sıcaklık alert'i var mı kontrol et
                var existingAlert = await context.Alerts
                    .FirstOrDefaultAsync(a => a.DeviceId == device.Id &&
                                               a.AlertType == "TemperatureAnomaly" &&
                                               !a.IsResolved &&
                                               a.CreatedAt >= oneHourAgo);

                // Alert yoksa yeni alert oluştur
                if (existingAlert == null)
                {
                    _logger.LogInformation($"Yüksek sıcaklık tespit edildi! DeviceId: {device.Id}, Temperature: {request.Temperature:F2}°C (Eşik: 40°C)");
                    await alertService.CreateAlertAsync(
                        device.UserId,
                        "Yüksek Sıcaklık",
                        $"{device.DeviceName} cihazında yüksek sıcaklık tespit edildi: {request.Temperature:F2}°C",
                        "TemperatureAnomaly",
                        request.Temperature > 50 ? "Critical" : "High",
                        device.Id,
                        JsonSerializer.Serialize(new { Temperature = request.Temperature, Threshold = 40 })
                    );
                    _logger.LogInformation($"Yüksek sıcaklık alert'i oluşturuldu. DeviceId: {device.Id}");
                }
                else
                {
                    _logger.LogInformation($"Yüksek sıcaklık alert'i zaten mevcut (AlertId: {existingAlert.Id}), yeni alert oluşturulmadı");
                }
            }

            // 🔹 Voltaj Anomali Kontrolü: Voltaj normal aralığın dışında mı? (200V-250V arası normal)
            // NOT: 0 değeri geçersiz veri, alert oluşturma (cihaz kapalı veya sensör hatası)
            // ML servisi zaten "VoltageAnomaly" tespit ettiyse atla
            if (request.Voltage > 0 && (request.Voltage < 200 || request.Voltage > 250) && !mlDetectedAnomalyTypes.Contains("VoltageAnomaly"))
            {
                // Son 1 saatte aynı cihaz için voltaj alert'i var mı kontrol et
                var existingAlert = await context.Alerts
                    .FirstOrDefaultAsync(a => a.DeviceId == device.Id &&
                                               a.AlertType == "VoltageAnomaly" &&
                                               !a.IsResolved &&
                                               a.CreatedAt >= oneHourAgo);

                // Alert yoksa yeni alert oluştur
                if (existingAlert == null)
                {
                    _logger.LogInformation($"Voltaj anomalisi tespit edildi! DeviceId: {device.Id}, Voltage: {request.Voltage:F2}V (Normal: 200-250V)");
                    await alertService.CreateAlertAsync(
                        device.UserId,
                        "Voltaj Anomalisi",
                        $"{device.DeviceName} cihazında voltaj anomalisi tespit edildi: {request.Voltage:F2}V (Normal: 220V)",
                        "VoltageAnomaly",
                        request.Voltage < 180 || request.Voltage > 260 ? "Critical" : "Medium",
                        device.Id,
                        JsonSerializer.Serialize(new { Voltage = request.Voltage, NormalMin = 200, NormalMax = 250 })
                    );
                    _logger.LogInformation($"Voltaj anomalisi alert'i oluşturuldu. DeviceId: {device.Id}");
                }
                else
                {
                    _logger.LogInformation($"Voltaj anomalisi alert'i zaten mevcut (AlertId: {existingAlert.Id}), yeni alert oluşturulmadı");
                }
            }
            else if (request.Voltage == 0)
            {
                _logger.LogWarning($"Geçersiz voltaj değeri (0V) alındı, alert oluşturulmadı. DeviceId: {device.Id}");
            }

            // 🔹 Düşük Güç Faktörü Kontrolü: Güç faktörü 0.7'den düşük mü? (Normal: >0.8)
            // NOT: 0 değeri geçersiz veri, alert oluşturma (cihaz kapalı veya sensör hatası)
            // ML servisi zaten "LowPowerFactor" tespit ettiyse atla
            if (request.PowerFactor > 0 && request.PowerFactor < 0.7 && !mlDetectedAnomalyTypes.Contains("LowPowerFactor"))
            {
                // Son 1 saatte aynı cihaz için güç faktörü alert'i var mı kontrol et
                var existingAlert = await context.Alerts
                    .FirstOrDefaultAsync(a => a.DeviceId == device.Id &&
                                               a.AlertType == "LowPowerFactor" &&
                                               !a.IsResolved &&
                                               a.CreatedAt >= oneHourAgo);

                // Alert yoksa yeni alert oluştur
                if (existingAlert == null)
                {
                    _logger.LogInformation($"Düşük güç faktörü tespit edildi! DeviceId: {device.Id}, PowerFactor: {request.PowerFactor:F2} (Eşik: 0.7)");
                    await alertService.CreateAlertAsync(
                        device.UserId,
                        "Düşük Güç Faktörü",
                        $"{device.DeviceName} cihazında düşük güç faktörü tespit edildi: {request.PowerFactor:F2} (Normal: >0.8)",
                        "LowPowerFactor",
                        request.PowerFactor < 0.5 ? "High" : "Medium",
                        device.Id,
                        JsonSerializer.Serialize(new { PowerFactor = request.PowerFactor, Threshold = 0.7 })
                    );
                    _logger.LogInformation($"Düşük güç faktörü alert'i oluşturuldu. DeviceId: {device.Id}");
                }
                else
                {
                    _logger.LogInformation($"Düşük güç faktörü alert'i zaten mevcut (AlertId: {existingAlert.Id}), yeni alert oluşturulmadı");
                }
            }
            else if (request.PowerFactor == 0)
            {
                _logger.LogWarning($"Geçersiz güç faktörü değeri (0) alındı, alert oluşturulmadı. DeviceId: {device.Id}");
            }
        }

        /// <summary>
        /// DeviceId Olmadan Basit Anomali Kontrolleri: DeviceId olmadan gelen veriler için anomali kontrolü yapar
        /// Bu metod DeviceId olmadan gelen sensör verileri için kullanılır
        /// </summary>
        private async Task PerformSimpleAnomalyChecksWithoutDevice(SensorDataRequest request, EnergyConsumption energyConsumption, Device device, HashSet<string> mlDetectedAnomalyTypes)
        {
            // 🔹 Duplicate Kontrolü: Son kısa süre içinde (varsayılan: 1 dakika) aynı sensör için aynı tip alert var mı kontrol et
            // Demo ve test sırasında daha fazla uyarı görebilmek için süre 1 saatten 1 dakikaya düşürüldü.
            var oneHourAgo = DateTime.UtcNow.AddMinutes(-1);

            // 🔹 Yüksek Tüketim Kontrolü: Enerji tüketimi 300 kWh'yi aştı mı?
            // ML servisi zaten "HighConsumption" tespit ettiyse atla
            if (energyConsumption.EnergyUsed > 300 && !mlDetectedAnomalyTypes.Contains("HighConsumption"))
            {
                var existingAlert = await _context.Alerts
                    .FirstOrDefaultAsync(a => a.AlertType == "HighConsumption" &&
                                               !a.IsResolved &&
                                               a.Title.Contains(device.DeviceName) &&
                                               a.CreatedAt >= oneHourAgo);

                if (existingAlert == null)
                {
                    await _alertService.CreateAlertAsync(
                        device.UserId,
                        $"Yüksek Enerji Tüketimi - {device.DeviceName}",
                        $"{device.DeviceName} cihazında yüksek enerji tüketimi tespit edildi: {energyConsumption.EnergyUsed:F2} kWh",
                        "HighConsumption",
                        "High",
                        null, // DeviceId yok
                        JsonSerializer.Serialize(new { EnergyUsed = energyConsumption.EnergyUsed, Threshold = 300, SensorName = device.DeviceName })
                    );
                }
            }

            // 🔹 Yüksek Sıcaklık Kontrolü
            // ML servisi zaten "TemperatureAnomaly" tespit ettiyse atla
            if (request.Temperature > 40 && !mlDetectedAnomalyTypes.Contains("TemperatureAnomaly"))
            {
                var existingAlert = await _context.Alerts
                    .FirstOrDefaultAsync(a => a.AlertType == "TemperatureAnomaly" &&
                                               !a.IsResolved &&
                                               a.Title.Contains(device.DeviceName) &&
                                               a.CreatedAt >= oneHourAgo);

                if (existingAlert == null)
                {
                    await _alertService.CreateAlertAsync(
                        device.UserId,
                        $"Yüksek Sıcaklık - {device.DeviceName}",
                        $"{device.DeviceName} cihazında yüksek sıcaklık tespit edildi: {request.Temperature:F2}°C",
                        "TemperatureAnomaly",
                        request.Temperature > 50 ? "Critical" : "High",
                        null, // DeviceId yok
                        JsonSerializer.Serialize(new { Temperature = request.Temperature, Threshold = 40, SensorName = device.DeviceName })
                    );
                }
            }

            // 🔹 Voltaj Anomali Kontrolü
            // ML servisi zaten "VoltageAnomaly" tespit ettiyse atla
            if ((request.Voltage < 200 || request.Voltage > 250) && !mlDetectedAnomalyTypes.Contains("VoltageAnomaly"))
            {
                var existingAlert = await _context.Alerts
                    .FirstOrDefaultAsync(a => a.AlertType == "VoltageAnomaly" &&
                                               !a.IsResolved &&
                                               a.Title.Contains(device.DeviceName) &&
                                               a.CreatedAt >= oneHourAgo);

                if (existingAlert == null)
                {
                    await _alertService.CreateAlertAsync(
                        device.UserId,
                        $"Voltaj Anomalisi - {device.DeviceName}",
                        $"{device.DeviceName} cihazında voltaj anomalisi tespit edildi: {request.Voltage:F2}V (Normal: 220V)",
                        "VoltageAnomaly",
                        request.Voltage < 180 || request.Voltage > 260 ? "Critical" : "Medium",
                        null, // DeviceId yok
                        JsonSerializer.Serialize(new { Voltage = request.Voltage, NormalMin = 200, NormalMax = 250, SensorName = device.DeviceName })
                    );
                }
            }

            // 🔹 Düşük Güç Faktörü Kontrolü
            // ML servisi zaten "LowPowerFactor" tespit ettiyse atla
            if (request.PowerFactor < 0.7 && !mlDetectedAnomalyTypes.Contains("LowPowerFactor"))
            {
                var existingAlert = await _context.Alerts
                    .FirstOrDefaultAsync(a => a.AlertType == "LowPowerFactor" &&
                                               !a.IsResolved &&
                                               a.Title.Contains(device.DeviceName) &&
                                               a.CreatedAt >= oneHourAgo);

                if (existingAlert == null)
                {
                    await _alertService.CreateAlertAsync(
                        device.UserId,
                        $"Düşük Güç Faktörü - {device.DeviceName}",
                        $"{device.DeviceName} cihazında düşük güç faktörü tespit edildi: {request.PowerFactor:F2} (Normal: >0.8)",
                        "LowPowerFactor",
                        request.PowerFactor < 0.5 ? "High" : "Medium",
                        null, // DeviceId yok
                        JsonSerializer.Serialize(new { PowerFactor = request.PowerFactor, Threshold = 0.7, SensorName = device.DeviceName })
                    );
                }
            }
        }
    }

    // DTO sınıfları
    /// <summary>
    /// Sensör Verisi Request Modeli: IoT cihazlarından gelen sensör verilerini almak için kullanılır
    /// Validation: DataAnnotations ile veri doğrulama yapılır
    /// </summary>
    public class SensorDataRequest
    {
        [Required(ErrorMessage = "Sensör adı zorunludur")]
        [StringLength(100, ErrorMessage = "Sensör adı en fazla 100 karakter olabilir")]
        public string SensorName { get; set; } = string.Empty;

        [StringLength(50)]
        public string? SensorType { get; set; }

        [Range(-50, 1000, ErrorMessage = "Sıcaklık -50 ile 1000 arasında olmalıdır")]
        public double Temperature { get; set; }

        [Range(0, 100, ErrorMessage = "Gaz seviyesi 0 ile 100 arasında olmalıdır")]
        public double GasLevel { get; set; }

        [Range(0, 10000000, ErrorMessage = "Enerji kullanımı 0 ile 10.000.000 arasında olmalıdır")]
        public double EnergyUsage { get; set; }

        [Range(0, 500, ErrorMessage = "Voltaj 0 ile 500 arasında olmalıdır")]
        public double Voltage { get; set; }

        [Range(0, 1000, ErrorMessage = "Akım 0 ile 1000 arasında olmalıdır")]
        public double Current { get; set; }

        [Range(0, 1, ErrorMessage = "Güç faktörü 0 ile 1 arasında olmalıdır")]
        public double PowerFactor { get; set; } = 1.0;

        [StringLength(100)]
        public string? Location { get; set; }

        [StringLength(50)]
        public string? Status { get; set; }

        public Dictionary<string, object>? RawData { get; set; }

        [StringLength(100)]
        public string? FirmwareVersion { get; set; }

        [StringLength(50)]
        public string? SignalStrength { get; set; }

        public int? DeviceId { get; set; }

        [StringLength(50)]
        public string? WeatherCondition { get; set; }
    }

    public class DeviceStatusRequest
    {
        public bool IsActive { get; set; }
        public DateTime? LastMaintenanceAt { get; set; }
    }

    public class CreateDeviceRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? SerialNumber { get; set; }
        public string? Model { get; set; }
        public string? Manufacturer { get; set; }
        public double MaxPowerConsumption { get; set; } = 1000;
        public double MinPowerConsumption { get; set; } = 0;
        public bool? IsActive { get; set; }
    }
}
