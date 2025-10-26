using AygazSmartEnergy.Models;

namespace AygazSmartEnergy.Services
{
    public interface IAlertService
    {
        Task CreateAlertAsync(string userId, string title, string message, string alertType, string severity, int? deviceId = null, string? additionalData = null);
        Task<List<Alert>> GetUserAlertsAsync(string userId, bool includeResolved = false);
        Task<Alert?> GetAlertByIdAsync(int alertId);
        Task MarkAlertAsReadAsync(int alertId);
        Task ResolveAlertAsync(int alertId, string actionTaken);
        Task DeleteAlertAsync(int alertId);
        Task CheckAndCreateAlertsAsync();
        Task SendAlertNotificationAsync(int alertId, string notificationType);
    }

    public class AlertRule
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Condition { get; set; } = string.Empty; // JSON condition
        public string AlertType { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public int? DeviceId { get; set; }
        public int? UserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class AlertCondition
    {
        public string Field { get; set; } = string.Empty; // PowerConsumption, Temperature, etc.
        public string Operator { get; set; } = string.Empty; // >, <, >=, <=, ==, !=
        public double Value { get; set; }
        public string TimeWindow { get; set; } = string.Empty; // 1h, 24h, 7d, etc.
        public int MinOccurrences { get; set; } = 1;
    }
}
