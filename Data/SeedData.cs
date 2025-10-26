using AygazSmartEnergy.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AygazSmartEnergy.Data
{
    public static class SeedData
    {
        public static async Task Initialize(IServiceProvider serviceProvider)
        {
            using var context = new AppDbContext(
                serviceProvider.GetRequiredService<DbContextOptions<AppDbContext>>());
            
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // Rolleri oluştur
            await CreateRoles(roleManager);

            // Admin kullanıcısı oluştur
            await CreateAdminUser(userManager);

            // Test kullanıcısı oluştur
            var testUser = await CreateTestUser(userManager);

            // Test cihazları oluştur
            await CreateTestDevices(context, testUser);

            // Test sensör verileri oluştur
            await CreateTestSensorData(context);

            // Test enerji tüketim verileri oluştur
            await CreateTestEnergyConsumption(context);

            // Test uyarıları oluştur
            await CreateTestAlerts(context, testUser);
        }

        private static async Task CreateRoles(RoleManager<IdentityRole> roleManager)
        {
            string[] roles = { "Admin", "User", "Manager" };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }
        }

        private static async Task CreateAdminUser(UserManager<ApplicationUser> userManager)
        {
            var adminEmail = "admin@aygaz.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);

            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FirstName = "Admin",
                    LastName = "User",
                    UserType = "Admin",
                    IsActive = true,
                    EmailConfirmed = true
                };

                await userManager.CreateAsync(adminUser, "Admin123!");
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }

        private static async Task<ApplicationUser> CreateTestUser(UserManager<ApplicationUser> userManager)
        {
            var testEmail = "test@aygaz.com";
            var testUser = await userManager.FindByEmailAsync(testEmail);

            if (testUser == null)
            {
                testUser = new ApplicationUser
                {
                    UserName = testEmail,
                    Email = testEmail,
                    FirstName = "Test",
                    LastName = "User",
                    UserType = "Individual",
                    IsActive = true,
                    EmailConfirmed = true
                };

                await userManager.CreateAsync(testUser, "Test123!");
                await userManager.AddToRoleAsync(testUser, "User");
            }

            return testUser;
        }

        private static async Task CreateTestDevices(AppDbContext context, ApplicationUser user)
        {
            if (!context.Devices.Any())
            {
                var devices = new List<Device>
                {
                    new Device
                    {
                        DeviceName = "Ana Sayaç - Giriş",
                        DeviceType = "Smart Meter",
                        Location = "Giriş Katı",
                        Description = "Ana elektrik sayacı - giriş katı",
                        IsActive = true,
                        InstalledAt = DateTime.Now.AddMonths(-6),
                        LastMaintenanceAt = DateTime.Now.AddDays(-30),
                        UserId = user.Id
                    },
                    new Device
                    {
                        DeviceName = "Pompa Sistemi - Bodrum",
                        DeviceType = "Pump",
                        Location = "Bodrum Katı",
                        Description = "Su pompası sistemi",
                        IsActive = true,
                        InstalledAt = DateTime.Now.AddMonths(-4),
                        LastMaintenanceAt = DateTime.Now.AddDays(-15),
                        UserId = user.Id
                    },
                    new Device
                    {
                        DeviceName = "HVAC Sistemi - 1. Kat",
                        DeviceType = "HVAC",
                        Location = "1. Kat",
                        Description = "Isıtma ve soğutma sistemi",
                        IsActive = true,
                        InstalledAt = DateTime.Now.AddMonths(-8),
                        LastMaintenanceAt = DateTime.Now.AddDays(-45),
                        UserId = user.Id
                    },
                    new Device
                    {
                        DeviceName = "Aydınlatma - Ofis",
                        DeviceType = "Lighting",
                        Location = "Ofis Alanı",
                        Description = "LED aydınlatma sistemi",
                        IsActive = true,
                        InstalledAt = DateTime.Now.AddMonths(-2),
                        LastMaintenanceAt = DateTime.Now.AddDays(-7),
                        UserId = user.Id
                    },
                    new Device
                    {
                        DeviceName = "Eski Sayaç - Arşiv",
                        DeviceType = "Smart Meter",
                        Location = "Arşiv Odası",
                        Description = "Eski elektrik sayacı - arşiv",
                        IsActive = false,
                        InstalledAt = DateTime.Now.AddYears(-2),
                        LastMaintenanceAt = DateTime.Now.AddMonths(-6),
                        UserId = user.Id
                    }
                };

                context.Devices.AddRange(devices);
                await context.SaveChangesAsync();
            }
        }

        private static async Task CreateTestSensorData(AppDbContext context)
        {
            if (!context.SensorDatas.Any())
            {
                var devices = context.Devices.ToList();
                var random = new Random();
                var sensorData = new List<SensorData>();

                foreach (var device in devices.Where(d => d.IsActive))
                {
                    for (int i = 0; i < 100; i++) // Son 100 veri
                    {
                        var recordedAt = DateTime.Now.AddHours(-i * 2); // Her 2 saatte bir veri
                        
                        sensorData.Add(new SensorData
                        {
                            SensorName = $"{device.DeviceName} - Sensör 1",
                            SensorType = "Energy",
                            Temperature = 20 + random.NextDouble() * 15, // 20-35°C
                            GasLevel = random.NextDouble() * 100, // 0-100%
                            EnergyUsage = 50 + random.NextDouble() * 200, // 50-250 kWh
                            Voltage = 220 + random.NextDouble() * 20, // 220-240V
                            Current = 1 + random.NextDouble() * 10, // 1-11A
                            PowerFactor = 0.8 + random.NextDouble() * 0.2, // 0.8-1.0
                            Location = device.Location,
                            Status = "Active",
                            RecordedAt = recordedAt,
                            DeviceId = device.Id,
                            RawData = $"{{\"timestamp\":\"{recordedAt:O}\",\"quality\":\"good\"}}",
                            FirmwareVersion = "1.2.3",
                            SignalStrength = $"{-30 - random.Next(20)}dBm"
                        });
                    }
                }

                context.SensorDatas.AddRange(sensorData);
                await context.SaveChangesAsync();
            }
        }

        private static async Task CreateTestEnergyConsumption(AppDbContext context)
        {
            if (!context.EnergyConsumptions.Any())
            {
                var devices = context.Devices.Where(d => d.IsActive).ToList();
                var random = new Random();
                var energyConsumptions = new List<EnergyConsumption>();

                foreach (var device in devices)
                {
                    for (int i = 0; i < 30; i++) // Son 30 gün
                    {
                        var recordedAt = DateTime.Now.AddDays(-i);
                        var energyUsed = 10 + random.NextDouble() * 50; // 10-60 kWh
                        
                        energyConsumptions.Add(new EnergyConsumption
                        {
                            DeviceId = device.Id,
                        EnergyUsed = energyUsed,
                        RecordedAt = recordedAt,
                        ConsumptionInterval = "Daily",
                            PowerConsumption = energyUsed * 1000, // W cinsinden
                            Temperature = 20 + random.NextDouble() * 15,
                            Humidity = 40 + random.NextDouble() * 40,
                            PowerFactor = 0.8 + random.NextDouble() * 0.2,
                            Voltage = 220 + random.NextDouble() * 20,
                            WeatherCondition = new[] { "Sunny", "Cloudy", "Rainy", "Windy" }[random.Next(4)],
                            Notes = i % 7 == 0 ? "Haftalık bakım yapıldı" : null
                        });
                    }
                }

                context.EnergyConsumptions.AddRange(energyConsumptions);
                await context.SaveChangesAsync();
            }
        }

        private static async Task CreateTestAlerts(AppDbContext context, ApplicationUser user)
        {
            if (!context.Alerts.Any())
            {
                var devices = context.Devices.Where(d => d.IsActive).ToList();
                var alerts = new List<Alert>
                {
                    new Alert
                    {
                        UserId = user.Id,
                        Title = "Yüksek Enerji Tüketimi",
                        Message = "Ana Sayaç - Giriş cihazında yüksek enerji tüketimi tespit edildi.",
                        AlertType = "HighConsumption",
                        Severity = "High",
                        IsResolved = false,
                        CreatedAt = DateTime.Now.AddHours(-2),
                        DeviceId = devices.First().Id,
                        AdditionalData = "{\"consumption\": 250.5, \"threshold\": 200.0}"
                    },
                    new Alert
                    {
                        UserId = user.Id,
                        Title = "Sıcaklık Anomalisi",
                        Message = "HVAC Sistemi - 1. Kat cihazında yüksek sıcaklık tespit edildi.",
                        AlertType = "TemperatureAnomaly",
                        Severity = "Medium",
                        IsResolved = false,
                        CreatedAt = DateTime.Now.AddHours(-5),
                        DeviceId = devices.Skip(2).First().Id,
                        AdditionalData = "{\"temperature\": 45.2, \"threshold\": 40.0}"
                    },
                    new Alert
                    {
                        UserId = user.Id,
                        Title = "Cihaz Çevrimdışı",
                        Message = "Pompa Sistemi - Bodrum cihazından 15 dakikadır veri alınamıyor.",
                        AlertType = "DeviceOffline",
                        Severity = "Critical",
                        IsResolved = true,
                        CreatedAt = DateTime.Now.AddDays(-1),
                        ResolvedAt = DateTime.Now.AddDays(-1).AddHours(2),
                        ActionTaken = "Cihaz yeniden başlatıldı ve bağlantı sağlandı.",
                        DeviceId = devices.Skip(1).First().Id,
                        AdditionalData = "{\"lastSeen\": \"2024-01-15T10:30:00Z\"}"
                    },
                    new Alert
                    {
                        UserId = user.Id,
                        Title = "Düşük Güç Faktörü",
                        Message = "Aydınlatma - Ofis cihazında düşük güç faktörü tespit edildi.",
                        AlertType = "LowPowerFactor",
                        Severity = "Low",
                        IsResolved = false,
                        CreatedAt = DateTime.Now.AddHours(-12),
                        DeviceId = devices.Skip(3).First().Id,
                        AdditionalData = "{\"powerFactor\": 0.65, \"threshold\": 0.8}"
                    }
                };

                context.Alerts.AddRange(alerts);
                await context.SaveChangesAsync();
            }
        }
    }
}
