using Microsoft.EntityFrameworkCore;
using AygazSmartEnergy.Data;
using AygazSmartEnergy.Models;
using AygazSmartEnergy.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace AygazSmartEnergy.Services
{
    public class AlertService : IAlertService
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<EnergyHub> _hubContext;
        private readonly ILogger<AlertService> _logger;

        public AlertService(AppDbContext context, IHubContext<EnergyHub> hubContext, ILogger<AlertService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task CreateAlertAsync(string userId, string title, string message, string alertType, string severity, int? deviceId = null, string? additionalData = null)
        {
            try
            {
                var alert = new Alert
                {
                    UserId = userId,
                    DeviceId = deviceId,
                    Title = title,
                    Message = message,
                    AlertType = alertType,
                    Severity = severity,
                    AdditionalData = additionalData
                };

                _context.Alerts.Add(alert);
                await _context.SaveChangesAsync();

                // SignalR ile gerçek zamanlı bildirim gönder
                await _hubContext.NotifyAlertCreated(alert);

                // E-posta bildirimi gönder (eğer kritikse)
                if (severity == "Critical" || severity == "High")
                {
                    await SendAlertNotificationAsync(alert.Id, "Email");
                }

                _logger.LogInformation("Alert created: {AlertId} for user {UserId}", alert.Id, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating alert for user {UserId}", userId);
                throw;
            }
        }

        public async Task<List<Alert>> GetUserAlertsAsync(string userId, bool includeResolved = false)
        {
            try
            {
                IQueryable<Alert> query = _context.Alerts
                    .Where(a => a.UserId == userId)
                    .Include(a => a.Device);

                if (!includeResolved)
                {
                    query = query.Where(a => !a.IsResolved);
                }

                return await query
                    .OrderByDescending(a => a.CreatedAt)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting alerts for user {UserId}", userId);
                return new List<Alert>();
            }
        }

        public async Task<Alert?> GetAlertByIdAsync(int alertId)
        {
            try
            {
                return await _context.Alerts
                    .Include(a => a.Device)
                    .FirstOrDefaultAsync(a => a.Id == alertId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting alert {AlertId}", alertId);
                return null;
            }
        }

        public async Task MarkAlertAsReadAsync(int alertId)
        {
            try
            {
                var alert = await _context.Alerts.FindAsync(alertId);
                if (alert != null)
                {
                    alert.IsRead = true;
                    alert.ReadAt = DateTime.Now;
                    await _context.SaveChangesAsync();

                    // SignalR ile güncelleme bildirimi gönder
                    await _hubContext.NotifyAlertUpdated(alert);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking alert {AlertId} as read", alertId);
                throw;
            }
        }

        public async Task ResolveAlertAsync(int alertId, string actionTaken)
        {
            try
            {
                var alert = await _context.Alerts.FindAsync(alertId);
                if (alert != null)
                {
                    alert.IsResolved = true;
                    alert.ResolvedAt = DateTime.Now;
                    alert.ActionTaken = actionTaken;
                    await _context.SaveChangesAsync();

                    // SignalR ile güncelleme bildirimi gönder
                    await _hubContext.NotifyAlertUpdated(alert);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving alert {AlertId}", alertId);
                throw;
            }
        }

        public async Task DeleteAlertAsync(int alertId)
        {
            try
            {
                var alert = await _context.Alerts.FindAsync(alertId);
                if (alert != null)
                {
                    _context.Alerts.Remove(alert);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting alert {AlertId}", alertId);
                throw;
            }
        }

        public async Task CheckAndCreateAlertsAsync()
        {
            try
            {
                // Yüksek enerji tüketimi kontrolü
                await CheckHighEnergyConsumptionAlertsAsync();

                // Sıcaklık anomali kontrolü
                await CheckTemperatureAnomalyAlertsAsync();

                // Cihaz durumu kontrolü
                await CheckDeviceStatusAlertsAsync();

                // Güç faktörü kontrolü
                await CheckPowerFactorAlertsAsync();

                // Voltaj anomali kontrolü
                await CheckVoltageAnomalyAlertsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking and creating alerts");
            }
        }

        public async Task SendAlertNotificationAsync(int alertId, string notificationType)
        {
            try
            {
                var alert = await _context.Alerts
                    .Include(a => a.User)
                    .FirstOrDefaultAsync(a => a.Id == alertId);

                if (alert == null) return;

                var notification = new AlertNotification
                {
                    AlertId = alertId,
                    NotificationType = notificationType,
                    SentAt = DateTime.Now
                };

                _context.AlertNotifications.Add(notification);

                // E-posta gönderme simülasyonu
                if (notificationType == "Email")
                {
                    await SendEmailNotificationAsync(alert);
                    notification.IsDelivered = true;
                    notification.DeliveredAt = DateTime.Now;
                }
                // SMS gönderme simülasyonu
                else if (notificationType == "SMS")
                {
                    await SendSMSNotificationAsync(alert);
                    notification.IsDelivered = true;
                    notification.DeliveredAt = DateTime.Now;
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending alert notification {AlertId}", alertId);
            }
        }

        private async Task CheckHighEnergyConsumptionAlertsAsync()
        {
            try
            {
                // Son 1 saatteki verileri kontrol et
                var oneHourAgo = DateTime.Now.AddHours(-1);
                var recentConsumptions = await _context.EnergyConsumptions
                    .Where(e => e.RecordedAt >= oneHourAgo)
                    .Include(e => e.Device)
                    .ToListAsync();

                foreach (var consumption in recentConsumptions)
                {
                    if (consumption.DeviceId != null)
                    {
                        var device = consumption.Device;
                        if (device != null && consumption.PowerConsumption > device.MaxPowerConsumption * 0.9)
                        {
                            // Aynı cihaz için son 1 saatte uyarı oluşturulmuş mu kontrol et
                            var existingAlert = await _context.Alerts
                                .FirstOrDefaultAsync(a => a.DeviceId == device.Id && 
                                                         a.AlertType == "HighConsumption" && 
                                                         a.CreatedAt >= oneHourAgo);

                            if (existingAlert == null)
                            {
                                await CreateAlertAsync(
                                    device.UserId,
                                    "Yüksek Enerji Tüketimi",
                                    $"{device.DeviceName} cihazında yüksek enerji tüketimi tespit edildi. Mevcut tüketim: {consumption.PowerConsumption:F2}W",
                                    "HighConsumption",
                                    "High",
                                    device.Id,
                                    JsonSerializer.Serialize(new { PowerConsumption = consumption.PowerConsumption, MaxPower = device.MaxPowerConsumption })
                                );
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking high energy consumption alerts");
            }
        }

        private async Task CheckTemperatureAnomalyAlertsAsync()
        {
            try
            {
                var oneHourAgo = DateTime.Now.AddHours(-1);
                var recentConsumptions = await _context.EnergyConsumptions
                    .Where(e => e.RecordedAt >= oneHourAgo && e.Temperature > 60)
                    .Include(e => e.Device)
                    .ToListAsync();

                foreach (var consumption in recentConsumptions)
                {
                    if (consumption.DeviceId != null)
                    {
                        var device = consumption.Device;
                        if (device != null)
                        {
                            var existingAlert = await _context.Alerts
                                .FirstOrDefaultAsync(a => a.DeviceId == device.Id && 
                                                         a.AlertType == "TemperatureAnomaly" && 
                                                         a.CreatedAt >= oneHourAgo);

                            if (existingAlert == null)
                            {
                                await CreateAlertAsync(
                                    device.UserId,
                                    "Sıcaklık Anomalisi",
                                    $"{device.DeviceName} cihazında yüksek sıcaklık tespit edildi. Mevcut sıcaklık: {consumption.Temperature:F2}°C",
                                    "TemperatureAnomaly",
                                    "High",
                                    device.Id,
                                    JsonSerializer.Serialize(new { Temperature = consumption.Temperature })
                                );
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking temperature anomaly alerts");
            }
        }

        private async Task CheckDeviceStatusAlertsAsync()
        {
            try
            {
                // Son 24 saatte veri gelmeyen cihazları kontrol et
                var oneDayAgo = DateTime.Now.AddDays(-1);
                var devicesWithoutData = await _context.Devices
                    .Where(d => d.IsActive && !d.EnergyConsumptions.Any(e => e.RecordedAt >= oneDayAgo))
                    .ToListAsync();

                foreach (var device in devicesWithoutData)
                {
                    var existingAlert = await _context.Alerts
                        .FirstOrDefaultAsync(a => a.DeviceId == device.Id && 
                                                 a.AlertType == "DeviceOffline" && 
                                                 a.CreatedAt >= oneDayAgo);

                    if (existingAlert == null)
                    {
                        await CreateAlertAsync(
                            device.UserId,
                            "Cihaz Çevrimdışı",
                            $"{device.DeviceName} cihazından son 24 saatte veri alınamadı. Cihaz durumunu kontrol edin.",
                            "DeviceOffline",
                            "Medium",
                            device.Id
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking device status alerts");
            }
        }

        private async Task CheckPowerFactorAlertsAsync()
        {
            try
            {
                var oneHourAgo = DateTime.Now.AddHours(-1);
                var recentConsumptions = await _context.EnergyConsumptions
                    .Where(e => e.RecordedAt >= oneHourAgo && e.PowerFactor < 0.8)
                    .Include(e => e.Device)
                    .ToListAsync();

                foreach (var consumption in recentConsumptions)
                {
                    if (consumption.DeviceId != null)
                    {
                        var device = consumption.Device;
                        if (device != null)
                        {
                            var existingAlert = await _context.Alerts
                                .FirstOrDefaultAsync(a => a.DeviceId == device.Id && 
                                                         a.AlertType == "LowPowerFactor" && 
                                                         a.CreatedAt >= oneHourAgo);

                            if (existingAlert == null)
                            {
                                await CreateAlertAsync(
                                    device.UserId,
                                    "Düşük Güç Faktörü",
                                    $"{device.DeviceName} cihazında düşük güç faktörü tespit edildi. Mevcut değer: {consumption.PowerFactor:F2}",
                                    "LowPowerFactor",
                                    "Medium",
                                    device.Id,
                                    JsonSerializer.Serialize(new { PowerFactor = consumption.PowerFactor })
                                );
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking power factor alerts");
            }
        }

        private async Task CheckVoltageAnomalyAlertsAsync()
        {
            try
            {
                var oneHourAgo = DateTime.Now.AddHours(-1);
                var recentConsumptions = await _context.EnergyConsumptions
                    .Where(e => e.RecordedAt >= oneHourAgo && (e.Voltage < 200 || e.Voltage > 250))
                    .Include(e => e.Device)
                    .ToListAsync();

                foreach (var consumption in recentConsumptions)
                {
                    if (consumption.DeviceId != null)
                    {
                        var device = consumption.Device;
                        if (device != null)
                        {
                            var existingAlert = await _context.Alerts
                                .FirstOrDefaultAsync(a => a.DeviceId == device.Id && 
                                                         a.AlertType == "VoltageAnomaly" && 
                                                         a.CreatedAt >= oneHourAgo);

                            if (existingAlert == null)
                            {
                                await CreateAlertAsync(
                                    device.UserId,
                                    "Voltaj Anomalisi",
                                    $"{device.DeviceName} cihazında anormal voltaj tespit edildi. Mevcut voltaj: {consumption.Voltage:F2}V",
                                    "VoltageAnomaly",
                                    "High",
                                    device.Id,
                                    JsonSerializer.Serialize(new { Voltage = consumption.Voltage })
                                );
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking voltage anomaly alerts");
            }
        }

        private async Task SendEmailNotificationAsync(Alert alert)
        {
            try
            {
                // E-posta gönderme simülasyonu
                _logger.LogInformation("Email notification sent for alert {AlertId} to user {UserId}", 
                    alert.Id, alert.UserId);
                
                // Gerçek uygulamada burada e-posta servisi kullanılır
                // await _emailService.SendAsync(alert.User.Email, alert.Title, alert.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email notification for alert {AlertId}", alert.Id);
            }
        }

        private async Task SendSMSNotificationAsync(Alert alert)
        {
            try
            {
                // SMS gönderme simülasyonu
                _logger.LogInformation("SMS notification sent for alert {AlertId} to user {UserId}", 
                    alert.Id, alert.UserId);
                
                // Gerçek uygulamada burada SMS servisi kullanılır
                // await _smsService.SendAsync(alert.User.PhoneNumber, alert.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending SMS notification for alert {AlertId}", alert.Id);
            }
        }
    }
}
