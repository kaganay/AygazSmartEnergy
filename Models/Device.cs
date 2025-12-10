using System.ComponentModel.DataAnnotations;

// Enerji izleme cihazı: kimlik, konum, sınırlar ve ilişkiler.
namespace AygazSmartEnergy.Models
{
    public class Device
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string DeviceName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Required]
        [StringLength(50)]
        public string DeviceType { get; set; } = string.Empty; // Pump, Motor, Lighting, etc.

        [Required]
        [StringLength(100)]
        public string Location { get; set; } = string.Empty;

        [StringLength(50)]
        public string? SerialNumber { get; set; }

        [StringLength(100)]
        public string? Model { get; set; }

        [StringLength(100)]
        public string? Manufacturer { get; set; }

        public double MaxPowerConsumption { get; set; } // Watts
        public double MinPowerConsumption { get; set; } // Watts

        public bool IsActive { get; set; } = true;
        public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastMaintenanceAt { get; set; }

        // Foreign Keys
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser User { get; set; } = null!;

        // Navigation properties
        public virtual ICollection<EnergyConsumption> EnergyConsumptions { get; set; } = new List<EnergyConsumption>();
        public virtual ICollection<SensorData> SensorDatas { get; set; } = new List<SensorData>();
    }
}
