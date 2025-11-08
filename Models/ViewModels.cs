using System;
using System.ComponentModel.DataAnnotations;
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
        public double EstimatedMonthlyEnergy { get; set; } // kWh
        public double PotentialEnergySavings { get; set; } // kWh
        public double PotentialCostSavings { get; set; } // TL
        public double CarbonIntensity { get; set; } // kg CO2 / kWh
        public List<Alert> RecentAlerts { get; set; } = new List<Alert>();
        public List<Device> Devices { get; set; } = new List<Device>();
    }

    public class AccountSettingsViewModel
    {
        public string? UserId { get; set; }
        
        [Required(ErrorMessage = "Ad zorunludur.")]
        [StringLength(64)]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Soyad zorunludur.")]
        [StringLength(64)]
        public string LastName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Phone]
        public string? PhoneNumber { get; set; }

        [StringLength(256)]
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public double GasThreshold { get; set; }
        public bool AutoFanEnabled { get; set; }
        public double TemperatureThreshold { get; set; }
        public bool TemperatureAutoFanEnabled { get; set; }
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

    public class BillingSummaryViewModel
    {
        public double DailyEnergy { get; set; }
        public double DailyCost { get; set; }
        public double EstimatedMonthlyEnergy { get; set; }
        public double EstimatedMonthlyCost { get; set; }
        public double PotentialMonthlySavings { get; set; }
        public IReadOnlyList<Device> TopConsumers { get; set; } = Array.Empty<Device>();
    }

    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Ad zorunludur.")]
        [StringLength(64)]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Soyad zorunludur.")]
        [StringLength(64)]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "E-posta zorunludur.")]
        [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
        public string Email { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Geçerli bir telefon numarası giriniz.")]
        public string? PhoneNumber { get; set; }

        [StringLength(256)]
        public string? Address { get; set; }

        [Required(ErrorMessage = "Şifre zorunludur.")]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Şifre en az 8 karakter olmalıdır.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre tekrar zorunludur.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Şifreler eşleşmiyor.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class LoginViewModel
    {
        [Required(ErrorMessage = "E-posta zorunludur.")]
        [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre zorunludur.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }

        public string? ReturnUrl { get; set; }
    }
}






