# Aygaz Smart Energy - Akıllı Enerji Yönetim Sistemi

## 🎯 Proje Açıklaması
Aygaz Smart Energy, IoT sensörleri ve yapay zeka teknolojilerini kullanarak gerçek zamanlı enerji izleme, analiz ve tasarruf önerileri sunan kapsamlı bir akıllı enerji yönetim sistemidir.

## 🔧 Teknoloji Stack
- **Backend**: ASP.NET Core 9
- **Database**: SQL Server
- **ORM**: Entity Framework Core
- **Identity**: ASP.NET Core Identity
- **Real-time**: SignalR + Redis backplane (yatay ölçekleme için)
- **AI/ML**: Python (Flask, scikit-learn, pandas)
- **Mesajlaşma**: RabbitMQ (sensor-data kuyruğu)

## 📋 Gereksinimler
- .NET 9 SDK
- SQL Server (LocalDB veya Express)
- Python 3.8+
- Visual Studio 2022 veya VS Code
- Redis 7+ (SignalR backplane)
- RabbitMQ 3.13+ (mesaj kuyruğu)

## 🚀 Kurulum

### Docker ile Çalıştırma (Önerilen)

```bash
# Proje dizinine git
cd C:\Users\kagan\Projects\AygazSmartEnergy

# Docker container'ları build et
docker-compose build

# Container'ları başlat
docker-compose up -d

# Log'ları izle
docker-compose logs -f dotnet-api
```

**Erişim:**
- Web UI: http://localhost:5001
- RabbitMQ Management: http://localhost:15672 (guest/guest)
- Python ML Service: http://localhost:5000

### Test Verisi Gönderme

```bash
# Python script ile test verisi gönder
python canli_veri_uret.py
```

Detaylı kurulum ve kullanım için **`MIMARI_VE_API_DOKUMANTASYONU.md`** dosyasına bakın.

## 📊 Özellikler
- ✅ Gerçek zamanlı enerji izleme
- ✅ IoT sensör entegrasyonu
- ✅ AI destekli anomali tespiti
- ✅ Enerji tasarruf önerileri
- ✅ Karbon ayak izi hesaplama
- ✅ Otomatik uyarı sistemi
- ✅ Interactive dashboard

## 🔌 API Endpoints

Detaylı API dokümantasyonu için **`MIMARI_VE_API_DOKUMANTASYONU.md`** dosyasına bakın.

### Önemli Endpoint'ler
- `POST /api/IoT/sensor-data` - Sensör verisi gönder
- `GET /api/IoT/sensor-data/latest` - Son sensör verileri
- `GET /api/IoT/devices` - Cihaz listesi
- `POST /api/EnergyApi/ml-results` - ML servisinden sonuçları al
- `GET /Dashboard/EnergyForecast` - AI enerji tahmini

## 🖥️ Dashboard Özeti
- Canlı sıcaklık, voltaj, fan ve cihaz durum kartları
- Hızlı aksiyon butonları (cihaz yönetimi, uyarılar, enerji analizi, fatura tahmini vb.)
- Karbon yoğunluğu göstergesi (gauge) ve sürdürülebilirlik kartları
- Enerji tüketim grafikleri (Chart.js) ve DataTables destekli cihaz/uyarı listeleri

## 🔐 Kimlik Doğrulama
- ASP.NET Core Identity ile kayıt, giriş, çıkış
- Profil ve ayar ekranları (kişisel bilgiler + sistem eşikleri)
- Oturum sonrası dashboard hero kartı kullanıcı adı/e-postası ile kişiselleşir

## 🔄 Gerçek Zamanlı Katman
- **SignalR + Redis**: Tüm dashboard istemcilerine canlı sensör verisi dağıtılır. Redis backplane, birden fazla uygulama örneği çalıştırıldığında mesajların paylaşılmasını sağlar.
- **RabbitMQ**: Mikro servislerin sensör verilerini asenkron olarak işlemesine imkân tanır. `RabbitMqOptions` ile yapılandırılır, `RabbitMqMessageBus` servis tarafından kuyruk/mesaj yönetimi yapılır.
- **Akış**: IoT cihazı → `IoTController.PostSensorData` → EF Core → SignalR hub → RabbitMQ mesajı → Redis → Tüm dashboard istemcileri.

## 📚 Dokümantasyon

### Ana Dokümantasyon
- **`MIMARI_VE_API_DOKUMANTASYONU.md`** ⭐ - Kapsamlı mimari ve API dokümantasyonu
- **`ESP8266_SETUP.md`** - ESP8266 IoT cihaz kurulumu

### Test ve Kullanım
- **`canli_veri_uret.py`** - Canlı test verisi gönderme scripti

## 📝 Notlar
- Tüm zaman damgaları UTC olarak saklanır, UI'da Europe/Istanbul'a çevrilir
- Python ML servisi en az 7 günlük veri bekler (enerji tahmini için)
- RabbitMQ mesajları asenkron işlenir (fire-and-forget pattern)
- SignalR bağlantıları otomatik yeniden bağlanır

## 👨‍💻 Geliştirici
Kağan - Aygaz Ar-Ge Başvurusu

## 📄 Lisans
MIT License






