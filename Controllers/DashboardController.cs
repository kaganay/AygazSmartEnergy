using ModelsNS = AygazSmartEnergy.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AygazSmartEnergy.Data;
using AygazSmartEnergy.Services;
using AygazSmartEnergy.Models;

namespace AygazSmartEnergy.Controllers
{
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IEnergyAnalysisService _energyAnalysisService;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            AppDbContext context, 
            IEnergyAnalysisService energyAnalysisService,
            ILogger<DashboardController> logger)
        {
            _context = context;
            _energyAnalysisService = energyAnalysisService;
            _logger = logger;
        }

        /// <summary>
        /// Ana dashboard sayfası
        /// </summary>
        public async Task<IActionResult> Index()
        {
            try
            {
                var userId = "1"; // Geçici kullanıcı kimliği

                var devices = await _context.Devices
                    .Where(d => d.UserId == userId && d.IsActive)
                    .Include(d => d.EnergyConsumptions.Take(10))
                    .ToListAsync();

                var totalDevices = devices.Count;
                var activeDevices = devices.Count(d => d.IsActive);

                // Son 24 saatlik veriler
                var last24Hours = DateTime.Now.AddHours(-24);
                var deviceIds = devices.Select(d => d.Id).ToList();
                var recentConsumptions = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId.HasValue && deviceIds.Contains(e.DeviceId.Value) &&
                                e.RecordedAt >= last24Hours)
                    .ToListAsync();

                var totalEnergyConsumed = recentConsumptions.Sum(e => e.EnergyUsed);
                var totalCost = recentConsumptions.Sum(e => e.CostPerHour);
                var totalCarbonFootprint = recentConsumptions.Sum(e => e.CarbonFootprint);

                // Son uyarılar
                var recentAlerts = await _context.Alerts
                    .Where(a => a.UserId == userId && !a.IsResolved)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(5)
                    .ToListAsync();

                var dashboardData = new DashboardViewModel
                {
                    TotalDevices = totalDevices,
                    ActiveDevices = activeDevices,
                    TotalEnergyConsumed = Math.Round(totalEnergyConsumed, 2),
                    TotalCost = Math.Round(totalCost, 2),
                    TotalCarbonFootprint = Math.Round(totalCarbonFootprint, 2),
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
                var device = await _context.Devices
                    .Include(d => d.EnergyConsumptions.OrderByDescending(e => e.RecordedAt).Take(100))
                    .Include(d => d.SensorDatas.OrderByDescending(s => s.RecordedAt).Take(50))
                    .FirstOrDefaultAsync(d => d.Id == id);

                if (device == null)
                    return NotFound();

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
        /// Uyarılar sayfası
        /// </summary>
        public async Task<IActionResult> Alerts()
        {
            try
            {
                var userId = "1";
                var alerts = await _context.Alerts
                    .Where(a => a.UserId == userId)
                    .Include(a => a.Device)
                    .OrderByDescending(a => a.CreatedAt)
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
        /// Uyarıyı çözüldü olarak işaretle
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ResolveAlert(int id, string actionTaken)
        {
            try
            {
                var alert = await _context.Alerts.FindAsync(id);
                if (alert == null)
                    return NotFound();

                alert.IsResolved = true;
                alert.ResolvedAt = DateTime.Now;
                alert.ActionTaken = actionTaken;
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving alert");
                return Json(new { success = false, message = "Error resolving alert" });
            }
        }

        /// <summary>
        /// Cihaz listesi sayfası
        /// </summary>
        public async Task<IActionResult> Devices()
        {
            try
            {
                var userId = "1";
                var devices = await _context.Devices
                    .Where(d => d.UserId == userId)
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
    }
}
