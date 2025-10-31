# ESP8266 Sensor ve Fan Kontrol Sistemi Kurulumu

## Gereksinimler

1. **ESP8266 Modülü** (NodeMCU veya benzeri)
2. **DHT22 Sıcaklık Sensörü**
3. **Voltaj Sensörü** (A0 pini için)
4. **2-Channel Röle Modülü** (LED ve Fan için)
5. **Arduino IDE** veya **PlatformIO**
6. **WiFi Ağı**

## Kütüphaneler

Arduino IDE'de şu kütüphaneleri yükleyin:

1. **ESP8266WiFi** (ESP8266 Board Manager ile birlikte gelir)
2. **ESP8266HTTPClient** (ESP8266 Board Manager ile birlikte gelir)
3. **DHT sensor library** by Adafruit (Library Manager'dan yükleyin)
4. **ArduinoJson** by Benoit Blanchon (v6 veya v7 - Library Manager'dan yükleyin)

### Kütüphane Kurulumu:

1. Arduino IDE'de **Sketch > Include Library > Manage Libraries**
2. Aşağıdaki kütüphaneleri arayıp yükleyin:
   - `DHT sensor library` by Adafruit
   - `ArduinoJson` by Benoit Blanchon

## Bağlantı Şeması

```
ESP8266 (NodeMCU) Bağlantıları:
- D1  -> DHT22 Data
- D5  -> LED Rölesi (IN)
- D6  -> Fan Rölesi (IN)
- A0  -> Voltaj Sensörü
- 3.3V -> DHT22 VCC
- GND  -> DHT22 GND, Röle GND, Voltaj Sensörü GND
```

## Kod Yapılandırması

`ESP8266_Sensor_WithSignalR.ino` dosyasını açın ve şu bölümleri düzenleyin:

### 1. WiFi Ayarları
```cpp
const char* ssid = "WIFI_SSID";        // WiFi ağ adınızı yazın
const char* password = "WIFI_PASSWORD"; // WiFi şifrenizi yazın
```

### 2. Sunucu Adresi
```cpp
const char* serverUrl = "http://192.168.1.100:5000";
```
**Önemli:** Kendi bilgisayarınızın IP adresini ve port numarasını yazın. Port numarası genellikle 5000 veya 5001'dir (development ortamında).

Sunucu IP'nizi bulmak için:
- Windows: Komut satırında `ipconfig` çalıştırın, "IPv4 Address" değerini kullanın
- Mac/Linux: Terminal'de `ifconfig` veya `ip addr` çalıştırın

### 3. Sıcaklık Eşiği
```cpp
const float SICAKLIK_ESIGI = 27.0; // İstediğiniz eşik değerini yazın
```

## Yükleme

1. Arduino IDE'de **Tools > Board > NodeMCU 1.0 (ESP-12E Module)** seçin
2. **Tools > Upload Speed > 115200** seçin
3. **Tools > Port > COMx** (ESP8266'nızın bağlı olduğu port) seçin
4. Kodu yükleyin (Upload butonu)

## Çalıştırma

1. Seri Port Monitörü'nü açın (115200 baud rate)
2. ESP8266 başlatıldığında WiFi'ye bağlanmaya çalışacak
3. Bağlantı başarılı olduğunda her 2 saniyede bir sunucuya veri gönderecek
4. Dashboard'da canlı verileri göreceksiniz

## Özellikler

### Otomatik Fan Kontrolü
- Sıcaklık eşiği aşıldığında fan otomatik açılır
- Sıcaklık normale döndüğünde fan otomatik kapanır
- Eşik değeri `appsettings.json` içinde `TemperatureSettings:Threshold` ile ayarlanabilir

### Manuel Fan Kontrolü
- Web arayüzünden (navbar'daki "Fanı Aç/Kapat" butonu) manuel kontrol yapılabilir
- Manuel kontrol aktifken otomatik kontrol devre dışı kalır
- Fan durumu SignalR ile canlı olarak tüm istemcilere bildirilir

### Canlı Veri Akışı
- Sıcaklık ve voltaj değerleri SignalR ile canlı olarak Dashboard'a gönderilir
- Veriler her 2 saniyede bir güncellenir

## Sorun Giderme

### WiFi Bağlanamıyor
- SSID ve şifreyi kontrol edin
- WiFi sinyal gücünü kontrol edin
- ESP8266'nın menzil içinde olduğundan emin olun

### Sunucuya Veri Gönderilemiyor
- Sunucu IP adresini ve port numarasını kontrol edin
- Sunucunun çalıştığından emin olun
- Firewall ayarlarını kontrol edin (5000 portu açık olmalı)
- ESP8266 ve sunucunun aynı ağda olduğundan emin olun

### DHT22 Okuma Hatası
- DHT22 bağlantılarını kontrol edin
- Sensörün güçlendiğinden emin olun
- 3.3V yerine 5V deneyin (bazı modellerde gerekebilir)

### Fan Çalışmıyor
- Röle modülünün doğru bağlandığını kontrol edin
- Röle modülünün LED'inin yanıp söndüğünü kontrol edin
- Fan'ın güç bağlantılarını kontrol edin

## API Endpoints

ESP8266 şu endpoint'lere istek gönderir:

- `POST /api/IoT/sensor-data` - Sensör verilerini gönderir
- `GET /api/IoT/fan` - Fan durumunu alır

## Güvenlik Notu

**Geliştirme ortamı için:** Bu kod CORS ve authentication olmadan çalışır. Üretim ortamında mutlaka güvenlik önlemleri ekleyin.

