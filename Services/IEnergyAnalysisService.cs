using AygazSmartEnergy.Models;

namespace AygazSmartEnergy.Services
{
    public interface IEnergyAnalysisService
    {
        Task<EnergyConsumptionSummary> GetConsumptionSummaryAsync(int deviceId, DateTime startDate, DateTime endDate);
        Task<List<EnergyTrend>> GetEnergyTrendsAsync(int deviceId, int days);
        Task<EnergyEfficiencyReport> GetEfficiencyReportAsync(int deviceId, int days);
        Task<List<AnomalyDetection>> DetectAnomaliesAsync(int deviceId, int days);
        Task<EnergySavingsRecommendation> GetSavingsRecommendationsAsync(int deviceId);
        Task<double> CalculateCarbonFootprintAsync(int deviceId, int days);
        Task<double> EstimateMonthlyBillAsync(int deviceId);
    }

    public class EnergyConsumptionSummary
    {
        public double TotalEnergyConsumed { get; set; } // kWh
        public double AveragePowerConsumption { get; set; } // Watts
        public double PeakPowerConsumption { get; set; } // Watts
        public double MinPowerConsumption { get; set; } // Watts
        public double TotalCost { get; set; } // TL
        public double CarbonFootprint { get; set; } // kg CO2
        public DateTime PeakHour { get; set; }
        public DateTime MinHour { get; set; }
        public int TotalRecords { get; set; }
    }

    public class EnergyTrend
    {
        public DateTime Date { get; set; }
        public double EnergyConsumed { get; set; } // kWh
        public double AveragePower { get; set; } // Watts
        public double Cost { get; set; } // TL
        public double Temperature { get; set; } // Celsius
        public string? WeatherCondition { get; set; }
    }

    public class EnergyEfficiencyReport
    {
        public double EfficiencyScore { get; set; } // 0-100
        public string EfficiencyLevel { get; set; } = string.Empty; // Excellent, Good, Average, Poor
        public List<string> ImprovementAreas { get; set; } = new List<string>();
        public double PotentialSavings { get; set; } // TL per month
        public double EnergyWastePercentage { get; set; } // %
    }

    public class AnomalyDetection
    {
        public DateTime DetectedAt { get; set; }
        public string AnomalyType { get; set; } = string.Empty; // HighConsumption, LowEfficiency, TemperatureSpike, etc.
        public string Description { get; set; } = string.Empty;
        public double Severity { get; set; } // 0-1
        public double NormalValue { get; set; }
        public double ActualValue { get; set; }
        public string Recommendation { get; set; } = string.Empty;
    }

    public class EnergySavingsRecommendation
    {
        public List<RecommendationItem> Recommendations { get; set; } = new List<RecommendationItem>();
        public double TotalPotentialSavings { get; set; } // TL per month
        public double TotalEnergyReduction { get; set; } // kWh per month
        public double CarbonReduction { get; set; } // kg CO2 per month
    }

    public class RecommendationItem
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // Equipment, Schedule, Maintenance, etc.
        public double PotentialSavings { get; set; } // TL per month
        public double EnergyReduction { get; set; } // kWh per month
        public string Priority { get; set; } = string.Empty; // High, Medium, Low
        public string ImplementationDifficulty { get; set; } = string.Empty; // Easy, Medium, Hard
        public List<string> Steps { get; set; } = new List<string>();
    }
}

