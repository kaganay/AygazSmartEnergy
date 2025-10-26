using Microsoft.EntityFrameworkCore;
using AygazSmartEnergy.Data;
using AygazSmartEnergy.Models;
using System.Text.Json;

namespace AygazSmartEnergy.Services
{
    public class AIMLService : IAIMLService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<AIMLService> _logger;
        private readonly HttpClient _httpClient;

        public AIMLService(AppDbContext context, ILogger<AIMLService> logger, HttpClient httpClient)
        {
            _context = context;
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<EnergyPrediction> PredictEnergyConsumptionAsync(int deviceId, int daysAhead)
        {
            try
            {
                // Son 30 günlük verileri al
                var historicalData = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId == deviceId)
                    .OrderBy(e => e.RecordedAt)
                    .Take(30)
                    .ToListAsync();

                if (!historicalData.Any())
                {
                    return new EnergyPrediction
                    {
                        PredictionDate = DateTime.Now.AddDays(daysAhead),
                        PredictedEnergyConsumption = 0,
                        ConfidenceLevel = 0
                    };
                }

                // Python ML servisine veri gönder
                var predictionRequest = new
                {
                    DeviceId = deviceId,
                    HistoricalData = historicalData.Select(e => new
                    {
                        Date = e.RecordedAt,
                        EnergyConsumption = e.EnergyUsed,
                        PowerConsumption = e.PowerConsumption,
                        Temperature = e.Temperature,
                        Voltage = e.Voltage,
                        Current = e.Current,
                        PowerFactor = e.PowerFactor
                    }).ToList(),
                    DaysAhead = daysAhead
                };

                var jsonContent = JsonSerializer.Serialize(predictionRequest);
                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("http://localhost:5000/predict-energy", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var prediction = JsonSerializer.Deserialize<EnergyPrediction>(responseContent);
                    return prediction ?? new EnergyPrediction();
                }
                else
                {
                    // Fallback: Basit trend analizi
                    return PerformSimplePrediction(historicalData, daysAhead);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error predicting energy consumption for device {DeviceId}", deviceId);
                return new EnergyPrediction
                {
                    PredictionDate = DateTime.Now.AddDays(daysAhead),
                    PredictedEnergyConsumption = 0,
                    ConfidenceLevel = 0
                };
            }
        }

        public async Task<List<AnomalyDetection>> DetectAnomaliesWithMLAsync(int deviceId, int days)
        {
            try
            {
                var startDate = DateTime.Now.AddDays(-days);
                var historicalData = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId == deviceId && e.RecordedAt >= startDate)
                    .OrderBy(e => e.RecordedAt)
                    .ToListAsync();

                if (historicalData.Count < 10)
                {
                    return new List<AnomalyDetection>();
                }

                // Python ML servisine veri gönder
                var anomalyRequest = new
                {
                    DeviceId = deviceId,
                    Data = historicalData.Select(e => new
                    {
                        Date = e.RecordedAt,
                        EnergyConsumption = e.EnergyUsed,
                        PowerConsumption = e.PowerConsumption,
                        Temperature = e.Temperature,
                        Voltage = e.Voltage,
                        Current = e.Current,
                        PowerFactor = e.PowerFactor
                    }).ToList()
                };

                var jsonContent = JsonSerializer.Serialize(anomalyRequest);
                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("http://localhost:5000/detect-anomalies", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var anomalies = JsonSerializer.Deserialize<List<AnomalyDetection>>(responseContent);
                    return anomalies ?? new List<AnomalyDetection>();
                }
                else
                {
                    // Fallback: Basit istatistiksel anomali tespiti
                    return PerformSimpleAnomalyDetection(historicalData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting anomalies with ML for device {DeviceId}", deviceId);
                return new List<AnomalyDetection>();
            }
        }

        public async Task<EnergyOptimizationRecommendation> GetOptimizationRecommendationsAsync(int deviceId)
        {
            try
            {
                var device = await _context.Devices.FindAsync(deviceId);
                if (device == null)
                {
                    return new EnergyOptimizationRecommendation();
                }

                // Son 30 günlük verileri al
                var historicalData = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId == deviceId)
                    .OrderByDescending(e => e.RecordedAt)
                    .Take(30)
                    .ToListAsync();

                if (!historicalData.Any())
                {
                    return new EnergyOptimizationRecommendation();
                }

                // Python ML servisine veri gönder
                var optimizationRequest = new
                {
                    DeviceId = deviceId,
                    DeviceType = device.DeviceType,
                    MaxPowerConsumption = device.MaxPowerConsumption,
                    MinPowerConsumption = device.MinPowerConsumption,
                    HistoricalData = historicalData.Select(e => new
                    {
                        Date = e.RecordedAt,
                        EnergyConsumption = e.EnergyUsed,
                        PowerConsumption = e.PowerConsumption,
                        Temperature = e.Temperature,
                        Voltage = e.Voltage,
                        Current = e.Current,
                        PowerFactor = e.PowerFactor,
                        WeatherCondition = e.WeatherCondition
                    }).ToList()
                };

                var jsonContent = JsonSerializer.Serialize(optimizationRequest);
                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("http://localhost:5000/optimize-energy", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var recommendations = JsonSerializer.Deserialize<EnergyOptimizationRecommendation>(responseContent);
                    return recommendations ?? new EnergyOptimizationRecommendation();
                }
                else
                {
                    // Fallback: Basit optimizasyon önerileri
                    return GenerateSimpleOptimizationRecommendations(device, historicalData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting optimization recommendations for device {DeviceId}", deviceId);
                return new EnergyOptimizationRecommendation();
            }
        }

        public async Task<MaintenancePrediction> PredictMaintenanceNeedsAsync(int deviceId)
        {
            try
            {
                var device = await _context.Devices.FindAsync(deviceId);
                if (device == null)
                {
                    return new MaintenancePrediction();
                }

                // Son 90 günlük verileri al
                var historicalData = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId == deviceId)
                    .OrderByDescending(e => e.RecordedAt)
                    .Take(90)
                    .ToListAsync();

                if (!historicalData.Any())
                {
                    return new MaintenancePrediction();
                }

                // Python ML servisine veri gönder
                var maintenanceRequest = new
                {
                    DeviceId = deviceId,
                    DeviceType = device.DeviceType,
                    InstallationDate = device.InstalledAt,
                    LastMaintenance = device.LastMaintenanceAt,
                    HistoricalData = historicalData.Select(e => new
                    {
                        Date = e.RecordedAt,
                        EnergyConsumption = e.EnergyUsed,
                        PowerConsumption = e.PowerConsumption,
                        Temperature = e.Temperature,
                        Voltage = e.Voltage,
                        Current = e.Current,
                        PowerFactor = e.PowerFactor
                    }).ToList()
                };

                var jsonContent = JsonSerializer.Serialize(maintenanceRequest);
                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("http://localhost:5000/predict-maintenance", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var prediction = JsonSerializer.Deserialize<MaintenancePrediction>(responseContent);
                    return prediction ?? new MaintenancePrediction();
                }
                else
                {
                    // Fallback: Basit bakım tahmini
                    return PerformSimpleMaintenancePrediction(device, historicalData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error predicting maintenance needs for device {DeviceId}", deviceId);
                return new MaintenancePrediction();
            }
        }

        public async Task<EnergyEfficiencyScore> CalculateEfficiencyScoreAsync(int deviceId, int days)
        {
            try
            {
                var device = await _context.Devices.FindAsync(deviceId);
                if (device == null)
                {
                    return new EnergyEfficiencyScore();
                }

                var startDate = DateTime.Now.AddDays(-days);
                var historicalData = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId == deviceId && e.RecordedAt >= startDate)
                    .ToListAsync();

                if (!historicalData.Any())
                {
                    return new EnergyEfficiencyScore();
                }

                // Python ML servisine veri gönder
                var efficiencyRequest = new
                {
                    DeviceId = deviceId,
                    DeviceType = device.DeviceType,
                    MaxPowerConsumption = device.MaxPowerConsumption,
                    MinPowerConsumption = device.MinPowerConsumption,
                    HistoricalData = historicalData.Select(e => new
                    {
                        Date = e.RecordedAt,
                        EnergyConsumption = e.EnergyUsed,
                        PowerConsumption = e.PowerConsumption,
                        Temperature = e.Temperature,
                        Voltage = e.Voltage,
                        Current = e.Current,
                        PowerFactor = e.PowerFactor
                    }).ToList()
                };

                var jsonContent = JsonSerializer.Serialize(efficiencyRequest);
                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("http://localhost:5000/calculate-efficiency", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var efficiency = JsonSerializer.Deserialize<EnergyEfficiencyScore>(responseContent);
                    return efficiency ?? new EnergyEfficiencyScore();
                }
                else
                {
                    // Fallback: Basit verimlilik hesaplama
                    return CalculateSimpleEfficiencyScore(device, historicalData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating efficiency score for device {DeviceId}", deviceId);
                return new EnergyEfficiencyScore();
            }
        }

        // Fallback metodları
        private EnergyPrediction PerformSimplePrediction(List<EnergyConsumption> historicalData, int daysAhead)
        {
            var avgConsumption = historicalData.Average(e => e.EnergyUsed);
            var trend = CalculateTrend(historicalData);
            
            return new EnergyPrediction
            {
                PredictionDate = DateTime.Now.AddDays(daysAhead),
                PredictedEnergyConsumption = avgConsumption + (trend * daysAhead),
                ConfidenceLevel = 0.7,
                MinPrediction = avgConsumption * 0.8,
                MaxPrediction = avgConsumption * 1.2,
                Factors = new List<PredictionFactor>
                {
                    new PredictionFactor
                    {
                        FactorName = "Tarihsel Trend",
                        Impact = trend,
                        Description = "Geçmiş verilere dayalı trend analizi"
                    }
                }
            };
        }

        private List<AnomalyDetection> PerformSimpleAnomalyDetection(List<EnergyConsumption> historicalData)
        {
            var anomalies = new List<AnomalyDetection>();
            var avgPower = historicalData.Average(e => e.PowerConsumption);
            var powerStdDev = CalculateStandardDeviation(historicalData.Select(e => e.PowerConsumption));

            foreach (var data in historicalData)
            {
                if (Math.Abs(data.PowerConsumption - avgPower) > 2 * powerStdDev)
                {
                    anomalies.Add(new AnomalyDetection
                    {
                        DetectedAt = data.RecordedAt,
                        AnomalyType = "HighConsumption",
                        Description = $"Aşırı yüksek enerji tüketimi: {data.PowerConsumption:F2}W",
                        Severity = Math.Min(1.0, Math.Abs(data.PowerConsumption - avgPower) / (avgPower * 0.5)),
                        NormalValue = avgPower,
                        ActualValue = data.PowerConsumption,
                        Recommendation = "Cihazın bakımını kontrol edin"
                    });
                }
            }

            return anomalies;
        }

        private EnergyOptimizationRecommendation GenerateSimpleOptimizationRecommendations(Device device, List<EnergyConsumption> historicalData)
        {
            var avgPower = historicalData.Average(e => e.PowerConsumption);
            var maxPower = device.MaxPowerConsumption;
            var efficiency = (avgPower / maxPower) * 100;

            var recommendations = new EnergyOptimizationRecommendation();

            if (efficiency < 70)
            {
                recommendations.Actions.Add(new OptimizationAction
                {
                    ActionName = "Enerji Verimliliği İyileştirmesi",
                    Description = "Cihazın enerji verimliliği düşük. Bakım ve optimizasyon gerekli.",
                    Category = "Efficiency",
                    PotentialSavings = 200,
                    EnergyReduction = 50,
                    ImplementationCost = 1000,
                    PaybackPeriod = 5,
                    Priority = "High",
                    Steps = new List<string>
                    {
                        "Cihazın periyodik bakımını yapın",
                        "Eski parçaları yenileyin",
                        "Kullanım saatlerini optimize edin"
                    }
                });
            }

            recommendations.TotalPotentialSavings = recommendations.Actions.Sum(a => a.PotentialSavings);
            recommendations.TotalEnergyReduction = recommendations.Actions.Sum(a => a.EnergyReduction);
            recommendations.CarbonReduction = recommendations.TotalEnergyReduction * 0.4;

            return recommendations;
        }

        private MaintenancePrediction PerformSimpleMaintenancePrediction(Device device, List<EnergyConsumption> historicalData)
        {
            var daysSinceInstallation = (DateTime.Now - device.InstalledAt).TotalDays;
            var daysSinceLastMaintenance = device.LastMaintenanceAt.HasValue 
                ? (DateTime.Now - device.LastMaintenanceAt.Value).TotalDays 
                : daysSinceInstallation;

            var urgencyScore = Math.Min(1.0, daysSinceLastMaintenance / 365); // Yıllık bakım varsayımı

            return new MaintenancePrediction
            {
                PredictedMaintenanceDate = DateTime.Now.AddDays(365 - daysSinceLastMaintenance),
                UrgencyScore = urgencyScore,
                MaintenanceType = "Rutin Bakım",
                RecommendedActions = new List<string>
                {
                    "Genel temizlik ve kontrol",
                    "Parça değişimi gerekebilir",
                    "Kalibrasyon kontrolü"
                },
                EstimatedCost = 500,
                RiskLevel = urgencyScore > 0.8 ? "High" : urgencyScore > 0.5 ? "Medium" : "Low"
            };
        }

        private EnergyEfficiencyScore CalculateSimpleEfficiencyScore(Device device, List<EnergyConsumption> historicalData)
        {
            var avgPower = historicalData.Average(e => e.PowerConsumption);
            var maxPower = device.MaxPowerConsumption;
            var efficiency = (avgPower / maxPower) * 100;

            return new EnergyEfficiencyScore
            {
                OverallScore = Math.Round(efficiency, 2),
                EfficiencyLevel = efficiency switch
                {
                    >= 90 => "Excellent",
                    >= 80 => "Good",
                    >= 70 => "Average",
                    >= 60 => "Below Average",
                    _ => "Poor"
                },
                Metrics = new List<EfficiencyMetric>
                {
                    new EfficiencyMetric
                    {
                        MetricName = "Güç Verimliliği",
                        Value = efficiency,
                        Benchmark = 85,
                        Score = Math.Min(100, efficiency),
                        Unit = "%"
                    }
                },
                ImprovementAreas = efficiency < 80 ? new List<string> { "Enerji verimliliği iyileştirilmeli" } : new List<string>(),
                BenchmarkComparison = efficiency - 85
            };
        }

        private double CalculateTrend(List<EnergyConsumption> data)
        {
            if (data.Count < 2) return 0;

            var firstHalf = data.Take(data.Count / 2).Average(e => e.EnergyUsed);
            var secondHalf = data.Skip(data.Count / 2).Average(e => e.EnergyUsed);
            
            return (secondHalf - firstHalf) / (data.Count / 2);
        }

        private double CalculateStandardDeviation(IEnumerable<double> values)
        {
            var valueList = values.ToList();
            if (valueList.Count < 2) return 0;

            var mean = valueList.Average();
            var sumOfSquares = valueList.Sum(x => Math.Pow(x - mean, 2));
            return Math.Sqrt(sumOfSquares / (valueList.Count - 1));
        }
    }
}
