using Microsoft.AspNetCore.Mvc;

namespace AygazSmartEnergy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DeviceController : ControllerBase
    {
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var deviceStatus = new
            {
                DeviceId = 1,
                DeviceName = "EnergyMeter001",
                Temperature = 26.4,
                Voltage = 220.5,
                Status = "Active"
            };

            return Ok(deviceStatus);
        }
    }
}

