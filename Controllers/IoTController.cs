using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AygazSmartEnergy.Data;
using AygazSmartEnergy.Models;
using System.Text.Json;

namespace AygazSmartEnergy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class IoTController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<IoTController> _logger;

        public IoTController(AppDbContext context, ILogger<IoTController> logger)
        {
            _context = context;
            _logger = logger;
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

                // Enerji tüketimi verisi de oluştur (eğer DeviceId varsa)
                if (request.DeviceId.HasValue)
                {
                    await CreateEnergyConsumptionRecord(request, sensorData.Id);
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
                    .Include(s => s.Device);

                if (deviceId.HasValue)
                {
                    query = query.Where(s => s.DeviceId == deviceId);
                }

                var sensorData = await query
                    .OrderByDescending(s => s.RecordedAt)
                    .Take(count)
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
}
