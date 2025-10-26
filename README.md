# Aygaz Smart Energy - Akıllı Enerji Yönetim Sistemi

## 🎯 Proje Açıklaması
Aygaz Smart Energy, IoT sensörleri ve yapay zeka teknolojilerini kullanarak gerçek zamanlı enerji izleme, analiz ve tasarruf önerileri sunan kapsamlı bir akıllı enerji yönetim sistemidir.

## 🔧 Teknoloji Stack
- **Backend**: ASP.NET Core 9
- **Database**: SQL Server
- **ORM**: Entity Framework Core
- **Identity**: ASP.NET Core Identity
- **Real-time**: SignalR
- **AI/ML**: Python (Flask, scikit-learn, pandas)

## 📋 Gereksinimler
- .NET 9 SDK
- SQL Server (LocalDB veya Express)
- Python 3.8+
- Visual Studio 2022 veya VS Code

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

## 📝 Notlar
- Proje halen geliştirme aşamasındadır
- Bazı özellikler test aşamasındadır
- Python ML servisi opsiyoneldir, olmadan da çalışır

## 👨‍💻 Geliştirici
Kağan - Aygaz Ar-Ge Başvurusu

## 📄 Lisans
MIT License

