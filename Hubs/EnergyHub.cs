using Microsoft.AspNetCore.SignalR;
using AygazSmartEnergy.Models;

// SignalR hub: cihaz/kullanıcı grupları ve canlı bildirimler.
namespace AygazSmartEnergy.Hubs
{
    public class EnergyHub : Hub
    {
        private readonly ILogger<EnergyHub> _logger;

        public EnergyHub(ILogger<EnergyHub> logger)
        {
            _logger = logger;
        }

        public async Task JoinDeviceGroup(int deviceId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Device_{deviceId}");
            _logger.LogInformation($"User {Context.ConnectionId} joined device group {deviceId}");
        }

        public async Task LeaveDeviceGroup(int deviceId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Device_{deviceId}");
            _logger.LogInformation($"User {Context.ConnectionId} left device group {deviceId}");
        }

        public async Task JoinUserGroup(int userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");
            _logger.LogInformation($"User {Context.ConnectionId} joined user group {userId}");
        }

        public async Task LeaveUserGroup(int userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"User_{userId}");
            _logger.LogInformation($"User {Context.ConnectionId} left user group {userId}");
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"Client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
            await base.OnDisconnectedAsync(exception);
        }
    }

    public static class EnergyHubExtensions
    {
        public static async Task NotifySensorDataUpdate(this IHubContext<EnergyHub> hub, SensorData sensorData)
        {
            string? deviceName = null;
            if (sensorData.DeviceId.HasValue)
            {
            }

            var message = new
            {
                Type = "SensorDataUpdate",
                DeviceId = sensorData.DeviceId,
                DeviceName = deviceName,
                SensorName = sensorData.SensorName,
                Temperature = sensorData.Temperature,
                GasLevel = sensorData.GasLevel,
                EnergyUsage = sensorData.EnergyUsage,
                Voltage = sensorData.Voltage,
                Current = sensorData.Current,
                PowerFactor = sensorData.PowerFactor,
                Location = sensorData.Location,
                RecordedAt = sensorData.RecordedAt,
                Status = sensorData.Status
            };

            if (sensorData.DeviceId.HasValue)
            {
                await hub.Clients.Group($"Device_{sensorData.DeviceId}").SendAsync("ReceiveSensorData", message);
            }

            await hub.Clients.All.SendAsync("ReceiveSensorData", message);
        }

        public static async Task NotifyEnergyConsumptionUpdate(this IHubContext<EnergyHub> hub, EnergyConsumption energyConsumption)
        {
            var message = new
            {
                Type = "EnergyConsumptionUpdate",
                DeviceId = energyConsumption.DeviceId,
                PowerConsumption = energyConsumption.PowerConsumption,
                EnergyUsed = energyConsumption.EnergyUsed,
                CostPerHour = energyConsumption.CostPerHour,
                CarbonFootprint = energyConsumption.CarbonFootprint,
                RecordedAt = energyConsumption.RecordedAt
            };

            await hub.Clients.Group($"Device_{energyConsumption.DeviceId}").SendAsync("ReceiveEnergyConsumption", message);
            await hub.Clients.All.SendAsync("ReceiveEnergyConsumption", message);
        }

        public static async Task NotifyAlertCreated(this IHubContext<EnergyHub> hub, Alert alert)
        {
            var message = new
            {
                Type = "AlertCreated",
                Id = alert.Id,
                Title = alert.Title,
                Message = alert.Message,
                AlertType = alert.AlertType,
                Severity = alert.Severity,
                DeviceId = alert.DeviceId,
                DeviceName = alert.Device?.DeviceName ?? "Bilinmeyen Cihaz",
                CreatedAt = alert.CreatedAt,
                IsRead = alert.IsRead,
                IsResolved = alert.IsResolved
            };

            await hub.Clients.All.SendAsync("ReceiveAlert", message);

            await hub.Clients.Group($"User_{alert.UserId}").SendAsync("ReceiveAlert", message);

            if (alert.DeviceId.HasValue)
            {
                await hub.Clients.Group($"Device_{alert.DeviceId}").SendAsync("ReceiveAlert", message);
            }
        }

        public static async Task NotifyAlertUpdated(this IHubContext<EnergyHub> hub, Alert alert)
        {
            var message = new
            {
                Type = "AlertUpdated",
                Id = alert.Id,
                Title = alert.Title,
                IsRead = alert.IsRead,
                IsResolved = alert.IsResolved,
                ResolvedAt = alert.ResolvedAt,
                ActionTaken = alert.ActionTaken
            };

            await hub.Clients.Group($"User_{alert.UserId}").SendAsync("ReceiveAlertUpdate", message);
        }

        public static async Task NotifyDeviceStatusChanged(this IHubContext<EnergyHub> hub, Device device)
        {
            var message = new
            {
                Type = "DeviceStatusChanged",
                DeviceId = device.Id,
                Name = device.DeviceName,
                IsActive = device.IsActive,
                Status = device.IsActive ? "Active" : "Inactive",
                LastMaintenanceAt = device.LastMaintenanceAt,
                UpdatedAt = DateTime.UtcNow
            };

            await hub.Clients.Group($"Device_{device.Id}").SendAsync("ReceiveDeviceStatus", message);
            await hub.Clients.Group($"User_{device.UserId}").SendAsync("ReceiveDeviceStatus", message);
        }

        public static async Task NotifyAnalysisReportReady(this IHubContext<EnergyHub> hub, int deviceId, int userId, string reportType)
        {
            var message = new
            {
                Type = "AnalysisReportReady",
                DeviceId = deviceId,
                ReportType = reportType,
                ReadyAt = DateTime.UtcNow
            };

            await hub.Clients.Group($"User_{userId}").SendAsync("ReceiveAnalysisReport", message);
            await hub.Clients.Group($"Device_{deviceId}").SendAsync("ReceiveAnalysisReport", message);
        }
    }
}
