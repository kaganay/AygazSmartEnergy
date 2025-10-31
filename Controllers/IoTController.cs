using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using AygazSmartEnergy.Data;
using AygazSmartEnergy.Models;
using AygazSmartEnergy.Hubs;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using AygazSmartEnergy.Services;

namespace AygazSmartEnergy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class IoTController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<IoTController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IDeviceControlService _deviceControlService;
        private readonly IHubContext<EnergyHub> _hubContext;

        public IoTController(AppDbContext context, ILogger<IoTController> logger, IConfiguration configuration, IDeviceControlService deviceControlService, IHubContext<EnergyHub> hubContext)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
            _deviceControlService = deviceControlService;
            _hubContext = hubContext;
        }

        /// <summary>
        /// IoT cihazlarından gelen sensör verilerini alır
        /// </summary>
        [HttpPost("sensor-data")]
        public async Task<IActionResult> PostSensorData([FromBody] SensorDataRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // Sensör verisini oluştur
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
                    DeviceId = request.DeviceId
                };

                _context.SensorDatas.Add(sensorData);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Sensor data received from {request.SensorName} at {sensorData.RecordedAt}");

                // MQ-2 gaz eşik kontrolü ve fan kontrolü
                var autoFanEnabled = _configuration.GetValue<bool>("GasSettings:AutoFanEnabled");
                var threshold = _configuration.GetValue<double>("GasSettings:Mq2Threshold", 40.0);
                var isMq2 = string.Equals(request.SensorType, "Gas", StringComparison.OrdinalIgnoreCase) ||
                            request.SensorName.Contains("mq2", StringComparison.OrdinalIgnoreCase) ||
                            (request.RawData != null && request.RawData.TryGetValue("sensor", out var sensorNameObj) && sensorNameObj?.ToString()?.Contains("mq2", StringComparison.OrdinalIgnoreCase) == true);

                bool? fanStateChanged = null;
                if (autoFanEnabled && isMq2)
                {
                    if (request.GasLevel > threshold)
                    {
                        var newState = await _deviceControlService.SetFanStateAsync(true);
                        fanStateChanged = newState;
                        _logger.LogWarning("Gas level {gas} exceeded threshold {threshold}. Fan turned ON.", request.GasLevel, threshold);
                    }
                    else if (_deviceControlService.GetFanState())
                    {
                        var newState = await _deviceControlService.SetFanStateAsync(false);
                        fanStateChanged = newState;
                        _logger.LogInformation("Gas level {gas} below threshold {threshold}. Fan turned OFF.", request.GasLevel, threshold);
                    }
                }

                // DHT22 sıcaklık kontrolü ve fan kontrolü
                var isDht22 = string.Equals(request.SensorType, "Temperature", StringComparison.OrdinalIgnoreCase) ||
                              request.SensorName.Contains("dht22", StringComparison.OrdinalIgnoreCase) ||
                              request.SensorName.Contains("dht", StringComparison.OrdinalIgnoreCase) ||
                              (request.Temperature > 0 && request.RawData != null && request.RawData.TryGetValue("sensor", out var sensorObj) && sensorObj?.ToString()?.Contains("dht", StringComparison.OrdinalIgnoreCase) == true);

                if (isDht22 && request.Temperature > 0)
                {
                    var previousFanState = _deviceControlService.GetFanState();
                    var newFanState = await _deviceControlService.CheckTemperatureAndControlFanAsync(request.Temperature);
                    if (previousFanState != newFanState)
                    {
                        fanStateChanged = newFanState;
                    }
                }

                // SignalR ile sensör verisini broadcast et
                await _hubContext.NotifySensorDataUpdate(sensorData);

                // Fan durumu değiştiyse SignalR ile bildir
                if (fanStateChanged.HasValue)
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveFanStatus", new
                    {
                        Type = "FanStatusChanged",
                        IsOn = fanStateChanged.Value,
                        ChangedAt = DateTime.Now,
                        Reason = isDht22 ? $"Temperature: {request.Temperature}°C" : $"Gas: {request.GasLevel}%"
                    });
                }

                // Enerji tüketimi verisi de oluştur (eğer DeviceId varsa)
                if (request.DeviceId.HasValue)
                {
                    await CreateEnergyConsumptionRecord(request, sensorData.Id);
                }

                return Ok(new { 
                    success = true, 
                    message = "Sensor data received successfully",
                    id = sensorData.Id,
                    timestamp = sensorData.RecordedAt,
                    autoFan = fanStateChanged,
                    fanState = _deviceControlService.GetFanState()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing sensor data");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// Fan durumunu getirir
        /// </summary>
        [HttpGet("fan")]
        public IActionResult GetFan()
        {
            var state = _deviceControlService.GetFanState();
            return Ok(new { success = true, on = state });
        }

        /// <summary>
        /// Fanı manuel aç/kapat
        /// </summary>
        [HttpPost("fan")]
        public async Task<IActionResult> SetFan([FromBody] FanToggleRequest request)
        {
            var state = await _deviceControlService.SetFanStateAsync(request.On);
            
            // SignalR ile fan durumunu bildir
            await _hubContext.Clients.All.SendAsync("ReceiveFanStatus", new
            {
                Type = "FanStatusChanged",
                IsOn = state,
                ChangedAt = DateTime.Now,
                Reason = "Manual"
            });

            return Ok(new { success = true, on = state });
        }

        /// <summary>
        /// Cihaz durumunu günceller
        /// </summary>
        [HttpPut("device-status/{deviceId}")]
        public async Task<IActionResult> UpdateDeviceStatus(int deviceId, [FromBody] DeviceStatusRequest request)
        {
            try
            {
                var device = await _context.Devices.FindAsync(deviceId);
                if (device == null)
                {
                    return NotFound(new { success = false, message = "Device not found" });
                }

                device.IsActive = request.IsActive;
                device.LastMaintenanceAt = request.LastMaintenanceAt;

                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Device status updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating device status");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// Son sensör verilerini getirir
        /// </summary>
        [HttpGet("sensor-data/latest")]
        public async Task<IActionResult> GetLatestSensorData([FromQuery] int? deviceId = null, [FromQuery] int count = 10)
        {
            try
            {
                IQueryable<SensorData> query = _context.SensorDatas
                    .AsNoTracking(); // Döngüsel referansı önlemek için tracking'i kapat

                if (deviceId.HasValue)
                {
                    query = query.Where(s => s.DeviceId == deviceId);
                }

                var sensorData = await query
                    .OrderByDescending(s => s.RecordedAt)
                    .Take(count)
                    .Select(s => new
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
                    .ToListAsync();

                return Ok(new { success = true, data = sensorData });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sensor data");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        /// <summary>
        /// Cihaz listesini getirir
        /// </summary>
        [HttpGet("devices")]
        public async Task<IActionResult> GetDevices([FromQuery] string? userId = null)
        {
            try
            {
                var query = _context.Devices.Include(d => d.User).AsQueryable();

                if (!string.IsNullOrEmpty(userId))
                {
                    query = query.Where(d => d.UserId == userId);
                }

                var devices = await query.ToListAsync();

                return Ok(new { success = true, data = devices });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving devices");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        private async Task CreateEnergyConsumptionRecord(SensorDataRequest request, int sensorDataId)
        {
            try
            {
                var energyConsumption = new EnergyConsumption
                {
                    DeviceId = request.DeviceId!.Value,
                    PowerConsumption = request.EnergyUsage, // Watts
                    EnergyUsed = CalculateEnergyUsed(request.EnergyUsage, request.Voltage, request.Current), // kWh
                    Voltage = request.Voltage,
                    Current = request.Current,
                    PowerFactor = request.PowerFactor,
                    Temperature = request.Temperature,
                    GasLevel = request.GasLevel,
                    WeatherCondition = request.WeatherCondition
                };

                _context.EnergyConsumptions.Add(energyConsumption);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating energy consumption record");
            }
        }

        private double CalculateEnergyUsed(double powerConsumption, double voltage, double current)
        {
            // Basit hesaplama: Power (W) * Time (1 hour) / 1000 = kWh
            // Gerçek uygulamada zaman aralığı dikkate alınmalı
            return (powerConsumption * 1.0) / 1000.0; // 1 saatlik tüketim
        }
    }

    // DTO sınıfları
    public class SensorDataRequest
    {
        public string SensorName { get; set; } = string.Empty;
        public string? SensorType { get; set; }
        public double Temperature { get; set; }
        public double GasLevel { get; set; }
        public double EnergyUsage { get; set; }
        public double Voltage { get; set; }
        public double Current { get; set; }
        public double PowerFactor { get; set; } = 1.0;
        public string? Location { get; set; }
        public string? Status { get; set; }
        public Dictionary<string, object>? RawData { get; set; }
        public string? FirmwareVersion { get; set; }
        public string? SignalStrength { get; set; }
        public int? DeviceId { get; set; }
        public string? WeatherCondition { get; set; }
    }

    public class DeviceStatusRequest
    {
        public bool IsActive { get; set; }
        public DateTime? LastMaintenanceAt { get; set; }
    }

    public class FanToggleRequest
    {
        public bool On { get; set; }
    }
}
