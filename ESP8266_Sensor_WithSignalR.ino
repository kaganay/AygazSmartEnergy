#include <ESP8266WiFi.h>
#include <ESP8266HTTPClient.h>
#include <WiFiClient.h>
#include <DHT.h>
#include <ArduinoJson.h>

// Pin Tanımlamaları
#define DHTPIN D1        // DHT22 Data Pini
#define LED_PIN D5       // LED'in bağlı olduğu Pin
#define FAN_PIN D6       // Fan Rölesinin bağlı olduğu Pin
#define VOLTAGE_SENSOR_PIN A0 // Voltaj Sensörünün bağlı olduğu Analog Pin

// DHT Sensör Tipi
#define DHTTYPE DHT22    // DHT 22 (AM2302)

// Eşik Değeri
const float SICAKLIK_ESIGI = 27.0; // Santigrat derece (°C)

// Voltaj Sensörü Kalibrasyonu
const float VOLTAGE_DIVIDER_RATIO = 4.53; // Voltaj Bölücü Oranı
const float ADC_REF_VOLTAGE = 3.3; // ESP8266 3.3V ile çalışır

// WiFi Ayarları - BURAYA KENDİ AĞ BİLGİLERİNİZİ GİRİN
const char* ssid = "min.";
const char* password = "12345678";

// Sunucu Bilgileri - Bilgisayarınızın IP adresi
// ESP8266 IP: 10.65.71.53
// Sunucu IP: 10.65.71.254
const char* serverUrl = "http://10.65.71.254:5152";

DHT dht(DHTPIN, DHTTYPE);

// Fan kontrolü için değişken
bool fanManualControl = false;
bool fanState = false;
unsigned long lastServerCheck = 0;
const unsigned long SERVER_CHECK_INTERVAL = 60000; // 60 saniyede bir sunucu kontrolü

void setup() {
  Serial.begin(115200);
  delay(1000);
  
  Serial.println("ESP8266 Kontrol Sistemi Baslatiliyor...");

  // Pinleri Çıkış olarak ayarla
  pinMode(LED_PIN, OUTPUT);
  pinMode(FAN_PIN, OUTPUT);

  // Başlangıçta LED ve Fan Kapalı
  digitalWrite(LED_PIN, LOW);
  digitalWrite(FAN_PIN, LOW);
  fanState = false;

  // DHT Sensörünü Başlat
  dht.begin();

  // WiFi Bağlantısı
  WiFi.begin(ssid, password);
  Serial.print("WiFi'ye baglaniyor");
  
  int wifiTimeout = 0;
  while (WiFi.status() != WL_CONNECTED && wifiTimeout < 30) {
    delay(500);
    Serial.print(".");
    wifiTimeout++;
  }
  
  if (WiFi.status() == WL_CONNECTED) {
    Serial.println("");
    Serial.println("WiFi baglandi!");
    Serial.print("ESP8266 IP Adresi: ");
    Serial.println(WiFi.localIP());
    Serial.print("Gateway IP: ");
    Serial.println(WiFi.gatewayIP());
    Serial.print("Sunucu URL: ");
    Serial.println(serverUrl);
    Serial.println("---");
    Serial.println("UYARI: ESP8266 ve sunucu ayni agda olmalidir!");
    Serial.print("ESP8266 IP: ");
    Serial.println(WiFi.localIP());
    Serial.println("Sunucu IP bu agda olmali!");
    Serial.println("---");
  } else {
    Serial.println("");
    Serial.println("WiFi baglanamadi!");
  }
}

