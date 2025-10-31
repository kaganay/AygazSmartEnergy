using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace AygazSmartEnergy.Services
{
    public interface IDeviceControlService
    {
        Task<bool> SetFanStateAsync(bool turnOn);
        bool GetFanState();
        Task<bool> CheckTemperatureAndControlFanAsync(double temperature);
    }

    public class DeviceControlService : IDeviceControlService
    {
        private readonly ILogger<DeviceControlService> _logger;
        private readonly IConfiguration _configuration;
        private static bool _fanOn;
        private static bool _manualControl = false; // Manuel kontrol modunda ise otomatik devreye girmesin

        public DeviceControlService(ILogger<DeviceControlService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public Task<bool> SetFanStateAsync(bool turnOn)
        {
            _fanOn = turnOn;
            _manualControl = true; // Manuel kontrol yapıldı
            _logger.LogInformation("Fan state changed manually: {state}", turnOn ? "ON" : "OFF");
            // TODO: Integrate with actual GPIO or device API here
            return Task.FromResult(_fanOn);
        }

        public bool GetFanState()
        {
            return _fanOn;
        }

        public Task<bool> CheckTemperatureAndControlFanAsync(double temperature)
        {
            var autoFanEnabled = _configuration.GetValue<bool>("TemperatureSettings:AutoFanEnabled", true);
            var threshold = _configuration.GetValue<double>("TemperatureSettings:Threshold", 27.0);

            if (!autoFanEnabled || _manualControl)
            {
                return Task.FromResult(_fanOn);
            }

            bool shouldBeOn = temperature > threshold;
            
            if (shouldBeOn != _fanOn)
            {
                _fanOn = shouldBeOn;
                _logger.LogInformation("Fan automatically {action} due to temperature {temp}°C (threshold: {threshold}°C)", 
                    shouldBeOn ? "turned ON" : "turned OFF", temperature, threshold);
            }

            return Task.FromResult(_fanOn);
        }

        public void ResetManualControl()
        {
            _manualControl = false;
        }
    }
}


