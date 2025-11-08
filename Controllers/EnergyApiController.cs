using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using AygazSmartEnergy.Configuration;
using AygazSmartEnergy.Data;
using AygazSmartEnergy.Models;
using AygazSmartEnergy.Services;

namespace AygazSmartEnergy.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EnergyApiController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IMessageBus _messageBus;
        private readonly RabbitMqOptions _rabbitOptions;

        public EnergyApiController(
            AppDbContext context,
            IMessageBus messageBus,
            IOptions<RabbitMqOptions> rabbitOptions)
        {
            _context = context;
            _messageBus = messageBus;
            _rabbitOptions = rabbitOptions.Value;
        }

        // ESP8266'dan veri almak için endpoint
        [HttpPost("upload")]
        public async Task<IActionResult> UploadData([FromBody] EnergyConsumption data)
        {
            if (data == null)
                return BadRequest("Geçersiz veri");

            data.RecordedAt = DateTime.Now;
            _context.EnergyConsumptions.Add(data);
            await _context.SaveChangesAsync();

            _ = _messageBus.PublishAsync(
                _rabbitOptions.SensorQueue ?? "sensor-data",
                new
                {
                    data.Id,
                    data.DeviceId,
                    data.EnergyUsed,
                    data.CostPerHour,
                    data.CarbonFootprint,
                    RecordedAt = data.RecordedAt
                });

            return Ok(new { message = "Veri başarıyla kaydedildi" });
        }

        // Web Dashboard'ın en son verileri alması için
        [HttpGet("latest")]
        public IActionResult GetLatest()
        {
            var lastData = _context.EnergyConsumptions
                .OrderByDescending(e => e.RecordedAt)
                .Take(10)
                .ToList();

            return Ok(lastData);
        }
    }
}