void loop() {
  delay(2000); // Her 2 saniyede bir okuma yap

  // 1. DHT22 Sıcaklık Okuması
  float sicaklik = dht.readTemperature(); // Santigrat (Celsius) cinsinden oku

  // Okuma hatası kontrolü
  if (isnan(sicaklik)) {
    Serial.println("DHT sensöründen okuma hatasi!");
    return;
  }

  // 2. Voltaj Sensörü Okuması
  int adc_degeri = analogRead(VOLTAGE_SENSOR_PIN); // 0-1023
  
  // Analog değeri gerçek voltaja dönüştür
  float voltaj = ((float)adc_degeri / 1024.0) * ADC_REF_VOLTAGE * VOLTAGE_DIVIDER_RATIO;

  // 3. Sıcaklık Kontrolü ve Aktüatör Çalıştırma (sadece manuel kontrol yoksa)
  if (!fanManualControl) {
    if (sicaklik > SICAKLIK_ESIGI) {
      // Sıcaklık eşiği aşıldı
      if (!fanState) {
        digitalWrite(LED_PIN, HIGH); // LED'i Yak
        digitalWrite(FAN_PIN, HIGH); // Fanı Çalıştır (Röleyi Çek)
        fanState = true;
        Serial.println(">>> SICAKLIK ESIGI ASILDI! LED ve FAN ACILDI.");
      }
    } else {
      // Sıcaklık eşiği altında
      if (fanState) {
        digitalWrite(LED_PIN, LOW);  // LED'i Söndür
        digitalWrite(FAN_PIN, LOW);  // Fanı Kapat (Röleyi Bırak)
        fanState = false;
        Serial.println(">>> SICAKLIK NORMAL. LED ve FAN KAPATILDI.");
      }
    }
  }

  // 4. Seriye Veri Yazdırma
  Serial.print("Sicaklik: ");
  Serial.print(sicaklik);
  Serial.print(" °C | Voltaj Okumasi (ADC): ");
  Serial.print(adc_degeri);
  Serial.print(" | Gercek Voltaj: ");
  Serial.print(voltaj);
  Serial.print(" V | Fan: ");
  Serial.println(fanState ? "ACIK" : "KAPALI");

  // 5. Sunucuya Veri Gönderme
  if (WiFi.status() == WL_CONNECTED) {
    // Sunucu erişilebilirlik kontrolü
    unsigned long currentTime = millis();
    if (currentTime - lastServerCheck > SERVER_CHECK_INTERVAL || lastServerCheck == 0) {
      Serial.println("Sunucu erisilebilirlik kontrolu yapiliyor...");
      lastServerCheck = currentTime;
    }
    
    sendSensorDataToServer(sicaklik, voltaj, adc_degeri, fanState);
    
    // Fan durumunu sunucudan kontrol et (manuel kontrol için) - sadece her 5. döngüde
    static int loopCount = 0;
    loopCount++;
    if (loopCount >= 5) { // Her 10 saniyede bir kontrol et (2 saniye * 5 = 10 saniye)
      checkFanCommandFromServer();
      loopCount = 0;
    }
  } else {
    Serial.println("UYARI: WiFi baglantisi yok!");
    // WiFi bağlantısını yeniden dene
    WiFi.begin(ssid, password);
    int reconnectAttempts = 0;
    while (WiFi.status() != WL_CONNECTED && reconnectAttempts < 10) {
      delay(500);
      Serial.print(".");
      reconnectAttempts++;
    }
    if (WiFi.status() == WL_CONNECTED) {
      Serial.println("\nWiFi yeniden baglandi!");
    } else {
      Serial.println("\nWiFi yeniden baglanamadi!");
    }
  }
}

