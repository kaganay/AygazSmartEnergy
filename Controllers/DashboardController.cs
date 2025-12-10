using ModelsNS = AygazSmartEnergy.Models;    // Models namespace için alias (uzun isimlerden kaçınmak için)
using Microsoft.AspNetCore.Mvc;              // Controller, ViewResult
using Microsoft.EntityFrameworkCore;        // EF Core ORM (Include, ToListAsync vb.)
using Microsoft.AspNetCore.Authorization;    // [Authorize] attribute için
using AygazSmartEnergy.Data;                 // AppDbContext
using AygazSmartEnergy.Services;             // IEnergyAnalysisService, IAIMLService
using AygazSmartEnergy.Models;              // Entity modelleri

// Dashboard: özet veriler, cihaz listesi, uyarılar ve tahmin ekranları.
namespace AygazSmartEnergy.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IEnergyAnalysisService _energyAnalysisService;
        private readonly IAIMLService _aiMlService;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            AppDbContext context,
            IEnergyAnalysisService energyAnalysisService,
            IAIMLService aiMlService,
            ILogger<DashboardController> logger)
        {
            _context = context;
            _energyAnalysisService = energyAnalysisService;
            _aiMlService = aiMlService;
            _logger = logger;
        }

        /// <summary>
        /// GET /Dashboard/Index
        /// Ana dashboard sayfası: Özet istatistikler, aktif cihazlar, son uyarılar
        /// Gösterilen Bilgiler:
        /// - Toplam cihaz sayısı, aktif cihaz sayısı
        /// - Son 24 saatlik toplam enerji tüketimi, maliyet, karbon ayak izi
        /// - Tahmini aylık enerji tüketimi ve potansiyel tasarruf
        /// - Son 5 uyarı (çözülmemiş)
        /// - Tüm aktif cihazların listesi
        /// </summary>
        public async Task<IActionResult> Index()
        {
            try
            {
                // Tüm aktif cihazları göster (UserId filtresi kaldırıldı)
                // Performans için AsNoTracking ve sadece gerekli alanları çek
                var devices = await _context.Devices
                    .Where(d => d.IsActive)
                    .AsNoTracking()
                    .OrderByDescending(d => d.InstalledAt)
                    .ToListAsync();

                var totalDevices = devices.Count;
                var activeDevices = devices.Count(d => d.IsActive);

                // Son 24 saatlik veriler - performans için AsNoTracking
                var last24Hours = DateTime.UtcNow.AddHours(-24);
                var deviceIds = devices.Select(d => d.Id).ToList();
                var recentConsumptions = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId.HasValue && deviceIds.Contains(e.DeviceId.Value) &&
                                e.RecordedAt >= last24Hours)
                    .AsNoTracking()
                    .ToListAsync();
                
                // Her cihaz için son enerji tüketimini ayrı sorgu ile al (performans için)
                foreach (var device in devices)
                {
                    var lastConsumption = await _context.EnergyConsumptions
                        .Where(e => e.DeviceId == device.Id)
                        .AsNoTracking()
                        .OrderByDescending(e => e.RecordedAt)
                        .FirstOrDefaultAsync();
                    
                    if (lastConsumption != null)
                    {
                        device.EnergyConsumptions = new List<EnergyConsumption> { lastConsumption };
                    }
                }

                var totalEnergyConsumed = recentConsumptions.Sum(e => e.EnergyUsed);
                var totalCost = recentConsumptions.Sum(e => e.CostPerHour);
                var totalCarbonFootprint = recentConsumptions.Sum(e => e.CarbonFootprint);

                var estimatedMonthlyEnergy = totalEnergyConsumed * 30; // kaba tahmin
                var potentialEnergySavings = estimatedMonthlyEnergy * 0.15; // %15 iyileştirme hedefi
                var averageCostPerKwh = totalEnergyConsumed > 0 ? totalCost / totalEnergyConsumed : 0;
                var potentialCostSavings = potentialEnergySavings * averageCostPerKwh;
                var carbonIntensity = totalEnergyConsumed > 0 ? totalCarbonFootprint / totalEnergyConsumed : 0;

                // Son uyarılar (tüm çözülmemiş uyarılar) - performans için AsNoTracking
                var recentAlerts = await _context.Alerts
                    .Include(a => a.Device)              // Cihaz bilgisini de getir (Include önce gelmeli)
                    .Where(a => !a.IsResolved)           // Çözülmemiş uyarılar
                    .AsNoTracking()                      // Change tracking kapalı (performans için)
                    .OrderByDescending(a => a.CreatedAt) // En yeni uyarılar önce
                    .Take(5)                             // İlk 5 kayıt
                    .ToListAsync();

                var dashboardData = new DashboardViewModel
                {
                    TotalDevices = totalDevices,
                    ActiveDevices = activeDevices,
                    TotalEnergyConsumed = Math.Round(totalEnergyConsumed, 2),
                    TotalCost = Math.Round(totalCost, 2),
                    TotalCarbonFootprint = Math.Round(totalCarbonFootprint, 2),
                    EstimatedMonthlyEnergy = Math.Round(estimatedMonthlyEnergy, 2),
                    PotentialEnergySavings = Math.Round(potentialEnergySavings, 2),
                    PotentialCostSavings = Math.Round(potentialCostSavings, 2),
                    CarbonIntensity = Math.Round(carbonIntensity, 3),
                    RecentAlerts = recentAlerts,
                    Devices = devices
                };

                return View(dashboardData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading dashboard data");
                return View(new DashboardViewModel());
            }
        }

        /// <summary>
        /// Cihaz detay sayfası
        /// </summary>
        public async Task<IActionResult> DeviceDetails(int id)
        {
            try
            {
                // 🔹 Cihazı bul - performans için AsNoTracking
                var device = await _context.Devices
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == id);

                if (device == null)
                    return NotFound();

                // 🔹 En son verileri ayrı sorgularla al (Include içinde OrderBy çalışmaz)
                // Enerji tüketimleri: En son 30 kayıt, tarih sırasına göre - performans için AsNoTracking
                var latestConsumptions = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId == id)
                    .AsNoTracking()
                    .OrderByDescending(e => e.RecordedAt)
                    .Take(30)
                    .ToListAsync();

                // Sensör verileri: En son 50 kayıt, tarih sırasına göre - performans için AsNoTracking
                var latestSensorDatas = await _context.SensorDatas
                    .Where(s => s.DeviceId == id)
                    .AsNoTracking()
                    .OrderByDescending(s => s.RecordedAt)
                    .Take(50)
                    .ToListAsync();

                // 🔹 Device nesnesine en son verileri ata (navigation property'leri güncelle)
                device.EnergyConsumptions = latestConsumptions;
                device.SensorDatas = latestSensorDatas;

                // Geçici placeholder veriler
                var trends = new List<ModelsNS.EnergyTrend>();
                var efficiencyReport = new ModelsNS.EnergyEfficiencyReport();
                var anomalies = new List<ModelsNS.AnomalyDetection>();
                var recommendations = new ModelsNS.EnergySavingsRecommendation();

                var deviceDetails = new DeviceDetailsViewModel
                {
                    Device = device,
                    EnergyTrends = trends,
                    EfficiencyReport = efficiencyReport,
                    Anomalies = anomalies,
                    Recommendations = recommendations
                };

                return View(deviceDetails);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading device details for device {DeviceId}", id);
                return NotFound();
            }
        }

        /// <summary>
        /// Enerji analiz raporu
        /// </summary>
        public async Task<IActionResult> EnergyAnalysis(int deviceId, int days = 30)
        {
            try
            {
                var device = await _context.Devices.FindAsync(deviceId);
                if (device == null)
                    return NotFound();

                // Geçici placeholder veriler
                var summary = new ModelsNS.EnergyConsumptionSummary();
                var trends = new List<ModelsNS.EnergyTrend>();
                var efficiencyReport = new ModelsNS.EnergyEfficiencyReport();
                var anomalies = new List<ModelsNS.AnomalyDetection>();
                var recommendations = new ModelsNS.EnergySavingsRecommendation();
                var carbonFootprint = 0.0;
                var estimatedBill = 0.0;

                var analysisData = new EnergyAnalysisViewModel
                {
                    Device = device,
                    Summary = summary,
                    Trends = trends,
                    EfficiencyReport = efficiencyReport,
                    Anomalies = anomalies,
                    Recommendations = recommendations,
                    CarbonFootprint = Math.Round(carbonFootprint, 2),
                    EstimatedMonthlyBill = Math.Round(estimatedBill, 2),
                    AnalysisPeriod = days
                };

                return View(analysisData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading energy analysis for device {DeviceId}", deviceId);
                return NotFound();
            }
        }

        /// <summary>
        /// Fatura tahmini ve maliyet analizi
        /// </summary>
        public async Task<IActionResult> BillPrediction()
        {
            try
            {
                // Tüm cihazları göster - performans için AsNoTracking
                var devices = await _context.Devices
                    .AsNoTracking()
                    .OrderByDescending(d => d.InstalledAt)
                    .ToListAsync();
                
                // Her cihaz için son enerji tüketimini ayrı sorgu ile al
                foreach (var device in devices)
                {
                    var lastConsumption = await _context.EnergyConsumptions
                        .Where(e => e.DeviceId == device.Id)
                        .AsNoTracking()
                        .OrderByDescending(e => e.RecordedAt)
                        .FirstOrDefaultAsync();
                    
                    if (lastConsumption != null)
                    {
                        device.EnergyConsumptions = new List<EnergyConsumption> { lastConsumption };
                    }
                }

                var last24Hours = DateTime.Now.AddHours(-24);
                var deviceIds = devices.Select(d => d.Id).ToList();
                var consumptions = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId.HasValue && deviceIds.Contains(e.DeviceId.Value) &&
                                e.RecordedAt >= last24Hours)
                    .ToListAsync();

                var dailyEnergy = consumptions.Sum(e => e.EnergyUsed);
                var dailyCost = consumptions.Sum(e => e.CostPerHour);

                var estimatedMonthlyEnergy = dailyEnergy * 30;
                var estimatedMonthlyCost = dailyCost * 30;
                var potentialSavings = estimatedMonthlyCost * 0.12; // varsayılan %12 iyileştirme

                var topConsumers = devices
                    .OrderByDescending(d => d.EnergyConsumptions.FirstOrDefault()?.EnergyUsed ?? 0)
                    .Take(5)
                    .ToList();

                var model = new ModelsNS.BillingSummaryViewModel
                {
                    DailyEnergy = Math.Round(dailyEnergy, 2),
                    DailyCost = Math.Round(dailyCost, 2),
                    EstimatedMonthlyEnergy = Math.Round(estimatedMonthlyEnergy, 2),
                    EstimatedMonthlyCost = Math.Round(estimatedMonthlyCost, 2),
                    PotentialMonthlySavings = Math.Round(potentialSavings, 2),
                    TopConsumers = topConsumers
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating billing summary");
                return View(new ModelsNS.BillingSummaryViewModel());
            }
        }

        /// <summary>
        /// AI/ML destekli enerji tüketimi tahmin sayfası
        /// Python ML servisi üzerindeki /predict-energy endpoint'ini kullanır.
        /// </summary>
        public async Task<IActionResult> EnergyForecast(int deviceId, int daysAhead = 7)
        {
            try
            {
                var device = await _context.Devices.FindAsync(deviceId);
                if (device == null)
                {
                    return NotFound();
                }

                // Python ML servisi üzerinden enerji tahmini al
                var prediction = await _aiMlService.PredictEnergyConsumptionAsync(deviceId, daysAhead);

                var model = new ModelsNS.EnergyForecastViewModel
                {
                    Device = device,
                    Prediction = prediction,
                    DaysAhead = daysAhead
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating energy forecast for device {DeviceId}", deviceId);
                return NotFound();
            }
        }

        /// <summary>
        /// Uyarılar sayfası
        /// </summary>
        public async Task<IActionResult> Alerts()
        {
            try
            {
                // Tüm uyarıları göster - performans için AsNoTracking ve pagination
                // Maksimum 1000 kayıt göster (çok fazla kayıt varsa yavaşlar)
                var alerts = await _context.Alerts
                    .Include(a => a.Device)              // Cihaz bilgisini de getir (Include önce gelmeli)
                    .AsNoTracking()                      // Change tracking kapalı (performans için)
                    .OrderByDescending(a => a.CreatedAt) // En yeni uyarılar önce
                    .Take(1000)                         // İlk 1000 kayıt
                    .ToListAsync();

                return View(alerts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading alerts");
                return View(new List<Alert>());
            }
        }

        /// <summary>
        /// Uyarıyı okundu olarak işaretle
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> MarkAlertAsRead(int id)
        {
            try
            {
                var alert = await _context.Alerts.FindAsync(id);
                if (alert == null)
                    return NotFound();

                alert.IsRead = true;
                alert.ReadAt = DateTime.Now;
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking alert as read");
                return Json(new { success = false, message = "Error updating alert" });
            }
        }

        /// <summary>
        /// Uyarı için ML tavsiyelerini getir
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAlertRecommendations(int alertId)
        {
            try
            {
                var alert = await _context.Alerts
                    .Include(a => a.Device)
                    .FirstOrDefaultAsync(a => a.Id == alertId);
                
                if (alert == null)
                    return Json(new { success = false, message = "Uyarı bulunamadı" });

                var recommendations = new List<string>();
                
                // Alert tipine göre ML tavsiyeleri
                if (alert.DeviceId.HasValue)
                {
                    // ML servisinden optimizasyon önerilerini al
                    var optimizationRecs = await _aiMlService.GetOptimizationRecommendationsAsync(alert.DeviceId.Value);
                    
                    // Alert tipine göre özel tavsiyeler
                    switch (alert.AlertType)
                    {
                        case "HighConsumption":
                        case "HighEnergyConsumption":
                            recommendations.Add("🔧 Cihazın bakımını kontrol edin");
                            recommendations.Add("⚡ Enerji verimliliği iyileştirmeleri yapın");
                            if (optimizationRecs.Actions.Any())
                            {
                                recommendations.AddRange(optimizationRecs.Actions
                                    .Where(a => a.Category == "Efficiency")
                                    .Select(a => $"💡 {a.Description}"));
                            }
                            break;
                            
                        case "TemperatureAnomaly":
                        case "TemperatureSpike":
                            recommendations.Add("❄️ Soğutma sistemini kontrol edin");
                            recommendations.Add("🌡️ Havalandırma sistemini iyileştirin");
                            recommendations.Add("🏠 Ortam sıcaklığını optimize edin");
                            if (optimizationRecs.Actions.Any())
                            {
                                recommendations.AddRange(optimizationRecs.Actions
                                    .Where(a => a.Category == "Temperature")
                                    .Select(a => $"💡 {a.Description}"));
                            }
                            break;
                            
                        case "VoltageAnomaly":
                        case "VoltageSpike":
                        case "LowVoltage":
                            recommendations.Add("⚡ Elektrik sistemini kontrol edin");
                            recommendations.Add("🔌 Voltaj regülatörü kullanmayı düşünün");
                            recommendations.Add("📊 Güç kalitesi analizi yapın");
                            break;
                            
                        case "LowPowerFactor":
                            recommendations.Add("🔋 Kompanzasyon sistemi kurun");
                            recommendations.Add("⚡ Reaktif güç kontrolü yapın");
                            recommendations.Add("📈 Güç faktörünü 0.9+ seviyesine çıkarın");
                            break;
                            
                        default:
                            recommendations.Add("🔍 Genel sistem kontrolü yapın");
                            recommendations.Add("📊 Cihaz performansını izleyin");
                            break;
                    }
                    
                    // Genel optimizasyon önerileri ekle
                    if (optimizationRecs.Actions.Any())
                    {
                        var generalRecs = optimizationRecs.Actions
                            .Where(a => a.Priority == "High")
                            .Take(2)
                            .Select(a => $"✅ {a.ActionName}: {a.Description}");
                        recommendations.AddRange(generalRecs);
                    }
                }
                else
                {
                    // Cihaz bilgisi yoksa genel tavsiyeler
                    recommendations.Add("🔍 Sistem genel kontrolü yapın");
                    recommendations.Add("📊 Uyarı kaynağını belirleyin");
                    recommendations.Add("⚙️ İlgili cihazları kontrol edin");
                }

                return Json(new { 
                    success = true, 
                    recommendations = recommendations,
                    alertType = alert.AlertType,
                    deviceName = alert.Device?.DeviceName ?? "Genel"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting alert recommendations for alert {AlertId}", alertId);
                return Json(new { 
                    success = false, 
                    message = "Tavsiyeler alınırken hata oluştu",
                    recommendations = new List<string> { "🔍 Genel sistem kontrolü yapın" }
                });
            }
        }

        /// <summary>
        /// Uyarıyı çözüldü olarak işaretle
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ResolveAlert([FromBody] ResolveAlertRequest request)
        {
            try
            {
                var alert = await _context.Alerts.FindAsync(request.Id);
                if (alert == null)
                    return Json(new { success = false, message = "Uyarı bulunamadı" });

                alert.IsResolved = true;
                alert.ResolvedAt = DateTime.Now;
                alert.ActionTaken = request.ActionTaken;
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving alert");
                return Json(new { success = false, message = "Uyarı çözülürken hata oluştu" });
            }
        }

        public class ResolveAlertRequest
        {
            public int Id { get; set; }
            public string ActionTaken { get; set; } = string.Empty;
        }

        /// <summary>
        /// Cihaz listesi sayfası
        /// </summary>
        public async Task<IActionResult> Devices()
        {
            try
            {
                // Tüm aktif cihazları göster (UserId filtresi kaldırıldı) - performans için AsNoTracking
                var devices = await _context.Devices
                    .AsNoTracking()
                    .OrderByDescending(d => d.InstalledAt)
                    .ToListAsync();
                return View(devices);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading devices");
                return View(new List<Device>());
            }
        }

        /// <summary>
        /// Uyarı silme
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteAlert(int id)
        {
            try
            {
                var alert = await _context.Alerts.FindAsync(id);
                if (alert == null)
                    return NotFound();

                _context.Alerts.Remove(alert);
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting alert");
                return Json(new { success = false, message = "Error deleting alert" });
            }
        }

        /// <summary>
        /// Tümünü okundu işaretle
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> MarkAllAsRead([FromBody] int[] alertIds)
        {
            try
            {
                var alerts = await _context.Alerts
                    .Where(a => alertIds.Contains(a.Id))
                    .ToListAsync();

                foreach (var alert in alerts)
                {
                    alert.IsRead = true;
                    alert.ReadAt = DateTime.Now;
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking alerts as read");
                return Json(new { success = false, message = "Error updating alerts" });
            }
        }

        /// <summary>
        /// POST /Dashboard/UpdateDevice/{id}
        /// Cihaz bilgilerini günceller
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> UpdateDevice(int id, [FromBody] UpdateDeviceRequest request)
        {
            try
            {
                // 🔹 Cihaz Bulma: Veritabanından cihazı ID ile bul
                var device = await _context.Devices.FindAsync(id);
                if (device == null)
                {
                    return Json(new { success = false, message = "Cihaz bulunamadı" });
                }

                // 🔹 Cihaz Bilgilerini Güncelleme: Request'ten gelen yeni değerleri ata
                device.DeviceName = request.DeviceName ?? device.DeviceName;
                device.DeviceType = request.DeviceType ?? device.DeviceType;
                device.Location = request.Location ?? device.Location;

                // 🔹 Veritabanına Kaydetme: Değişiklikleri veritabanına yaz
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Cihaz başarıyla güncellendi" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating device {DeviceId}", id);
                return Json(new { success = false, message = "Cihaz güncellenirken bir hata oluştu" });
            }
        }

        /// <summary>
        /// POST /Dashboard/DeleteDevice/{id}
        /// Cihazı siler (ilişkili veriler cascade ile silinir)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteDevice(int id)
        {
            try
            {
                // 🔹 Cihaz Bulma: Veritabanından cihazı ID ile bul
                var device = await _context.Devices.FindAsync(id);
                if (device == null)
                {
                    return Json(new { success = false, message = "Cihaz bulunamadı" });
                }

                // 🔹 Cihaz Silme: Veritabanından cihazı sil (cascade delete ile ilişkili veriler de silinir)
                _context.Devices.Remove(device);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Device deleted: {device.DeviceName} (ID: {device.Id})");

                return Json(new { success = true, message = "Cihaz başarıyla silindi" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting device {DeviceId}", id);
                return Json(new { success = false, message = "Cihaz silinirken bir hata oluştu" });
            }
        }
    }

    // 🔹 DTO: Cihaz güncelleme request modeli
    public class UpdateDeviceRequest
    {
        public string? DeviceName { get; set; }
        public string? DeviceType { get; set; }
        public string? Location { get; set; }
    }
}
