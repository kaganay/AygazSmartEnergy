using Microsoft.AspNetCore.Mvc;
using AygazSmartEnergy.Data;
using AygazSmartEnergy.Models;

namespace AygazSmartEnergy.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EnergyApiController : ControllerBase
    {
        private readonly AppDbContext _context;

        public EnergyApiController(AppDbContext context)
        {
            _context = context;
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
