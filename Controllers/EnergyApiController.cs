// 🔹 Namespace'ler: Gerekli kütüphaneleri içe aktarır
using Microsoft.AspNetCore.Mvc;              // ControllerBase, ApiController, HttpPost/HttpGet
using Microsoft.Extensions.Options;         // IOptions<T> (yapılandırma sınıfları için)
using Microsoft.AspNetCore.SignalR;         // IHubContext
using AygazSmartEnergy.Configuration;       // RabbitMqOptions
using AygazSmartEnergy.Data;                // AppDbContext
using AygazSmartEnergy.Models;             // EnergyConsumption, Alert
using AygazSmartEnergy.Services;            // IMessageBus, IAlertService
using AygazSmartEnergy.Hubs;                // EnergyHub
using System.Text.Json;                      // JsonSerializer

// ML sonuçlarını alır, alert üretir, RabbitMQ/SinalR ile dağıtır.
namespace AygazSmartEnergy.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EnergyApiController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IMessageBus _messageBus;
        private readonly RabbitMqOptions _rabbitOptions;
        private readonly IAlertService _alertService;
        private readonly IHubContext<EnergyHub> _hubContext;

        public EnergyApiController(
            AppDbContext context,
            IMessageBus messageBus,
            IOptions<RabbitMqOptions> rabbitOptions,
            IAlertService alertService,
            IHubContext<EnergyHub> hubContext)
        {
            _context = context;
            _messageBus = messageBus;
            _rabbitOptions = rabbitOptions.Value;
            _alertService = alertService;
            _hubContext = hubContext;
        }


        [HttpGet("latest")]
        public IActionResult GetLatest()
        {
            var lastData = _context.EnergyConsumptions
                .OrderByDescending(e => e.RecordedAt)
                .Take(10)
                .ToList();

            return Ok(lastData);
        }

        [HttpPost("ml-results")]
        public async Task<IActionResult> ReceiveMLResults([FromBody] MLResultRequest request)
        {
            if (request == null || request.DeviceId == 0)
                return BadRequest("Geçersiz veri");

            try
            {
                var logger = HttpContext.RequestServices.GetRequiredService<ILogger<EnergyApiController>>();

                logger.LogInformation(
                    "ML sonucu alındı: DeviceId={DeviceId}, ResultType={ResultType}, ProcessedAt={ProcessedAt}",
                    request.DeviceId, request.ResultType, request.ProcessedAt);

                if (request.ResultType == "anomaly_detection" && request.ResultData.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    if (request.ResultData.TryGetProperty("anomalies", out var anomalies) && anomalies.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var device = await _context.Devices.FindAsync(request.DeviceId);
                        if (device == null)
                        {
                            logger.LogWarning($"ML anomali sonucu için cihaz bulunamadı. DeviceId: {request.DeviceId}");
                            return BadRequest(new { error = "Cihaz bulunamadı" });
                        }

                        int alertCount = 0;
                        foreach (var anomaly in anomalies.EnumerateArray())
                        {
                            try
                            {
                                var severityValue = anomaly.TryGetProperty("Severity", out var severityPropUpper) 
                                    ? severityPropUpper.GetDouble() 
                                    : anomaly.TryGetProperty("severity", out var severityPropLower)
                                        ? severityPropLower.GetDouble()
                                        : 0.5;
                                
                                var severityLevel = severityValue > 0.8 ? "Critical" 
                                    : severityValue > 0.6 ? "High" 
                                    : severityValue > 0.4 ? "Medium" 
                                    : "Low";

                                // Python ML servisi hem AnomalyType/Description hem anomalyType/description
                                // şeklinde anahtarlar gönderebileceği için ikisini de dene.
                                string anomalyType =
                                    anomaly.TryGetProperty("AnomalyType", out var atUpper)
                                        ? atUpper.GetString() ?? "Unknown"
                                        : anomaly.TryGetProperty("anomalyType", out var atLower)
                                            ? atLower.GetString() ?? "Unknown"
                                            : "Unknown";

                                string description =
                                    anomaly.TryGetProperty("Description", out var descUpper)
                                        ? descUpper.GetString() ?? "ML servisi tarafından anomali tespit edildi"
                                        : anomaly.TryGetProperty("description", out var descLower)
                                            ? descLower.GetString() ?? "ML servisi tarafından anomali tespit edildi"
                                            : "ML servisi tarafından anomali tespit edildi";

                                // Anomali JSON'ını hazırla
                                var anomalyJson = anomaly.GetRawText();

                                logger.LogInformation($"ML Anomali Alert oluşturuluyor: Type={anomalyType}, Severity={severityLevel}, DeviceId={request.DeviceId}");

                                await _alertService.CreateAlertAsync(
                                    device.UserId,
                                    $"ML Anomali: {anomalyType}",
                                    $"{device.DeviceName} cihazında {description}",
                                    anomalyType,
                                    severityLevel,
                                    device.Id,
                                    anomalyJson
                                );

                                alertCount++;
                                logger.LogInformation($"✓ ML Anomali Alert başarıyla oluşturuldu: Type={anomalyType}, DeviceId={request.DeviceId}");
                            }
                            catch (Exception alertEx)
                            {
                                logger.LogError(alertEx, $"ML anomali alert'i oluşturulurken hata oluştu. DeviceId: {request.DeviceId}, Anomaly: {anomaly.GetRawText()}");
                            }
                        }

                        if (alertCount > 0)
                        {
                            logger.LogInformation($"ML servisi {alertCount} anomali alert'i oluşturdu. DeviceId: {request.DeviceId}");
                        }
                    }
                    else
                    {
                        logger.LogDebug($"ML servisi anomali sonucu gönderdi ancak 'anomalies' array'i bulunamadı. DeviceId: {request.DeviceId}");
                    }
                }

                // Verimlilik skoru sonuçları için log
                if (request.ResultType == "efficiency_score" && request.ResultData.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    logger.LogInformation(
                        "Verimlilik skoru: DeviceId={DeviceId}, Score={Score}, Level={Level}",
                        request.DeviceId,
                        request.ResultData.TryGetProperty("overallScore", out var scoreProp) ? scoreProp.GetDouble() : 0,
                        request.ResultData.TryGetProperty("efficiencyLevel", out var levelProp) ? levelProp.GetString() : "Unknown");
                }

                await _context.SaveChangesAsync();

                // RabbitMQ'ya ML sonuç mesajı gönder
                _ = _messageBus.PublishAsync(
                    _rabbitOptions.SensorQueue ?? "sensor-data",
                    new
                    {
                        DeviceId = request.DeviceId,
                        ResultType = request.ResultType,
                        ProcessedAt = request.ProcessedAt,
                        MLServiceVersion = request.MLServiceVersion
                    });

                return Ok(new { message = "ML sonucu başarıyla işlendi", deviceId = request.DeviceId });
            }
            catch (Exception ex)
            {
                var logger = HttpContext.RequestServices.GetRequiredService<ILogger<EnergyApiController>>();
                logger.LogError(ex, "ML sonucu işlenirken hata oluştu");
                return StatusCode(500, new { error = "ML sonucu işlenirken hata oluştu" });
            }
        }
    }

    // ML sonuç request modeli
    public class MLResultRequest
    {
        public int DeviceId { get; set; }
        public string ResultType { get; set; } = string.Empty;
        public System.Text.Json.JsonElement ResultData { get; set; }
        public string ProcessedAt { get; set; } = string.Empty;
        public string? MLServiceVersion { get; set; }
    }
}
