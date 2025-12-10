#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Canlı Veri Üretici - Aygaz Smart Energy
Sürekli olarak gerçek zamanlı test verileri gönderir
"""

import requests
import time
import random
import json
from datetime import datetime
import sys

# API URL
API_URL = "http://localhost:5001/api/IoT/sensor-data"

# Cihaz bilgileri
devices = []

def get_active_devices():
    """Aktif cihazları API'den al"""
    global devices
    try:
        response = requests.get("http://localhost:5001/api/IoT/devices", timeout=5)
        if response.status_code == 200:
            data = response.json()
            if data.get("success") and data.get("data"):
                devices = data["data"]
                return True
        devices = [{"id": 1, "deviceName": "Test Cihaz 1"}]
        return False
    except Exception as e:
        devices = [{"id": 1, "deviceName": "Test Cihaz 1"}]
        return False

def generate_normal_data(device_id, sensor_name):
    """Normal aralıkta veri üret"""
    return {
        "deviceId": device_id,
        "sensorName": sensor_name,
        "sensorType": "Energy",
        "temperature": round(20 + random.uniform(0, 15), 1),  # 20-35°C
        "gasLevel": round(random.uniform(10, 50), 1),
        "energyUsage": round(100 + random.uniform(0, 200), 1),  # 100-300W
        "voltage": round(215 + random.uniform(0, 15), 2),  # 215-230V
        "current": round(1 + random.uniform(0, 5), 2),  # 1-6A
        "powerFactor": round(0.85 + random.uniform(0, 0.15), 2),  # 0.85-1.0
        "location": f"Test Lokasyon - {sensor_name}",
        "status": "Active"
    }

def generate_anomaly_data(device_id, sensor_name, anomaly_type="random"):
    """Anomali verisi üret"""
    anomalies = {
        "high_temperature": {
            "temperature": round(55 + random.uniform(0, 20), 1),  # 55-75°C
            "voltage": round(220 + random.uniform(-5, 5), 2),
            "current": round(5 + random.uniform(0, 5), 2),
            "powerFactor": round(0.80 + random.uniform(0, 0.15), 2),
            "energyUsage": round(200 + random.uniform(0, 100), 1)
        },
        "low_voltage": {
            "temperature": round(20 + random.uniform(0, 10), 1),
            "voltage": round(180 + random.uniform(0, 15), 2),  # 180-195V (düşük)
            "current": round(2 + random.uniform(0, 3), 2),
            "powerFactor": round(0.70 + random.uniform(0, 0.10), 2),
            "energyUsage": round(80 + random.uniform(0, 50), 1)
        },
        "high_voltage": {
            "temperature": round(25 + random.uniform(0, 10), 1),
            "voltage": round(255 + random.uniform(0, 15), 2),  # 255-270V (yüksek)
            "current": round(4 + random.uniform(0, 4), 2),
            "powerFactor": round(0.85 + random.uniform(0, 0.10), 2),
            "energyUsage": round(250 + random.uniform(0, 100), 1)
        },
        "high_consumption": {
            "temperature": round(30 + random.uniform(0, 15), 1),
            "voltage": round(220 + random.uniform(-5, 5), 2),
            "current": round(10 + random.uniform(0, 8), 2),  # Yüksek akım
            "powerFactor": round(0.75 + random.uniform(0, 0.15), 2),
            "energyUsage": round(350000 + random.uniform(0, 100000), 1)  # 350-450 kWh (uyarı tetikler)
        },
        "low_power_factor": {
            "temperature": round(22 + random.uniform(0, 8), 1),
            "voltage": round(218 + random.uniform(-3, 3), 2),
            "current": round(3 + random.uniform(0, 3), 2),
            "powerFactor": round(0.40 + random.uniform(0, 0.25), 2),  # 0.40-0.65 (düşük)
            "energyUsage": round(150 + random.uniform(0, 50), 1)
        },
        "critical": {
            "temperature": round(70 + random.uniform(0, 15), 1),  # 70-85°C (kritik)
            "voltage": round(265 + random.uniform(0, 10), 2),  # 265-275V (kritik)
            "current": round(12 + random.uniform(0, 8), 2),  # Yüksek akım
            "powerFactor": round(0.35 + random.uniform(0, 0.15), 2),  # Çok düşük
            "energyUsage": round(500000 + random.uniform(0, 200000), 1)  # 500-700 kWh (kritik)
        }
    }
    
    if anomaly_type == "random":
        anomaly_type = random.choice(list(anomalies.keys()))
    
    data = anomalies.get(anomaly_type, anomalies["high_temperature"])
    
    return {
        "deviceId": device_id,
        "sensorName": sensor_name,
        "sensorType": "Energy",
        "temperature": data["temperature"],
        "gasLevel": round(random.uniform(20, 60), 1),
        "energyUsage": data["energyUsage"],
        "voltage": data["voltage"],
        "current": data["current"],
        "powerFactor": data["powerFactor"],
        "location": f"Test Lokasyon - {sensor_name}",
        "status": "Critical" if anomaly_type == "critical" else "Warning"
    }

