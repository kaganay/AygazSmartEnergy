// 🔹 Namespace'ler: Gerekli kütüphaneleri içe aktarır
using AygazSmartEnergy.Configuration;      // RabbitMQ ve diğer konfigürasyon sınıfları
using AygazSmartEnergy.Data;               // Veritabanı context'i (AppDbContext)
using AygazSmartEnergy.Models;             // Entity modelleri (Device, SensorData, Alert vb.)
using AygazSmartEnergy.Services;           // Servis arayüzleri (IEnergyAnalysisService, IAlertService, IAIMLService)
using AygazSmartEnergy.Hubs;               // SignalR Hub'ı (EnergyHub - gerçek zamanlı veri için)
using Microsoft.EntityFrameworkCore;       // EF Core ORM
using Microsoft.AspNetCore.Identity;       // Kullanıcı kimlik doğrulama ve yetkilendirme
using System.Text.Json.Serialization;      // JSON serialization ayarları
using StackExchange.Redis;                 // Redis bağlantısı (SignalR backplane için opsiyonel)

// 🔹 WebApplication Builder: ASP.NET Core uygulamasını oluşturur
var builder = WebApplication.CreateBuilder(args);

// 🔹 MVC Servisleri: Controller ve View desteğini ekler
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        // Döngüsel referans sorununu çöz: Device -> EnergyConsumption -> Device gibi referansları yok sayar
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        // Null değerleri JSON'a yazmaz (daha temiz JSON çıktısı)
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// 🔹 Veritabanı Bağlantısı: SQL Server ile Entity Framework Core entegrasyonu
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null)));

// 🔹 Identity Servisi: Kullanıcı yönetimi ve kimlik doğrulama sistemi
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Şifre güvenlik ayarları: En az 8 karakter, büyük-küçük harf, rakam zorunlu
    options.Password.RequireDigit = true;          // Rakam zorunlu
    options.Password.RequiredLength = 8;           // Minimum 8 karakter
    options.Password.RequireNonAlphanumeric = false; // Özel karakter zorunlu değil
    options.Password.RequireUppercase = true;      // Büyük harf zorunlu
    options.Password.RequireLowercase = true;      // Küçük harf zorunlu

    // Kullanıcı ayarları: Her kullanıcının benzersiz e-posta adresi olmalı
    options.User.RequireUniqueEmail = true;        // E-posta benzersiz olmalı
    options.SignIn.RequireConfirmedEmail = false;  // E-posta doğrulama zorunlu değil
})
.AddEntityFrameworkStores<AppDbContext>()         // Identity verilerini AppDbContext'e kaydet
.AddDefaultTokenProviders();                      // Şifre sıfırlama token'ları için

// 🔹 SignalR Servisi: Gerçek zamanlı iletişim için (Dashboard canlı güncellemeleri için)
var signalRBuilder = builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;  // Geliştirme ortamında detaylı hata mesajları göster
});

// 🔹 Redis Bağlantısı (Opsiyonel): Çoklu sunucu durumunda SignalR backplane olarak kullanılır
// Redis olmadan da SignalR çalışır, sadece birden fazla sunucu olduğunda mesaj senkronizasyonu olmaz
var redisConnection = builder.Configuration.GetConnectionString("RedisConnection");
if (!string.IsNullOrEmpty(redisConnection) && !redisConnection.Contains("disabled"))
{
    // Redis varsa SignalR için backplane olarak ekle (mesajları tüm sunucular arasında paylaşır)
    signalRBuilder.AddStackExchangeRedis(redisConnection, options =>
    {
        // Redis channel prefix: Tüm mesajlar "AygazSmartEnergy" prefix'i ile başlar
        options.Configuration.ChannelPrefix = new RedisChannel("AygazSmartEnergy", RedisChannel.PatternMode.Auto);
    });
}
// Redis yoksa SignalR varsayılan olarak in-memory çalışır (tek sunucu için yeterli)

// 🔹 Servis Kayıtları: Dependency Injection container'a servisleri kaydeder
builder.Services.AddScoped<IEnergyAnalysisService, EnergyAnalysisService>();  // Enerji analiz servisi (her request'te yeni instance)
builder.Services.AddScoped<IAlertService, AlertService>();                     // Alert/uyarı yönetim servisi
builder.Services.AddScoped<IAIMLService, AIMLService>();                       // AI/ML servisi arayüzü

// 🔹 ML Servisi için HttpClient (Python ML servisine bağlanmak için) - Optimize edilmiş timeout ayarları
builder.Services.AddHttpClient<IAIMLService, AIMLService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);  // 10 saniye timeout (ML işlemleri için yeterli)
    client.DefaultRequestHeaders.Add("Connection", "keep-alive");  // Bağlantıyı açık tut
});

// 🔹 Genel HttpClient (IoT cihazlarından veri almak için) - Optimize edilmiş timeout ayarları
builder.Services.AddHttpClient("Default", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);  // 5 saniye timeout (IoT cihazları için yeterli)
    client.DefaultRequestHeaders.Add("Connection", "keep-alive");  // Bağlantıyı açık tut
});

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq")); // RabbitMQ ayarlarını config'den al
builder.Services.AddSingleton<IMessageBus, RabbitMqMessageBus>();             // RabbitMQ mesaj kuyruğu (singleton - tek instance)

