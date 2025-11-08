using Microsoft.EntityFrameworkCore;
using AygazSmartEnergy.Data;
using AygazSmartEnergy.Models;

namespace AygazSmartEnergy.Services
{
    public class EnergyAnalysisService : IEnergyAnalysisService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<EnergyAnalysisService> _logger;

        public EnergyAnalysisService(AppDbContext context, ILogger<EnergyAnalysisService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<EnergyConsumptionSummary> GetConsumptionSummaryAsync(int deviceId, DateTime startDate, DateTime endDate)
        {
            try
            {
                var consumptions = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId == deviceId && 
                               e.RecordedAt >= startDate && 
                               e.RecordedAt <= endDate)
                    .ToListAsync();

                if (!consumptions.Any())
                {
                    return new EnergyConsumptionSummary();
                }

                var summary = new EnergyConsumptionSummary
                {
                    TotalEnergyConsumed = consumptions.Sum(e => e.EnergyUsed),
                    AveragePowerConsumption = consumptions.Average(e => e.PowerConsumption),
                    PeakPowerConsumption = consumptions.Max(e => e.PowerConsumption),
                    MinPowerConsumption = consumptions.Min(e => e.PowerConsumption),
                    TotalCost = consumptions.Sum(e => e.CostPerHour),
                    CarbonFootprint = consumptions.Sum(e => e.CarbonFootprint),
                    TotalRecords = consumptions.Count
                };

                var peakRecord = consumptions.OrderByDescending(e => e.PowerConsumption).First();
                var minRecord = consumptions.OrderBy(e => e.PowerConsumption).First();

                summary.PeakHour = peakRecord.RecordedAt;
                summary.MinHour = minRecord.RecordedAt;

                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating consumption summary for device {DeviceId}", deviceId);
                throw;
            }
        }