def send_sensor_data(data):
    """Sensör verisini API'ye gönder"""
    try:
        headers = {"Content-Type": "application/json", "Connection": "keep-alive"}
        response = requests.post(API_URL, json=data, headers=headers, timeout=3)  # 5 saniye → 3 saniye (daha hızlı timeout)
        
        if response.status_code == 200:
            result = response.json()
            timestamp = datetime.now().strftime("%H:%M:%S")
            device_name = data.get('sensorName', 'Bilinmeyen').split('_')[0] if '_' in data.get('sensorName', '') else data.get('sensorName', 'Bilinmeyen')
            print(f"[{timestamp}] {device_name} - Temp: {data['temperature']}°C | Volt: {data['voltage']}V | Enerji: {data['energyUsage']:.0f}W")
            return True
        else:
            print(f"✗ Hata {response.status_code}: {response.text}")
            return False
    except Exception as e:
        print(f"✗ İstek hatası: {e}")
        return False

def main():
    """Ana fonksiyon"""
    # Aktif cihazları al
    get_active_devices()
    
    if not devices:
        print("✗ Hiç cihaz bulunamadı!")
        sys.exit(1)
    
    print(f"{len(devices)} cihaza veri gönderiliyor... (Ctrl+C ile durdur)\n")
    
    # İstatistikler
    stats = {
        "total": 0,
        "success": 0,
        "failed": 0,
        "normal": 0,
        "anomaly": 0
    }
    
    try:
        interval = 3  # 3 saniye aralıklarla gönder (daha hızlı test)
        anomaly_probability = 0.15  # %15 olasılıkla anomali
        
        # Anomali tipleri
        anomaly_types = ["high_temperature", "low_voltage", "high_voltage", 
                        "high_consumption", "low_power_factor", "critical"]
        
        while True:
            for device in devices:
                device_id = device.get("id", 1)
                device_name = device.get("deviceName", "Test Cihaz")
                sensor_name = f"{device_name}_Sensor_{random.randint(1, 3)}"
                
                # Normal mi anomali mi?
                if random.random() < anomaly_probability:
                    # Anomali verisi üret
                    anomaly_type = random.choice(anomaly_types)
                    data = generate_anomaly_data(device_id, sensor_name, anomaly_type)
                    stats["anomaly"] += 1
                else:
                    # Normal veri üret
                    data = generate_normal_data(device_id, sensor_name)
                    stats["normal"] += 1
                
                # Veriyi gönder
                stats["total"] += 1
                if send_sensor_data(data):
                    stats["success"] += 1
                else:
                    stats["failed"] += 1
                
                # Kısa bekleme (aynı cihaz için) - optimize edildi
                time.sleep(0.05)  # Cihazlar arası kısa bekleme (daha hızlı)
            
            # Her 10 gönderimde özet göster
            if stats["total"] % 10 == 0:
                print(f"→ {stats['total']} veri gönderildi (✓{stats['success']} ✗{stats['failed']})")
            
            # Ana bekleme
            time.sleep(interval)
            
    except KeyboardInterrupt:
        print(f"\n✓ Durduruldu - Toplam: {stats['total']} (Başarılı: {stats['success']}, Başarısız: {stats['failed']})")

if __name__ == "__main__":
    main()

