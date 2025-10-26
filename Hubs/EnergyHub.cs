using Microsoft.AspNetCore.SignalR;
using AygazSmartEnergy.Models;

namespace AygazSmartEnergy.Hubs
{
    public class EnergyHub : Hub
    {
        private readonly ILogger<EnergyHub> _logger;

        public EnergyHub(ILogger<EnergyHub> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Kullanıcı belirli bir cihazı dinlemeye başladığında
        /// </summary>
        public async Task JoinDeviceGroup(int deviceId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Device_{deviceId}");
            _logger.LogInformation($"User {Context.ConnectionId} joined device group {deviceId}");
        }

        /// <summary>
        /// Kullanıcı cihaz dinlemeyi bıraktığında
        /// </summary>
        public async Task LeaveDeviceGroup(int deviceId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Device_{deviceId}");
            _logger.LogInformation($"User {Context.ConnectionId} left device group {deviceId}");
        }

        /// <summary>
        /// Kullanıcı belirli bir kullanıcının tüm cihazlarını dinlemeye başladığında
        /// </summary>
        public async Task JoinUserGroup(int userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");
            _logger.LogInformation($"User {Context.ConnectionId} joined user group {userId}");
        }

        /// <summary>
        /// Kullanıcı kullanıcı dinlemeyi bıraktığında
        /// </summary>
        public async Task LeaveUserGroup(int userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"User_{userId}");
            _logger.LogInformation($"User {Context.ConnectionId} left user group {userId}");
        }

        /// <summary>
        /// Bağlantı kurulduğunda
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"Client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Bağlantı kesildiğinde
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
            await base.OnDisconnectedAsync(exception);
        }
    }

    /// <summary>
    /// SignalR hub'ı için extension metodları
    /// </summary>
    public static class EnergyHubExtensions
    {
        /// <summary>
        /// Yeni sensör verisi geldiğinde tüm ilgili kullanıcılara bildirim gönder
        /// </summary>
        public static async Task NotifySensorDataUpdate(this IHubContext<EnergyHub> hub, SensorData sensorData)
        {
            var message = new
            {
                Type = "SensorDataUpdate",
                DeviceId = sensorData.DeviceId,
                SensorName = sensorData.SensorName,
                Temperature = sensorData.Temperature,
                GasLevel = sensorData.GasLevel,
                EnergyUsage = sensorData.EnergyUsage,
                Voltage = sensorData.Voltage,
                Current = sensorData.Current,
                PowerFactor = sensorData.PowerFactor,
                RecordedAt = sensorData.RecordedAt,
                Status = sensorData.Status
            };

            // Cihaz grubuna bildirim gönder
            if (sensorData.DeviceId.HasValue)
            {
                await hub.Clients.Group($"Device_{sensorData.DeviceId}").SendAsync("ReceiveSensorData", message);
            }

            // Genel bildirim
            await hub.Clients.All.SendAsync("ReceiveSensorData", message);
        }

        /// <summary>
        /// Enerji tüketimi verisi güncellendiğinde bildirim gönder
        /// </summary>
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

            // Cihaz grubuna bildirim gönder
            await hub.Clients.Group($"Device_{energyConsumption.DeviceId}").SendAsync("ReceiveEnergyConsumption", message);

            // Genel bildirim
            await hub.Clients.All.SendAsync("ReceiveEnergyConsumption", message);
        }

        /// <summary>
        /// Yeni uyarı oluşturulduğunda bildirim gönder
        /// </summary>
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
                CreatedAt = alert.CreatedAt,
                IsRead = alert.IsRead
            };

            // Kullanıcı grubuna bildirim gönder
            await hub.Clients.Group($"User_{alert.UserId}").SendAsync("ReceiveAlert", message);

            // Cihaz grubuna da bildirim gönder (eğer cihaz varsa)
            if (alert.DeviceId.HasValue)
            {
                await hub.Clients.Group($"Device_{alert.DeviceId}").SendAsync("ReceiveAlert", message);
            }
        }

        /// <summary>
        /// Uyarı güncellendiğinde bildirim gönder
        /// </summary>
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

            // Kullanıcı grubuna bildirim gönder
            await hub.Clients.Group($"User_{alert.UserId}").SendAsync("ReceiveAlertUpdate", message);
        }

        /// <summary>
        /// Cihaz durumu değiştiğinde bildirim gönder
        /// </summary>
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
                UpdatedAt = DateTime.Now
            };

            // Cihaz grubuna bildirim gönder
            await hub.Clients.Group($"Device_{device.Id}").SendAsync("ReceiveDeviceStatus", message);

            // Kullanıcı grubuna da bildirim gönder
            await hub.Clients.Group($"User_{device.UserId}").SendAsync("ReceiveDeviceStatus", message);
        }

        /// <summary>
        /// Enerji analiz raporu hazır olduğunda bildirim gönder
        /// </summary>
        public static async Task NotifyAnalysisReportReady(this IHubContext<EnergyHub> hub, int deviceId, int userId, string reportType)
        {
            var message = new
            {
                Type = "AnalysisReportReady",
                DeviceId = deviceId,
                ReportType = reportType,
                ReadyAt = DateTime.Now
            };

            // Kullanıcı grubuna bildirim gönder
            await hub.Clients.Group($"User_{userId}").SendAsync("ReceiveAnalysisReport", message);

            // Cihaz grubuna da bildirim gönder
            await hub.Clients.Group($"Device_{deviceId}").SendAsync("ReceiveAnalysisReport", message);
        }
    }
}
