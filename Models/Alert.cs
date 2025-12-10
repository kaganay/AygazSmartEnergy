using System.ComponentModel.DataAnnotations;

// Uyarı kaydı: tip/severity, zaman damgaları, kullanıcı/cihaz ilişkisi.
namespace AygazSmartEnergy.Models
{
    public class Alert
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(1000)]
        public string Message { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string AlertType { get; set; } = string.Empty; // Warning, Critical, Info, Success

        [Required]
        [StringLength(50)]
        public string Severity { get; set; } = string.Empty; // Low, Medium, High, Critical

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReadAt { get; set; }
        public DateTime? ResolvedAt { get; set; }

        public bool IsRead { get; set; } = false;
        public bool IsResolved { get; set; } = false;

        [StringLength(500)]
        public string? ActionTaken { get; set; }

        [StringLength(1000)]
        public string? AdditionalData { get; set; } // JSON data for additional context

        // Foreign Keys
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser User { get; set; } = null!;

        public int? DeviceId { get; set; }
        public virtual Device? Device { get; set; }

        // Navigation properties
        public virtual ICollection<AlertNotification> AlertNotifications { get; set; } = new List<AlertNotification>();
    }

    public class AlertNotification
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string NotificationType { get; set; } = string.Empty; // Email, SMS, Push, InApp

        [Required]
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public bool IsDelivered { get; set; } = false;
        public DateTime? DeliveredAt { get; set; }

        [StringLength(500)]
        public string? ErrorMessage { get; set; }

        // Foreign Keys
        public int AlertId { get; set; }
        public virtual Alert Alert { get; set; } = null!;
    }
}
