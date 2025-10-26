using System.ComponentModel.DataAnnotations;

namespace AygazSmartEnergy.Models
{
    public class EnergyConsumption
    {
        public int Id { get; set; }

        [Required]
        public double PowerConsumption { get; set; } // Watts

        [Required]
        public double EnergyUsed { get; set; } // kWh

        [Required]
        public double Voltage { get; set; } // Volts

        [Required]
        public double Current { get; set; } // Amperes

        [Required]
        public double PowerFactor { get; set; } // 0-1

        [Required]
        public double Temperature { get; set; } // Celsius

        [Required]
        public double GasLevel { get; set; } // Percentage

        [Required]
        public DateTime RecordedAt { get; set; } = DateTime.Now;

        [StringLength(50)]
        public string? WeatherCondition { get; set; } // Sunny, Cloudy, Rainy, etc.

        [StringLength(100)]
        public string? Notes { get; set; }

        public double? Humidity { get; set; } // Percentage
        public string? ConsumptionInterval { get; set; } = "Hourly"; // Hourly, Daily, Monthly

        // Foreign Keys
        public int? DeviceId { get; set; }
        public virtual Device? Device { get; set; }

        // Calculated properties
        public double CostPerHour => EnergyUsed * 0.5; // Assuming 0.5 TL per kWh
        public double CarbonFootprint => EnergyUsed * 0.4; // kg CO2 per kWh
    }
}
