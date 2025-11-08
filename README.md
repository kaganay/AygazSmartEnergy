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

### 1. Veritabanı Kurulumu
```bash
# Migration oluştur
dotnet ef migrations add InitialCreate

# Veritabanını güncelle
dotnet ef database update
```

### 2. Python ML Servisi Kurulumu
```bash
cd PythonMLService
pip install -r requirements.txt
python app.py
```

### 3. Projeyi Çalıştır
```bash
dotnet run
```

### 4. Redis Backplane
```bash
# Redis'i Docker ile ayağa kaldır
docker run -d --name aygaz-redis -p 6379:6379 redis:7-alpine
```

### 5. RabbitMQ
```bash
# RabbitMQ'yu Docker ile ayağa kaldır
docker run -d --name aygaz-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:3.13-management
```

### 6. Docker ile Çalıştırma
```bash
# İmajı oluştur
docker build -t aygaz-smart-energy .

# Konteyneri çalıştır (port 8080)
docker run -d -p 8080:8080 --name aygaz-smart-energy-app aygaz-smart-energy
```

## 📊 Özellikler
- ✅ Gerçek zamanlı enerji izleme
- ✅ IoT sensör entegrasyonu
- ✅ AI destekli anomali tespiti
- ✅ Enerji tasarruf önerileri
- ✅ Karbon ayak izi hesaplama
- ✅ Otomatik uyarı sistemi
- ✅ Interactive dashboard

## 🔌 API Endpoints

### IoT Endpoints
- `POST /api/iot/sensor-data` - Sensör verisi gönder
- `GET /api/iot/sensor-data/latest` - Son sensör verileri
- `GET /api/iot/devices` - Cihaz listesi

### Cihaz Endpoints
- `GET /api/device/status` - Cihazın güncel durumunu görüntüle

### Mesajlaşma
- `POST /api/energyapi/upload` çağrısı, enerji verisini kaydettikten sonra RabbitMQ `sensor-data` kuyruğuna JSON mesaj yayınlar.
- Kuyruk, başka bir servis tarafından tüketilerek raporlama/analitik modüllerine aktarılabilir.

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
- **Akış**: IoT cihazı → `EnergyApiController.UploadData` → EF Core → RabbitMQ mesajı → SignalR hub → Redis → Tüm dashboard istemcileri.

## 📝 Notlar
- Proje halen geliştirme aşamasındadır
- Bazı özellikler test aşamasındadır
- Python ML servisi opsiyoneldir, olmadan da çalışır

## 👨‍💻 Geliştirici
Kağan - Aygaz Ar-Ge Başvurusu

## 📄 Lisans
MIT License






