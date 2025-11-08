using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AygazSmartEnergy.Data;
using AygazSmartEnergy.Models;

namespace AygazSmartEnergy.Controllers
{
    public class SimpleDashboardController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SimpleDashboardController> _logger;

        public SimpleDashboardController(AppDbContext context, ILogger<SimpleDashboardController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var devices = await _context.Devices
                    .Where(d => d.IsActive)
                    .Take(10)
                    .ToListAsync();

                var alerts = await _context.Alerts
                    .Where(a => !a.IsResolved)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(5)
                    .ToListAsync();

                var recentConsumptions = await _context.EnergyConsumptions
                    .Where(e => e.DeviceId != null)
                    .OrderByDescending(e => e.RecordedAt)
                    .Take(20)
                    .ToListAsync();

                var totalEnergy = recentConsumptions.Sum(e => e.EnergyUsed);
                var totalCost = recentConsumptions.Sum(e => e.CostPerHour);
                var totalCarbon = recentConsumptions.Sum(e => e.CarbonFootprint);

                ViewBag.TotalDevices = devices.Count;
                ViewBag.ActiveDevices = devices.Count(d => d.IsActive);
                ViewBag.TotalEnergy = Math.Round(totalEnergy, 2);
                ViewBag.TotalCost = Math.Round(totalCost, 2);
                ViewBag.TotalCarbon = Math.Round(totalCarbon, 2);
                ViewBag.Devices = devices;
                ViewBag.Alerts = alerts;
                ViewBag.RecentConsumptions = recentConsumptions;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading dashboard");
                return View();
            }
        }

        public async Task<IActionResult> Devices()
        {
            try
            {
                var devices = await _context.Devices.ToListAsync();
                return View(devices);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading devices");
                return View(new List<Device>());
            }
        }

        public async Task<IActionResult> Alerts()
        {
            try
            {
                var alerts = await _context.Alerts
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
    }
}







