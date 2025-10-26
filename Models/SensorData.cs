using System;
using System.ComponentModel.DataAnnotations;

namespace AygazSmartEnergy.Models
{
    public class SensorData
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string SensorName { get; set; } = string.Empty;

        [StringLength(50)]
        public string? SensorType { get; set; } // Temperature, Gas, Energy, etc.

        [Range(-50, 1000)]
        public double Temperature { get; set; }

        [Range(0, 100)]
        public double GasLevel { get; set; }

        [Range(0, 10000)]
        public double EnergyUsage { get; set; }

        [Range(0, 500)]
        public double Voltage { get; set; }

        [Range(0, 100)]
        public double Current { get; set; }

        [Range(0, 1)]
        public double PowerFactor { get; set; } = 1.0;

        [StringLength(100)]
        public string? Location { get; set; }

        [StringLength(50)]
        public string? Status { get; set; } = "Active"; // Active, Inactive, Error

        public DateTime RecordedAt { get; set; } = DateTime.Now;

        // Foreign Keys
        public int? DeviceId { get; set; }
        public virtual Device? Device { get; set; }

        // Additional metadata
        [StringLength(500)]
        public string? RawData { get; set; } // JSON format for additional sensor data

        [StringLength(100)]
        public string? FirmwareVersion { get; set; }

        [StringLength(50)]
        public string? SignalStrength { get; set; } // For wireless sensors
    }
}