void sendSensorDataToServer(float temperature, float voltage, int adcValue, bool fanState) {
  WiFiClient client;
  HTTPClient http;

  String url = String(serverUrl) + "/api/IoT/sensor-data";
  
  // Timeout ayarları - donmayı önlemek için
  client.setTimeout(5000); // 5 saniye timeout
  http.begin(client, url);
  http.setTimeout(5000); // 5 saniye timeout
  http.addHeader("Content-Type", "application/json");
  http.addHeader("Connection", "close"); // Her istekten sonra bağlantıyı kapat

  // JSON oluştur
  StaticJsonDocument<512> doc;
  doc["sensorName"] = "ESP8266_DHT22_Sensor";
  doc["sensorType"] = "Temperature";
  doc["temperature"] = temperature;
  doc["gasLevel"] = 0; // MQ2 yoksa 0
  doc["energyUsage"] = 0; // Enerji kullanımı yoksa 0
  doc["voltage"] = voltage;
  doc["current"] = 0; // Akım yoksa 0
  doc["powerFactor"] = 1.0;
  doc["location"] = "ESP8266_Device";
  doc["status"] = "Active";
  doc["firmwareVersion"] = "1.0.0";
  doc["signalStrength"] = String(WiFi.RSSI()) + " dBm";
  
  JsonObject rawData = doc.createNestedObject("rawData");
  rawData["adcValue"] = adcValue;
  rawData["fanState"] = fanState;
  rawData["dht22"] = true;

  String jsonString;
  serializeJson(doc, jsonString);

  Serial.print("Sunucuya gonderiliyor: ");
  Serial.println(jsonString);
  Serial.print("Sunucu URL: ");
  Serial.println(url);

  int httpResponseCode = http.POST(jsonString);

  if (httpResponseCode > 0) {
    Serial.print("HTTP yanit kodu: ");
    Serial.println(httpResponseCode);
    
    String response = http.getString();
    Serial.print("Sunucu yaniti: ");
    Serial.println(response);

    // Fan durumu sunucudan değiştiyse güncelle
    if (response.length() > 0) {
      StaticJsonDocument<200> responseDoc;
      DeserializationError error = deserializeJson(responseDoc, response);
      
      if (!error && responseDoc.containsKey("fanState")) {
        bool serverFanState = responseDoc["fanState"];
        if (serverFanState != fanState && !fanManualControl) {
          digitalWrite(FAN_PIN, serverFanState ? HIGH : LOW);
          digitalWrite(LED_PIN, serverFanState ? HIGH : LOW);
          fanState = serverFanState;
          Serial.println(serverFanState ? "Sunucudan FAN ACILDI" : "Sunucudan FAN KAPATILDI");
        }
      }
    }
  } else {
    Serial.print("HTTP hatasi! Hata kodu: ");
    Serial.println(httpResponseCode);
    
    // Hata kodlarına göre mesaj göster
    if (httpResponseCode == -1) {
      Serial.println("HATA: Sunucuya baglanilamadi! (Timeout veya ag hatasi)");
      Serial.println("Kontrol edin:");
      Serial.println("1. Sunucu calisiyor mu?");
      Serial.println("2. IP adresi dogru mu? (Su anki: " + String(serverUrl) + ")");
      Serial.println("3. Port numarasi dogru mu?");
      Serial.println("4. WiFi baglantisi saglam mi?");
    } else if (httpResponseCode == -2) {
      Serial.println("HATA: Invalid server response");
    } else if (httpResponseCode == -3) {
      Serial.println("HATA: Invalid URL");
    } else if (httpResponseCode == -11) {
      Serial.println("HATA: Connection refused");
    }
  }

  http.end();
  client.stop(); // Bağlantıyı kapat
}

void checkFanCommandFromServer() {
  WiFiClient client;
  HTTPClient http;

  String url = String(serverUrl) + "/api/IoT/fan";
  
  // Timeout ayarları
  client.setTimeout(3000); // 3 saniye timeout (daha kısa)
  http.begin(client, url);
  http.setTimeout(3000);
  http.addHeader("Connection", "close");

  int httpResponseCode = http.GET();

  if (httpResponseCode == 200) {
    String response = http.getString();
    if (response.length() > 0) {
      StaticJsonDocument<200> doc;
      DeserializationError error = deserializeJson(doc, response);

      if (!error && doc.containsKey("on")) {
        bool serverFanState = doc["on"];
        
        // Manuel kontrol aktifse sunucudan gelen komutu kabul et
        if (serverFanState != fanState) {
          fanManualControl = true; // Manuel kontrol modu
          digitalWrite(FAN_PIN, serverFanState ? HIGH : LOW);
          digitalWrite(LED_PIN, serverFanState ? HIGH : LOW);
          fanState = serverFanState;
          Serial.println(serverFanState ? "Manuel: FAN ACILDI" : "Manuel: FAN KAPATILDI");
        } else {
          // Sunucu ile aynı durumda, otomatik moda geri dön
          fanManualControl = false;
        }
      }
    }
  } else {
    // Hata durumunda sessizce geç (fan kontrolü için kritik değil)
    Serial.print("Fan kontrol sorgusu basarisiz (kod: ");
    Serial.print(httpResponseCode);
    Serial.println(")");
  }

  http.end();
  client.stop();
}

