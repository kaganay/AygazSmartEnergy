# Aygaz Smart Energy - Mimari Dokümantasyon

## 📋 İçindekiler
1. [Genel Mimari](#genel-mimari)
2. [Controller'lar ve Sorumlulukları](#controllerlar-ve-sorumlulukları)
3. [Servisler ve İşlevleri](#servisler-ve-işlevleri)
4. [Veri Akışı](#veri-akışı)
5. [Alert Oluşturma Mekanizması](#alert-oluşturma-mekanizması)
6. [ML Servisi Entegrasyonu](#ml-servisi-entegrasyonu)
7. [Teknoloji Stack](#teknoloji-stack)

---

## 🏗️ Genel Mimari

### Mikroservis Mimarisi
Proje, **mikroservis mimarisi** prensiplerine göre tasarlanmıştır:

```
┌─────────────────┐
│  IoT Cihazları  │ (ESP8266, Sensörler)
└────────┬────────┘
         │ HTTP POST
         ▼
┌─────────────────────────────────────┐
│   ASP.NET Core API (Port 5001)      │
│  ┌───────────────────────────────┐  │
│  │  Controllers                 │  │
│  │  - IoTController             │  │
│  │  - DashboardController       │  │
│  │  - EnergyApiController       │  │
│  │  - AccountController         │  │
│  └───────────────────────────────┘  │
│  ┌───────────────────────────────┐  │
│  │  Services                     │  │
│  │  - AlertService              │  │
│  │  - AIMLService               │  │
│  │  - EnergyAnalysisService     │  │
│  │  - RabbitMqMessageBus        │  │
│  └───────────────────────────────┘  │
│  ┌───────────────────────────────┐  │
│  │  Hubs (SignalR)               │  │
│  │  - EnergyHub                  │  │
│  └───────────────────────────────┘  │
└────────┬────────────────────────────┘
         │
         ├──► SQL Server (Veritabanı)
         ├──► RabbitMQ (Mesaj Kuyruğu)
         ├──► Redis (SignalR Backplane)
         └──► Python ML Service (Port 5000/5002)
```

---

## 🎮 Controller'lar ve Sorumlulukları

### 1. **IoTController** (`/api/IoT`)
**Sorumluluk:** IoT cihazlarından gelen sensör verilerini işler.

**Ana Endpoint:**
- `POST /api/IoT/sensor-data` - Sensör verilerini alır

**İşlem Akışı:**
1. ✅ Gelen veriyi validasyon yapar
2. ✅ `SensorData` ve `EnergyConsumption` kayıtlarını veritabanına kaydeder
3. ✅ SignalR ile dashboard'a canlı veri gönderir (`NotifySensorDataUpdate`)
4. ✅ RabbitMQ'ya veri gönderir (`sensor-data` queue) — `_messageBus.PublishAsync`
5. ✅ Anomali kontrolü yapar (arka planda, scoped DbContext ile):
   - ML servisine HTTP isteği gönderir (`/detect-anomalies`)
   - ML servisi anomali döndürürse ilgili alert'leri üretir
   - ML başarısız/yanıt yoksa basit eşik kontrolleri çalışır
   - Anomali varsa `AlertService.CreateAlertAsync` çağrılır

**Kullanılan Servisler:**
- `AppDbContext` - Veritabanı işlemleri
- `IHubContext<EnergyHub>` - SignalR bildirimleri
- `IMessageBus` - RabbitMQ mesaj gönderme
- `IAlertService` - Alert oluşturma
- `HttpClient` - ML servisi ile iletişim

---

### 2. **DashboardController** (`/Dashboard`)
**Sorumluluk:** Web dashboard sayfalarını yönetir.

**Ana Sayfalar:**
- `GET /Dashboard` - Ana dashboard (özet istatistikler, cihazlar, uyarılar)
- `GET /Dashboard/Devices` - Cihaz listesi
- `GET /Dashboard/Device/{id}` - Cihaz detay sayfası
- `GET /Dashboard/Alerts` - Uyarı listesi
- `GET /Dashboard/BillPrediction` - Fatura tahmini

**Özellikler:**
- ✅ Tüm sayfalar `[Authorize]` ile korumalı (giriş zorunlu)
- ✅ `IAIMLService` ile enerji tahmini yapar
- ✅ `IEnergyAnalysisService` ile analiz yapar

**Kullanılan Servisler:**
- `AppDbContext` - Veritabanı sorguları
- `IAIMLService` - ML tahminleri
- `IEnergyAnalysisService` - Enerji analizi

---

### 3. **EnergyApiController** (`/api/EnergyApi`)
**Sorumluluk:** Enerji verileri ve ML sonuçları için API endpoint'leri.

**Ana Endpoint'ler:**
- `GET /api/EnergyApi/latest` - Son 10 enerji tüketimi kaydı
- `POST /api/EnergyApi/ml-results` - Python ML servisinden gelen sonuçları al

**Önemli:**
- `ml-results` endpoint'i Python ML servisinin callback'i olarak kullanılır
- ML servisi anomali tespit ettiğinde bu endpoint'e sonuç gönderir
- Alert oluşturma burada yapılır (Satır 153-165)
- **Not:** IoT verileri için `/api/IoT/sensor-data` endpoint'i kullanılmalıdır

---

### 4. **AccountController** (`/Account`)
**Sorumluluk:** Kullanıcı yönetimi (kayıt, giriş, profil, ayarlar).

**Ana Sayfalar:**
- `GET /Account/Register` - Kayıt sayfası
- `POST /Account/Register` - Kayıt işlemi
- `GET /Account/Login` - Giriş sayfası
- `POST /Account/Login` - Giriş işlemi
- `GET /Account/Profile` - Profil sayfası

**Kullanılan Servisler:**
- `UserManager<ApplicationUser>` - Kullanıcı yönetimi
- `SignInManager<ApplicationUser>` - Oturum yönetimi

---

## ⚙️ Servisler ve İşlevleri

### 1. **AlertService** (`Services/AlertService.cs`)
**Sorumluluk:** Alert oluşturma, yönetimi ve bildirimleri.

**Ana Metodlar:**
- `CreateAlertAsync()` - Alert oluşturur ve:
  - Veritabanına kaydeder
  - SignalR ile dashboard'a bildirim gönderir (`NotifyAlertCreated`)
  - Kritik/High severity ise e-posta simülasyonu yapar

**Alert Oluşturma Noktaları:**
1. `IoTController.CheckAnomaliesAndCreateAlertsAsync` (Satır 551) - ML servisi sonuçlarından
2. `IoTController.PerformSimpleAnomalyChecks` (Satır 611, 640, 669, 698, 736, 759, 782, 805) - Basit kontrollerden
3. `EnergyApiController.ReceiveMLResults` (Satır 165) - ML servisi callback'inden

---

### 2. **AIMLService** (`Services/AIMLService.cs`)
**Sorumluluk:** Python ML servisi ile entegrasyon (orchestrator).

**Ana Metodlar:**
- `PredictEnergyConsumptionAsync()` - Enerji tüketimi tahmini
  - Son 30 günlük verileri alır
  - Python ML servisine HTTP POST gönderir (`/predict-energy`)
  - ML servisi çalışmıyorsa fallback hesaplama yapar

**Fallback Mekanizması:**
- ML servisi çalışmıyorsa basit ortalama hesaplama yapar
- Sistem kesintisiz çalışmaya devam eder

---

### 3. **EnergyAnalysisService** (`Services/EnergyAnalysisService.cs`)
**Sorumluluk:** Enerji analizi ve trend hesaplamaları.

**Ana Metodlar:**
- `GetEnergyConsumptionSummaryAsync()` - Enerji tüketim özeti
- `GetEnergyTrendsAsync()` - Trend analizi
- `DetectAnomaliesAsync()` - Basit anomali tespiti (2-sigma kuralı)

---

### 4. **RabbitMqMessageBus** (`Services/RabbitMqMessageBus.cs`)
**Sorumluluk:** RabbitMQ üzerinden mesaj gönderme.

**Kullanım:**
- `PublishAsync(queueName, payload)` - Mesaj gönderir
- Exchange: `aygaz.sensors` (Topic type)
- Queue: `sensor-data` (IoT verileri için)

**Mesaj Akışı:**
```
IoTController → RabbitMQ (sensor-data queue) → Python ML Service (consumer)
```

---

### 5. **EnergyHub** (`Hubs/EnergyHub.cs`)
**Sorumluluk:** SignalR ile gerçek zamanlı iletişim.

**Ana Metodlar:**
- `JoinDeviceGroup(deviceId)` - Cihaz grubuna katıl
- `LeaveDeviceGroup(deviceId)` - Cihaz grubundan ayrıl
- `NotifySensorDataUpdate()` - Sensör verisi güncellemesi gönder
- `NotifyAlertCreated()` - Alert oluşturulduğunda bildirim gönder
- `NotifyEnergyConsumptionUpdate()` - Enerji tüketimi güncellemesi gönder

**Kullanım:**
- Dashboard sayfaları SignalR client olarak bağlanır
- Gerçek zamanlı veri güncellemeleri otomatik olarak gönderilir

---

## 🔄 Veri Akışı

### IoT Verisi İşleme Akışı

```
1. IoT Cihazı → HTTP POST /api/IoT/sensor-data
   │
   ├─► Validation (sıcaklık, voltaj, akım, güç faktörü)
   │
   ├─► Veritabanına Kaydet
   │   ├─► SensorData tablosuna kayıt
   │   └─► EnergyConsumption tablosuna kayıt
   │
   ├─► SignalR → Dashboard'a Canlı Güncelleme
   │   └─► EnergyHub.NotifySensorDataUpdate()
   │
   ├─► RabbitMQ → ML Servisi için Mesaj Gönder
   │   └─► Queue: "sensor-data"
   │
   └─► Anomali Kontrolü (Asenkron, Fire-and-Forget)
       ├─► ML Servisine HTTP İsteği (/detect-anomalies)
       │   ├─► Başarılı + Anomali Var → AlertService.CreateAlertAsync()
       │   └─► Başarısız → Fallback'e geç
       │
       └─► PerformSimpleAnomalyChecks (Fallback)
           ├─► Yüksek Tüketim (>300 kWh) → Alert
           ├─► Yüksek Sıcaklık (>40°C) → Alert
           ├─► Voltaj Anomalisi (<200V veya >250V) → Alert
           └─► Düşük Güç Faktörü (<0.7) → Alert
```

---

### ML Servisi Entegrasyonu

```
1. RabbitMQ Consumer (Python ML Service)
   │
   ├─► sensor-data queue'dan mesaj alır
   │
   ├─► Isolation Forest ile anomali tespiti yapar
   │
   └─► Sonuçları iki yolla gönderir:
       │
       ├─► HTTP POST /api/EnergyApi/ml-results (Callback)
       │   └─► EnergyApiController → Alert oluşturur
       │
       └─► HTTP POST /api/IoT/detect-anomalies (Direct)
           └─► IoTController.CheckAnomaliesAndCreateAlertsAsync
               └─► AlertService.CreateAlertAsync()
```

---

## 🚨 Alert Oluşturma Mekanizması

### Alert Oluşturma Noktaları (güncel kod)

1. **ML Servisi Sonuçlarından** – `IoTController.CheckAnomaliesAndCreateAlertsAsyncScoped`
   - ML servisine HTTP çağrısı yapılır, dönen her anomali için alert üretilir.
2. **Basit Eşik Kontrolleri** – `IoTController.PerformSimpleAnomalyChecksScoped`
   - ML başarısız veya anomali yoksa çalışır.
   - Kontroller ve eşikler:
     - Yüksek Enerji Tüketimi: `EnergyUsed > 300 kWh`
     - Yüksek Sıcaklık: `Temperature > 40°C` (50°C ve üzeri kritik)
     - Voltaj Anomalisi: `Voltage < 200V` veya `> 250V` (kritik <180V veya >260V)
     - Düşük Güç Faktörü: `PowerFactor < 0.7` (0.5 altı High)
   - Son 5 dakikada aynı tip alert varsa yeniden oluşturulmaz.
3. **ML Servisi Callback'inden** – `EnergyApiController.ReceiveMLResults`
   - Python ML servisi `POST /api/EnergyApi/ml-results` ile geldiğinde alert üretir.

---

### Alert İşleme Akışı

```
Alert Oluşturuldu (AlertService.CreateAlertAsync)
    │
    ├─► Veritabanına Kaydet (Alerts tablosu)
    │
    ├─► SignalR → Dashboard'a Bildirim Gönder
    │   └─► EnergyHub.NotifyAlertCreated(alert)
    │
    └─► Kritik/High Severity ise E-posta Simülasyonu
        └─► SendAlertNotificationAsync(alertId, "Email")
```

---

## 🤖 ML Servisi Entegrasyonu

### Python ML Service (`PythonMLService/app.py`)

**Ana Endpoint'ler:**
- `POST /detect-anomalies` - Anomali tespiti
- `POST /predict-energy` - Enerji tüketimi tahmini
- `POST /analyze-efficiency` - Verimlilik analizi

**Kullanılan Algoritmalar:**
- **Isolation Forest** - Anomali tespiti
- **Linear Regression** - Enerji tahmini
- **StandardScaler** - Veri normalizasyonu

**RabbitMQ Consumer:**
- `sensor-data` queue'dan mesaj alır
- Anomali tespiti yapar
- Sonuçları `/api/EnergyApi/ml-results` endpoint'ine gönderir

---

### AIMLService (C# Orchestrator)

**Sorumluluk:**
- Python ML servisini çağırır
- Sonuçları işler
- Fallback mekanizması sağlar

**Fallback:**
- ML servisi çalışmıyorsa basit ortalama hesaplama yapar
- Sistem kesintisiz çalışmaya devam eder

---

## 🛠️ Teknoloji Stack

### Backend
- **ASP.NET Core 9** - Web framework
- **Entity Framework Core** - ORM
- **ASP.NET Core Identity** - Kullanıcı yönetimi
- **SignalR** - Gerçek zamanlı iletişim
- **Redis** - SignalR backplane (yatay ölçekleme)

### Database
- **SQL Server** - Ana veritabanı

### Message Queue
- **RabbitMQ** - Mesaj kuyruğu (sensor-data queue)

### AI/ML
- **Python 3.8+** - ML servisi
- **Flask** - Web framework
- **scikit-learn** - ML algoritmaları
- **pandas** - Veri işleme

### Containerization
- **Docker** - Containerization
- **Docker Compose** - Multi-container orchestration

---

## 📊 Veritabanı Şeması

### Ana Tablolar
- **AspNetUsers** - Kullanıcılar (Identity)
- **Devices** - IoT cihazları
- **SensorData** - Sensör verileri
- **EnergyConsumptions** - Enerji tüketimi kayıtları
- **Alerts** - Uyarılar
- **AspNetRoles** - Roller (Identity)

### İlişkiler
- `Device.UserId` → `AspNetUsers.Id` (Many-to-One)
- `SensorData.DeviceId` → `Devices.Id` (Many-to-One)
- `EnergyConsumption.DeviceId` → `Devices.Id` (Many-to-One)
- `Alert.DeviceId` → `Devices.Id` (Many-to-One)
- `Alert.UserId` → `AspNetUsers.Id` (Many-to-One)

---

## 🔐 Güvenlik

### Authentication & Authorization
- **ASP.NET Core Identity** - Kullanıcı kimlik doğrulama
- **Cookie Authentication** - Oturum yönetimi
- **`[Authorize]` Attribute** - Sayfa koruması

### CORS
- IoT cihazları için `AllowAnyOrigin` politikası
- Production'da daha kısıtlayıcı ayarlar önerilir

---

## 🚀 Deployment

### Docker Compose
```yaml
services:
  - sqlserver (SQL Server)
  - redis (SignalR backplane)
  - rabbitmq (Message queue)
  - python-ml-service (ML servisi)
  - dotnet-api (Ana API)
```

### Port Mapping
- **5001** - ASP.NET Core API
- **5000/5002** - Python ML Service
- **15672** - RabbitMQ Management
- **1433** - SQL Server

---

## 📝 Önemli Notlar

### Anomali Kontrolü
- **İki seviyeli kontrol:**
  1. ML servisi ile gelişmiş anomali tespiti
  2. Basit eşik değer kontrolleri (fallback)

- **Duplicate Kontrolü:**
  - Son 1 dakikada aynı tip alert varsa yeni alert oluşturulmaz

### Asenkron İşlemler
- Anomali kontrolü **fire-and-forget** pattern ile yapılır
- RabbitMQ mesaj gönderme asenkron yapılır
- SignalR bildirimleri asenkron yapılır

### Hata Yönetimi
- ML servisi çalışmıyorsa fallback mekanizması devreye girer
- Tüm servisler try-catch ile korunur
- Loglama yapılır

---

## 🔄 Son Güncellemeler

### Düzeltilen Sorunlar
1. **Duplicate Anomali Kontrolü** (IoTController.cs Satır 150)
   - Önceki: `PerformSimpleAnomalyChecks` iki kez çağrılıyordu
   - Düzeltme: Duplicate çağrı kaldırıldı, sadece `CheckAnomaliesAndCreateAlertsAsync` çağrılıyor

---

## 📚 Ek Kaynaklar

- **README.md** - Proje kurulum ve genel bilgiler
- **ESP8266_SETUP.md** - IoT cihaz kurulum rehberi

