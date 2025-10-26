using AygazSmartEnergy.Models;

namespace AygazSmartEnergy.Models
{
    public class DashboardViewModel
    {
        public int TotalDevices { get; set; }
        public int ActiveDevices { get; set; }
        public double TotalEnergyConsumed { get; set; } // kWh
        public double TotalCost { get; set; } // TL
        public double TotalCarbonFootprint { get; set; } // kg CO2
        public List<Alert> RecentAlerts { get; set; } = new List<Alert>();
        public List<Device> Devices { get; set; } = new List<Device>();
    }

    public class DeviceDetailsViewModel
    {
        public Device Device { get; set; } = null!;
        public List<EnergyTrend> EnergyTrends { get; set; } = new List<EnergyTrend>();
        public EnergyEfficiencyReport EfficiencyReport { get; set; } = new EnergyEfficiencyReport();
        public List<AnomalyDetection> Anomalies { get; set; } = new List<AnomalyDetection>();
        public EnergySavingsRecommendation Recommendations { get; set; } = new EnergySavingsRecommendation();
    }

    public class EnergyAnalysisViewModel
    {
        public Device Device { get; set; } = null!;
        public EnergyConsumptionSummary Summary { get; set; } = new EnergyConsumptionSummary();
        public List<EnergyTrend> Trends { get; set; } = new List<EnergyTrend>();
        public EnergyEfficiencyReport EfficiencyReport { get; set; } = new EnergyEfficiencyReport();
        public List<AnomalyDetection> Anomalies { get; set; } = new List<AnomalyDetection>();
        public EnergySavingsRecommendation Recommendations { get; set; } = new EnergySavingsRecommendation();
        public double CarbonFootprint { get; set; }
        public double EstimatedMonthlyBill { get; set; }
        public int AnalysisPeriod { get; set; }
    }

    // Helper classes for analysis
    public class EnergyTrend
    {
        public DateTime Date { get; set; }
        public double EnergyUsed { get; set; }
        public double Cost { get; set; }
        public double CarbonFootprint { get; set; }
    }

    public class EnergyEfficiencyReport
    {
        public double AverageEfficiency { get; set; }
        public double PeakEfficiency { get; set; }
        public double LowEfficiency { get; set; }
        public string EfficiencyGrade { get; set; } = string.Empty;
    }

    public class AnomalyDetection
    {
        public DateTime DetectedAt { get; set; }
        public string AnomalyType { get; set; } = string.Empty;
        public double Severity { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public class EnergySavingsRecommendation
    {
        public List<OptimizationAction> Actions { get; set; } = new List<OptimizationAction>();
        public double PotentialSavings { get; set; }
        public double EnergyReduction { get; set; }
        public double CarbonReduction { get; set; }
    }

    public class OptimizationAction
    {
        public string ActionName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double PotentialSavings { get; set; }
        public double EnergyReduction { get; set; }
        public double ImplementationCost { get; set; }
        public int PaybackPeriod { get; set; }
    }

    public class EnergyConsumptionSummary
    {
        public double TotalEnergyUsed { get; set; }
        public double AverageDailyConsumption { get; set; }
        public double PeakConsumption { get; set; }
        public double LowConsumption { get; set; }
        public double TotalCost { get; set; }
        public double TotalCarbonFootprint { get; set; }
    }
}
