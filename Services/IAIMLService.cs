using AygazSmartEnergy.Models;

namespace AygazSmartEnergy.Services
{
    public interface IAIMLService
    {
        Task<EnergyPrediction> PredictEnergyConsumptionAsync(int deviceId, int daysAhead);
        Task<List<AnomalyDetection>> DetectAnomaliesWithMLAsync(int deviceId, int days);
        Task<EnergyOptimizationRecommendation> GetOptimizationRecommendationsAsync(int deviceId);
        Task<MaintenancePrediction> PredictMaintenanceNeedsAsync(int deviceId);
        Task<EnergyEfficiencyScore> CalculateEfficiencyScoreAsync(int deviceId, int days);
    }

    public class EnergyPrediction
    {
        public DateTime PredictionDate { get; set; }
        public double PredictedEnergyConsumption { get; set; } // kWh
        public double ConfidenceLevel { get; set; } // 0-1
        public double MinPrediction { get; set; }
        public double MaxPrediction { get; set; }
        public List<PredictionFactor> Factors { get; set; } = new List<PredictionFactor>();
    }

    public class PredictionFactor
    {
        public string FactorName { get; set; } = string.Empty;
        public double Impact { get; set; } // -1 to 1
        public string Description { get; set; } = string.Empty;
    }

    public class EnergyOptimizationRecommendation
    {
        public List<OptimizationAction> Actions { get; set; } = new List<OptimizationAction>();
        public double PotentialSavings { get; set; } // TL per month
        public double EnergyReduction { get; set; } // kWh per month
        public double CarbonReduction { get; set; } // kg CO2 per month
        public double ImplementationCost { get; set; } // TL
        public int PaybackPeriod { get; set; } // months
        public double TotalPotentialSavings { get; set; } // TL per month
        public double TotalEnergyReduction { get; set; } // kWh per month
    }

    public class OptimizationAction
    {
        public string ActionName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // Equipment, Schedule, Maintenance, etc.
        public double PotentialSavings { get; set; } // TL per month
        public double EnergyReduction { get; set; } // kWh per month
        public double ImplementationCost { get; set; } // TL
        public int PaybackPeriod { get; set; } // months
        public string Priority { get; set; } = string.Empty; // High, Medium, Low
        public List<string> Steps { get; set; } = new List<string>();
    }

    public class MaintenancePrediction
    {
        public DateTime PredictedMaintenanceDate { get; set; }
        public double UrgencyScore { get; set; } // 0-1
        public string MaintenanceType { get; set; } = string.Empty;
        public List<string> RecommendedActions { get; set; } = new List<string>();
        public double EstimatedCost { get; set; } // TL
        public string RiskLevel { get; set; } = string.Empty; // Low, Medium, High, Critical
    }

    public class EnergyEfficiencyScore
    {
        public double OverallScore { get; set; } // 0-100
        public string EfficiencyLevel { get; set; } = string.Empty; // Excellent, Good, Average, Poor
        public List<EfficiencyMetric> Metrics { get; set; } = new List<EfficiencyMetric>();
        public List<string> ImprovementAreas { get; set; } = new List<string>();
        public double BenchmarkComparison { get; set; } // Percentage compared to industry average
    }

    public class EfficiencyMetric
    {
        public string MetricName { get; set; } = string.Empty;
        public double Value { get; set; }
        public double Benchmark { get; set; }
        public double Score { get; set; } // 0-100
        public string Unit { get; set; } = string.Empty;
    }
}