        public async Task<List<EnergyTrend>> GetEnergyTrendsAsync(int deviceId, int days)
        {
            try
            {
                var startDate = DateTime.Now.AddDays(-days);
                var endDate = DateTime.Now;

                var consumptions = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId == deviceId && 
                               e.RecordedAt >= startDate && 
                               e.RecordedAt <= endDate)
                    .OrderBy(e => e.RecordedAt)
                    .ToListAsync();

                var trends = consumptions
                    .GroupBy(e => e.RecordedAt.Date)
                    .Select(g => new EnergyTrend
                    {
                        Date = g.Key,
                        EnergyConsumed = g.Sum(e => e.EnergyUsed),
                        AveragePower = g.Average(e => e.PowerConsumption),
                        Cost = g.Sum(e => e.CostPerHour),
                        Temperature = g.Average(e => e.Temperature),
                        WeatherCondition = g.FirstOrDefault()?.WeatherCondition
                    })
                    .OrderBy(t => t.Date)
                    .ToList();

                return trends;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating energy trends for device {DeviceId}", deviceId);
                throw;
            }
        }

        public async Task<EnergyEfficiencyReport> GetEfficiencyReportAsync(int deviceId, int days)
        {
            try
            {
                var startDate = DateTime.Now.AddDays(-days);
                var endDate = DateTime.Now;

                var consumptions = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId == deviceId && 
                               e.RecordedAt >= startDate && 
                               e.RecordedAt <= endDate)
                    .ToListAsync();

                if (!consumptions.Any())
                {
                    return new EnergyEfficiencyReport();
                }

                var device = await _context.Devices.FindAsync(deviceId);
                if (device == null)
                {
                    return new EnergyEfficiencyReport();
                }

                var averagePower = consumptions.Average(e => e.PowerConsumption);
                var maxPower = device.MaxPowerConsumption;
                var efficiencyScore = Math.Max(0, 100 - ((averagePower / maxPower) * 100));

                var report = new EnergyEfficiencyReport
                {
                    EfficiencyScore = Math.Round(efficiencyScore, 2),
                    EfficiencyLevel = GetEfficiencyLevel(efficiencyScore),
                    ImprovementAreas = GetImprovementAreas(consumptions, device),
                    PotentialSavings = CalculatePotentialSavings(consumptions, efficiencyScore),
                    EnergyWastePercentage = Math.Round(100 - efficiencyScore, 2)
                };

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating efficiency report for device {DeviceId}", deviceId);
                throw;
            }
        }

        public async Task<List<AnomalyDetection>> DetectAnomaliesAsync(int deviceId, int days)
        {
            try
            {
                var startDate = DateTime.Now.AddDays(-days);
                var endDate = DateTime.Now;

                var consumptions = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId == deviceId && 
                               e.RecordedAt >= startDate && 
                               e.RecordedAt <= endDate)
                    .OrderBy(e => e.RecordedAt)
                    .ToListAsync();

                if (consumptions.Count < 10) // Yeterli veri yoksa anomali tespiti yapma
                {
                    return new List<AnomalyDetection>();
                }

                var anomalies = new List<AnomalyDetection>();

                // Yüksek tüketim anomali tespiti
                var avgPower = consumptions.Average(e => e.PowerConsumption);
                var powerStdDev = CalculateStandardDeviation(consumptions.Select(e => e.PowerConsumption));
                var powerThreshold = avgPower + (2 * powerStdDev);

                var highConsumptionAnomalies = consumptions
                    .Where(e => e.PowerConsumption > powerThreshold)
                    .Select(e => new AnomalyDetection
                    {
                        DetectedAt = e.RecordedAt,
                        AnomalyType = "HighConsumption",
                        Description = $"Aşırı yüksek enerji tüketimi tespit edildi: {e.PowerConsumption:F2}W",
                        Severity = Math.Min(1.0, (e.PowerConsumption - avgPower) / (avgPower * 0.5)),
                        NormalValue = avgPower,
                        ActualValue = e.PowerConsumption,
                        Recommendation = "Cihazın bakımını kontrol edin veya kullanım saatlerini gözden geçirin."
                    });

                anomalies.AddRange(highConsumptionAnomalies);

                // Sıcaklık anomali tespiti
                var avgTemp = consumptions.Average(e => e.Temperature);
                var tempStdDev = CalculateStandardDeviation(consumptions.Select(e => e.Temperature));
                var tempThreshold = avgTemp + (2 * tempStdDev);

                var tempAnomalies = consumptions
                    .Where(e => e.Temperature > tempThreshold)
                    .Select(e => new AnomalyDetection
                    {
                        DetectedAt = e.RecordedAt,
                        AnomalyType = "TemperatureSpike",
                        Description = $"Anormal sıcaklık artışı tespit edildi: {e.Temperature:F2}°C",
                        Severity = Math.Min(1.0, (e.Temperature - avgTemp) / (avgTemp * 0.3)),
                        NormalValue = avgTemp,
                        ActualValue = e.Temperature,
                        Recommendation = "Cihazın soğutma sistemini kontrol edin."
                    });

                anomalies.AddRange(tempAnomalies);

                return anomalies.OrderByDescending(a => a.Severity).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting anomalies for device {DeviceId}", deviceId);
                throw;
            }
        }

        public async Task<EnergySavingsRecommendation> GetSavingsRecommendationsAsync(int deviceId)
        {
            try
            {
                var device = await _context.Devices.FindAsync(deviceId);
                if (device == null)
                {
                    return new EnergySavingsRecommendation();
                }

                var trends = await GetEnergyTrendsAsync(deviceId, 30);
                var efficiencyReport = await GetEfficiencyReportAsync(deviceId, 30);

                var recommendations = new EnergySavingsRecommendation();

                // Enerji verimliliği önerileri
                if (efficiencyReport.EfficiencyScore < 70)
                {
                    recommendations.Recommendations.Add(new RecommendationItem
                    {
                        Title = "Enerji Verimliliği İyileştirmesi",
                        Description = "Cihazın enerji verimliliği düşük. Bakım ve optimizasyon gerekli.",
                        Category = "Efficiency",
                        PotentialSavings = efficiencyReport.PotentialSavings * 0.3,
                        EnergyReduction = 50, // kWh per month
                        Priority = "High",
                        ImplementationDifficulty = "Medium",
                        Steps = new List<string>
                        {
                            "Cihazın periyodik bakımını yapın",
                            "Eski parçaları yenileyin",
                            "Kullanım saatlerini optimize edin"
                        }
                    });
                }

                // Zaman bazlı optimizasyon önerileri
                var peakHours = trends.Where(t => t.EnergyConsumed > trends.Average(tr => tr.EnergyConsumed) * 1.2).ToList();
                if (peakHours.Any())
                {
                    recommendations.Recommendations.Add(new RecommendationItem
                    {
                        Title = "Zaman Bazlı Kullanım Optimizasyonu",
                        Description = "Pik saatlerde enerji tüketimi yüksek. Kullanım saatlerini değiştirin.",
                        Category = "Schedule",
                        PotentialSavings = efficiencyReport.PotentialSavings * 0.2,
                        EnergyReduction = 30,
                        Priority = "Medium",
                        ImplementationDifficulty = "Easy",
                        Steps = new List<string>
                        {
                            "Pik saatlerde kullanımı azaltın",
                            "Gece saatlerinde çalıştırın",
                            "Hafta sonu kullanımını artırın"
                        }
                    });
                }

                // Sıcaklık optimizasyonu
                var avgTemp = trends.Average(t => t.Temperature);
                if (avgTemp > 30)
                {
                    recommendations.Recommendations.Add(new RecommendationItem
                    {
                        Title = "Sıcaklık Kontrolü",
                        Description = "Yüksek sıcaklık enerji tüketimini artırıyor. Soğutma sistemini iyileştirin.",
                        Category = "Temperature",
                        PotentialSavings = efficiencyReport.PotentialSavings * 0.15,
                        EnergyReduction = 20,
                        Priority = "Medium",
                        ImplementationDifficulty = "Hard",
                        Steps = new List<string>
                        {
                            "Soğutma sistemini kontrol edin",
                            "Havalandırma iyileştirin",
                            "Gölgelendirme ekleyin"
                        }
                    });
                }

                recommendations.TotalPotentialSavings = recommendations.Recommendations.Sum(r => r.PotentialSavings);
                recommendations.TotalEnergyReduction = recommendations.Recommendations.Sum(r => r.EnergyReduction);
                recommendations.CarbonReduction = recommendations.TotalEnergyReduction * 0.4; // kg CO2 per kWh

                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating savings recommendations for device {DeviceId}", deviceId);
                throw;
            }
        }

        public async Task<double> CalculateCarbonFootprintAsync(int deviceId, int days)
        {
            try
            {
                var startDate = DateTime.Now.AddDays(-days);
                var endDate = DateTime.Now;

                var totalEnergy = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId == deviceId && 
                               e.RecordedAt >= startDate && 
                               e.RecordedAt <= endDate)
                    .SumAsync(e => e.EnergyUsed);

                return totalEnergy * 0.4; // kg CO2 per kWh
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating carbon footprint for device {DeviceId}", deviceId);
                throw;
            }
        }

        public async Task<double> EstimateMonthlyBillAsync(int deviceId)
        {
            try
            {
                var trends = await GetEnergyTrendsAsync(deviceId, 30);
                if (!trends.Any())
                {
                    return 0;
                }

                var avgDailyCost = trends.Average(t => t.Cost);
                return avgDailyCost * 30; // Aylık tahmin
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error estimating monthly bill for device {DeviceId}", deviceId);
                throw;
            }
        }

        private string GetEfficiencyLevel(double score)
        {
            return score switch
            {
                >= 90 => "Excellent",
                >= 80 => "Good",
                >= 70 => "Average",
                >= 60 => "Below Average",
                _ => "Poor"
            };
        }

        private List<string> GetImprovementAreas(List<EnergyConsumption> consumptions, Device device)
        {
            var areas = new List<string>();

            var avgPower = consumptions.Average(e => e.PowerConsumption);
            var maxPower = device.MaxPowerConsumption;

            if (avgPower > maxPower * 0.8)
            {
                areas.Add("Yüksek güç tüketimi - cihaz kapasitesinin %80'ini aşıyor");
            }

            var avgPowerFactor = consumptions.Average(e => e.PowerFactor);
            if (avgPowerFactor < 0.8)
            {
                areas.Add("Düşük güç faktörü - reaktif güç kompanzasyonu gerekli");
            }

            var tempVariation = consumptions.Max(e => e.Temperature) - consumptions.Min(e => e.Temperature);
            if (tempVariation > 20)
            {
                areas.Add("Yüksek sıcaklık değişimi - termal stabilite sorunu");
            }

            return areas;
        }

        private double CalculatePotentialSavings(List<EnergyConsumption> consumptions, double efficiencyScore)
        {
            var avgCost = consumptions.Average(e => e.CostPerHour);
            var potentialImprovement = (100 - efficiencyScore) / 100;
            return avgCost * 24 * 30 * potentialImprovement; // Aylık potansiyel tasarruf
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