// 🔹 CORS Ayarları: IoT cihazlarının farklı domain'lerden API'ye bağlanabilmesi için
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowIoTDevices", policy =>
    {
        policy.AllowAnyOrigin()      // Tüm origin'lerden isteklere izin ver
              .AllowAnyMethod()      // GET, POST, PUT, DELETE gibi tüm HTTP metodlarına izin ver
              .AllowAnyHeader();     // Tüm header'lara izin ver
    });
});

// 🔹 Uygulama Oluşturma: Builder'dan WebApplication nesnesini oluştur
var app = builder.Build();

// 🔹 Veritabanı Migration'ları: Veritabanı şemasını otomatik olarak günceller
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();      // Veritabanı bağlantısı
        var logger = services.GetRequiredService<ILogger<Program>>();   // Loglama servisi
        
        // 🔹 Bekleme Süresi: SQL Server hazır olana kadar dene (Docker container'ları başlarken gerekli)
        var maxRetries = 15;      // Maksimum 15 deneme
        var retryDelay = 5000;    // Her deneme arasında 5 saniye bekle
        var migrationApplied = false;
        
        // Connection string'i al ve master veritabanına bağlanmak için geçici connection string oluştur
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        var masterConnectionString = connectionString?.Replace("Database=AygazSmartEnergyDb", "Database=master");
        
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                // Önce master veritabanına bağlanarak SQL Server'ın hazır olup olmadığını kontrol et
                using (var masterContext = new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>()
                        .UseSqlServer(masterConnectionString)
                        .Options))
                {
                    if (await masterContext.Database.CanConnectAsync())
                    {
                        logger.LogInformation("SQL Server hazir. Veritabani migration'lari uygulaniyor...");
                        // Bekleyen migration'ları otomatik olarak uygula (veritabanı şemasını güncelle)
                        // MigrateAsync() veritabanı yoksa otomatik olarak oluşturur
                        await context.Database.MigrateAsync();
                        logger.LogInformation("Migration'lar basariyla uygulandi.");
                        migrationApplied = true;
                        break;  // Başarılı oldu, döngüden çık
                    }
                }
            }
            catch (Microsoft.Data.SqlClient.SqlException sqlEx) when (sqlEx.Number == 4060 && i < maxRetries - 1)
            {
                // Veritabanı bulunamadı hatası - MigrateAsync() veritabanını oluşturacak
                logger.LogInformation("Veritabani henuz olusturulmamis. Migration ile olusturulacak... (Deneme {Retry}/{MaxRetries})", i + 1, maxRetries);
                try
                {
                    await context.Database.MigrateAsync();
                    logger.LogInformation("Migration'lar basariyla uygulandi.");
                    migrationApplied = true;
                    break;
                }
                catch (Exception migrateEx) when (i < maxRetries - 1)
                {
                    logger.LogWarning(migrateEx, "Migration uygulanamadi, bekleniyor... (Deneme {Retry}/{MaxRetries})", i + 1, maxRetries);
                    await Task.Delay(retryDelay);
                }
            }
            catch (Exception ex) when (i < maxRetries - 1)
            {
                // Son deneme değilse, hata logla ve tekrar dene
                logger.LogWarning(ex, "SQL Server hazir degil, bekleniyor... (Deneme {Retry}/{MaxRetries})", i + 1, maxRetries);
                await Task.Delay(retryDelay);  // 5 saniye bekle ve tekrar dene
            }
        }
        
        if (!migrationApplied)
        {
            logger.LogError("Veritabani migration'lari uygulanamadi. Lutfen manuel olarak kontrol edin.");
        }
        
        // 🔹 Seed Data: Veritabanına başlangıç verilerini yükle (kullanıcılar, cihazlar, roller vb.)
       // await SeedData.Initialize(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Veritabani migration/seeding hatasi.");
    }
}

// 🔹 Hata Yönetimi: Production ortamında kullanıcıya daha temiz hata sayfası göster
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");  // Hata durumunda /Home/Error sayfasına yönlendir
    app.UseHsts();                            // HTTP Strict Transport Security (HTTPS zorunluluğu)
}

// 🔹 Middleware Pipeline: İsteklerin işlenme sırası (sıralama önemli!)
app.UseHttpsRedirection();           // HTTP isteklerini HTTPS'e yönlendir
app.UseStaticFiles();                // wwwroot klasöründeki statik dosyaları (CSS, JS, resimler) servis et
app.UseRouting();                    // URL routing'i etkinleştir (controller/action bulma)
app.UseCors("AllowIoTDevices");      // CORS politikasını uygula (IoT cihazları için)
app.UseAuthentication();             // Kimlik doğrulamayı kontrol et (kimlik bilgileri var mı?)
app.UseAuthorization();              // Yetkilendirmeyi kontrol et (bu kullanıcı bu sayfaya erişebilir mi?)

// 🔹 SignalR Hub Mapping: Gerçek zamanlı iletişim endpoint'i (/energyHub)
app.MapHub<EnergyHub>("/energyHub");  // Dashboard bu endpoint'e bağlanarak canlı veri alır

// 🔹 Varsayılan Route: İlk açılacak sayfa belirlenir (giriş yapılmamışsa Login'e yönlendir)
app.MapControllerRoute(
    name: "default",                                        // Route adı
    pattern: "{controller=Account}/{action=Login}/{id?}"); // URL pattern: /Account/Login veya sadece / (giriş sayfası)

// 🔹 Uygulamayı Başlat: HTTP isteklerini dinlemeye başla
app.Run();
