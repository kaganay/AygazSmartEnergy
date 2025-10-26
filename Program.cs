using AygazSmartEnergy.Data;
using AygazSmartEnergy.Models;
using AygazSmartEnergy.Services;   // Servis arayüzleri (IEnergyAnalysisService vb.)
using AygazSmartEnergy.Hubs;       // SignalR için EnergyHub sınıfı
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// 🔹 MVC servislerini ekle
builder.Services.AddControllersWithViews();

// 🔹 Veritabanı bağlantısı (EF Core)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 🔹 Identity (kullanıcı yönetimi)
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Şifre ayarları
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;

    // Kullanıcı ayarları
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// 🔹 SignalR (Gerçek zamanlı veri için)
builder.Services.AddSignalR();

// 🔹 Servis kayıtları (Controller’lar tarafından kullanılacak)
builder.Services.AddScoped<IEnergyAnalysisService, EnergyAnalysisService>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<IAIMLService, AIMLService>();
builder.Services.AddHttpClient<IAIMLService, AIMLService>();

// 🔹 CORS (IoT cihazlarının API’ye bağlanabilmesi için)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowIoTDevices", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// 🔹 Seed Data (veritabanına başlangıç verilerini yükler)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        await SeedData.Initialize(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred seeding the DB.");
    }
}

// 🔹 Hata yönetimi
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowIoTDevices");
app.UseAuthentication();
app.UseAuthorization();

// 🔹 SignalR Hub aktif (dashboard canlı veri için)
app.MapHub<EnergyHub>("/energyHub");

// 🔹 Varsayılan yönlendirme (ilk açılacak sayfa)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
