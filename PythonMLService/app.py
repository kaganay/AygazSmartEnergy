from flask import Flask, request, jsonify  # pyright: ignore[reportMissingImports]
import pandas as pd  # pyright: ignore[reportMissingImports]
import numpy as np  # pyright: ignore[reportMissingImports]
from sklearn.ensemble import IsolationForest  # pyright: ignore[reportMissingImports]
from sklearn.preprocessing import StandardScaler  # pyright: ignore[reportMissingImports]
from sklearn.linear_model import LinearRegression  # pyright: ignore[reportMissingImports]
from sklearn.metrics import mean_absolute_error  # pyright: ignore[reportMissingImports]
import joblib  # pyright: ignore[reportMissingImports]
import os
from datetime import datetime, timedelta, timezone
import warnings
import json
import requests
import pika  # pyright: ignore[reportMissingModuleSource]
import threading
from typing import Dict, Any, Optional
warnings.filterwarnings('ignore')

app = Flask(__name__)

# ML servis (anomali + tahmin) için model klasörü
MODEL_DIR = 'models'
if not os.path.exists(MODEL_DIR):
    os.makedirs(MODEL_DIR)

class EnergyMLService:
    """Enerji yönetimi için ML servisi (IsolationForest + LinearRegression)."""
    def __init__(self):
        # Veri normalizasyonu için StandardScaler
        self.scaler = StandardScaler()
        
        # Isolation Forest - Anomali Tespiti Algoritması
        # contamination=0.1: %10 anomali beklentisi
        # random_state=42: Tekrarlanabilirlik için
        self.anomaly_detector = IsolationForest(contamination=0.1, random_state=42)
        
        # Linear Regression - Enerji Tüketimi Tahmini
        self.energy_predictor = LinearRegression()
        
    def predict_energy_consumption(self, historical_data, days_ahead):
        """Linear Regression ile enerji tüketimi tahmini."""
        try:
            # 1. VERİ HAZIRLAMA: Geçmiş verileri DataFrame'e dönüştür
            df = pd.DataFrame(historical_data)
            df['Date'] = pd.to_datetime(df['Date'])
            df = df.sort_values('Date')  # Tarihe göre sırala
            
            # 2. ÖZELLİK MÜHENDİSLİĞİ (Feature Engineering):
            #    Zaman bazlı özellikler ekle - ML modelinin öğrenmesi için kritik
            df['DayOfWeek'] = df['Date'].dt.dayofweek  # Haftanın günü (0=Pazartesi, 6=Pazar)
            df['Hour'] = df['Date'].dt.hour  # Günün saati (0-23)
            df['Month'] = df['Date'].dt.month  # Ay (1-12)
            # NOT: Bu özellikler mevsimsel ve günlük pattern'leri yakalamaya yardımcı olur
            
            # 3. TREND HESAPLAMA: Hareketli ortalama ile trend analizi
            df['EnergyTrend'] = df['EnergyConsumption'].rolling(window=7).mean()  # 7 günlük ortalama
            df['PowerTrend'] = df['PowerConsumption'].rolling(window=7).mean()  # 7 günlük güç ortalaması
            # NOT: Rolling window ile kısa vadeli trendleri yakalarız
            
            # 4. VERİ TEMİZLEME: Eksik değerleri doldur
            df = df.ffill().bfill()  # Önce ileriye, sonra geriye doldur
            # NOT: ML modeli eksik veri ile çalışamaz, bu yüzden dolduruyoruz
            
            # 5. ÖZELLİK SEÇİMİ: ML modeline verilecek girdi değişkenleri
            features = ['EnergyConsumption', 'PowerConsumption', 'Temperature', 
                      'Voltage', 'Current', 'PowerFactor', 'DayOfWeek', 'Hour', 'Month']
            # NOT: Bu özellikler enerji tüketimini etkileyen ana faktörlerdir
            
            X = df[features].values  # Girdi matrisi (features)
            y = df['EnergyConsumption'].values  # Çıktı vektörü (hedef değişken)
            
            # 6. MODEL EĞİTİMİ: Linear Regression modelini geçmiş verilerle eğit
            self.energy_predictor.fit(X, y)
            # NOT: Model, özellikler ile enerji tüketimi arasındaki ilişkiyi öğrenir
            
            # 7. TAHMİN BAŞLANGICI: Son mevcut veriyi kullan
            last_data = df.iloc[-1][features].values.reshape(1, -1)
            
            # 8. GELECEK TAHMİNLERİ: Her gün için ayrı ayrı tahmin yap
            predictions = []
            current_data = last_data.copy()
            
            for day in range(1, days_ahead + 1):
                # Tarih güncelleme: Gelecek tarihin özelliklerini hesapla
                future_date = df['Date'].iloc[-1] + timedelta(days=day)
                current_data[0][6] = future_date.dayofweek  # DayOfWeek güncelle
                current_data[0][7] = 12  # Saat (varsayılan: öğle vakti)
                current_data[0][8] = future_date.month  # Month güncelle
                
                # TAHMİN: Eğitilmiş model ile gelecek günün enerji tüketimini tahmin et
                pred = self.energy_predictor.predict(current_data)[0]
                predictions.append(pred)
                
                # İTERATİF TAHMİN: Sonraki gün için önceki tahmini kullan
                current_data[0][0] = pred  # EnergyConsumption'ı güncelle
                # NOT: Bu sayede her gün bir önceki günün tahminine bağlı olur
            
            # 9. GÜVEN ARALIĞI HESAPLAMA: Tahminin ne kadar güvenilir olduğunu hesapla
            mae = mean_absolute_error(y, self.energy_predictor.predict(X))  # Ortalama mutlak hata
            confidence = max(0.1, min(0.9, 1 - (mae / np.mean(y))))  # Güven seviyesi (0-1)
            # NOT: MAE ne kadar düşükse, güven seviyesi o kadar yüksektir
            
            # 10. SONUÇ HAZIRLAMA: Tahmin sonuçlarını yapılandırılmış formatta döndür
            return {
                'PredictionDate': (datetime.now() + timedelta(days=days_ahead)).isoformat(),
                'PredictedEnergyConsumption': float(predictions[-1]),  # Tahmin edilen enerji (kWh)
                'ConfidenceLevel': float(confidence),  # Güven seviyesi (0-1)
                'MinPrediction': float(predictions[-1] * 0.8),  # Minimum tahmin (%20 alt)
                'MaxPrediction': float(predictions[-1] * 1.2),  # Maximum tahmin (%20 üst)
                'Factors': [
                    {
                        'FactorName': 'Tarihsel Trend',
                        'Impact': float(np.mean(np.diff(predictions))),  # Günlük değişim ortalaması
                        'Description': 'Geçmiş verilere dayalı trend analizi. Pozitif değer artış, negatif değer azalış gösterir.'
                    },
                    {
                        'FactorName': 'Sıcaklık Etkisi',
                        'Impact': float(df['Temperature'].corr(df['EnergyConsumption'])),  # Korelasyon katsayısı
                        'Description': 'Sıcaklık ile enerji tüketimi arasındaki ilişki. 1.0 = tam pozitif, -1.0 = tam negatif korelasyon.'
                    }
                ]
            }
            # YORUMLAMA REHBERİ:
            # - PredictedEnergyConsumption: Beklenen enerji tüketimi (kWh)
            # - ConfidenceLevel > 0.7: Yüksek güvenilirlik, planlama için kullanılabilir
            # - MinPrediction - MaxPrediction: %80 güven aralığı (gerçek değer bu aralıkta olma ihtimali yüksek)
            # - Factors: Tahmini etkileyen faktörler ve etki dereceleri
        except Exception as e:
            print(f"Error in energy prediction: {e}")
            return {
                'PredictionDate': (datetime.now() + timedelta(days=days_ahead)).isoformat(),
                'PredictedEnergyConsumption': 0.0,
                'ConfidenceLevel': 0.0,
                'MinPrediction': 0.0,
                'MaxPrediction': 0.0,
                'Factors': []
            }
    
    def detect_anomalies(self, data):
        """
        Anomali Tespiti - Isolation Forest Algoritması + Basit Eşik Kontrolleri
        
        AI Teknolojisi: Isolation Forest (Unsupervised Learning)
        - Random Forest tabanlı anomali tespiti
        - Her veri noktası için isolation score hesaplar
        - Label: -1 = Anomali, 1 = Normal
        - Score: Negatif = Anomali (daha negatif = daha anormal)
        
        Özellikler (Features):
        - EnergyConsumption, PowerConsumption, Temperature
        - Voltage, Current, PowerFactor
        
        NOT: Tek veri noktası ile Isolation Forest çalışmaz, bu durumda basit eşik kontrolleri kullanılır
        """
        try:
            df = pd.DataFrame(data)
            df['Date'] = pd.to_datetime(df['Date'])
            
            # Özellikler (ML modeli için girdi değişkenleri)
            features = ['EnergyConsumption', 'PowerConsumption', 'Temperature', 
                      'Voltage', 'Current', 'PowerFactor']
            
            # Tek veri noktası kontrolü: Isolation Forest için en az 2 veri noktası gerekir
            if len(df) < 2:
                # Basit eşik kontrolleri ile anomali tespiti
                anomalies = []
                row = df.iloc[0]
                
                # Yüksek Enerji Tüketimi (>300 kWh)
                if row['EnergyConsumption'] > 300:
                    anomalies.append({
                        'DetectedAt': row['Date'].isoformat(),
                        'AnomalyType': 'HighConsumption',
                        'Description': f'Yüksek enerji tüketimi tespit edildi: {row["EnergyConsumption"]:.2f} kWh (Eşik: 300 kWh)',
                        'Severity': 0.7,  # High severity
                        'NormalValue': 200.0,  # Varsayılan normal değer
                        'ActualValue': float(row['EnergyConsumption']),
                        'Recommendation': 'Enerji tüketimini optimize etmek için cihaz kullanımını gözden geçirin.'
                    })
                
                # Yüksek Sıcaklık (>40°C)
                if row['Temperature'] > 40:
                    severity = 0.9 if row['Temperature'] > 50 else 0.7
                    anomalies.append({
                        'DetectedAt': row['Date'].isoformat(),
                        'AnomalyType': 'TemperatureAnomaly',
                        'Description': f'Yüksek sıcaklık tespit edildi: {row["Temperature"]:.2f}°C (Eşik: 40°C)',
                        'Severity': severity,
                        'NormalValue': 25.0,
                        'ActualValue': float(row['Temperature']),
                        'Recommendation': 'Cihazın soğutma sistemini kontrol edin ve havalandırmayı iyileştirin.'
                    })
                
                # Voltaj Anomalisi (<200V veya >250V) - 0 değeri geçersiz, kontrol etme
                if row['Voltage'] > 0 and (row['Voltage'] < 200 or row['Voltage'] > 250):
                    severity = 0.9 if (row['Voltage'] < 180 or row['Voltage'] > 260) else 0.6
                    anomalies.append({
                        'DetectedAt': row['Date'].isoformat(),
                        'AnomalyType': 'VoltageAnomaly',
                        'Description': f'Voltaj anomalisi tespit edildi: {row["Voltage"]:.2f}V (Normal: 200-250V)',
                        'Severity': severity,
                        'NormalValue': 220.0,
                        'ActualValue': float(row['Voltage']),
                        'Recommendation': 'Elektrik şebekesindeki voltaj dalgalanmalarını kontrol edin.'
                    })
                
                # Düşük Güç Faktörü (<0.7) - 0 değeri geçersiz, kontrol etme
                if row['PowerFactor'] > 0 and row['PowerFactor'] < 0.7:
                    severity = 0.8 if row['PowerFactor'] < 0.5 else 0.6
                    anomalies.append({
                        'DetectedAt': row['Date'].isoformat(),
                        'AnomalyType': 'LowPowerFactor',
                        'Description': f'Düşük güç faktörü tespit edildi: {row["PowerFactor"]:.2f} (Normal: >0.8)',
                        'Severity': severity,
                        'NormalValue': 0.85,
                        'ActualValue': float(row['PowerFactor']),
                        'Recommendation': 'Güç faktörünü iyileştirmek için kompanzasyon sistemini kontrol edin.'
                    })
                
                return anomalies
            
            # Birden fazla veri noktası varsa Isolation Forest kullan
            X = df[features].values
            
            # Isolation Forest ile anomali tespiti
            # fit_predict: Modeli eğitir ve tahmin yapar (online learning)
            anomaly_labels = self.anomaly_detector.fit_predict(X)
            # decision_function: Anomali skorunu hesaplar (-1 ile 1 arası)
            anomaly_scores = self.anomaly_detector.decision_function(X)
            
            anomalies = []
            for i, (label, score) in enumerate(zip(anomaly_labels, anomaly_scores)):
                if label == -1:  # Anomali
                    anomaly_type = self._classify_anomaly(df.iloc[i], features)
                    anomalies.append({
                        'DetectedAt': df.iloc[i]['Date'].isoformat(),
                        'AnomalyType': anomaly_type,
                        'Description': f'{anomaly_type} anomali tespit edildi',
                        'Severity': float(abs(score)),
                        'NormalValue': float(np.mean(X[:, features.index('EnergyConsumption')])),
                        'ActualValue': float(df.iloc[i]['EnergyConsumption']),
                        'Recommendation': self._get_anomaly_recommendation(anomaly_type)
                    })
            
            return anomalies
        except Exception as e:
            print(f"Error in anomaly detection: {e}")
            import traceback
            traceback.print_exc()
            return []
    
    def optimize_energy(self, device_info, historical_data):
        """
        ============================================================
        ENERJİ OPTİMİZASYONU ANALİZİ - AI/ML SERVİSİ
        ============================================================
        
        AMAÇ: Cihazın enerji kullanımını analiz ederek optimizasyon önerileri sunar.
        
        KULLANILAN YÖNTEM: İstatistiksel Analiz + Kural Tabanlı Öneriler
        - Verimlilik analizi (efficiency analysis)
        - Trend analizi (trend analysis)
        - Korelasyon analizi (correlation analysis)
        - Maliyet-fayda analizi (cost-benefit analysis)
        
        GİRDİ VERİLERİ:
        - device_info: Cihaz bilgileri (MaxPowerConsumption, DeviceType vb.)
        - historical_data: Son 30 günlük enerji tüketim verileri
        
        ÇIKTI:
        - Actions: Önerilen optimizasyon aksiyonları
        - PotentialSavings: Potansiyel tasarruf (TL/ay)
        - EnergyReduction: Enerji azaltma miktarı (kWh/ay)
        - CarbonReduction: Karbon emisyonu azaltma (kg CO2/ay)
        - ImplementationCost: Uygulama maliyeti (TL)
        - PaybackPeriod: Geri ödeme süresi (ay)
        
        YORUMLAMA:
        - PotentialSavings > ImplementationCost: Yatırım mantıklı
        - PaybackPeriod < 12 ay: Hızlı geri dönüş, öncelikli
        - Priority: High > Medium > Low (öncelik sırası)
        
        ============================================================
        """
        try:
            # 1. VERİ HAZIRLAMA
            df = pd.DataFrame(historical_data)
            df['Date'] = pd.to_datetime(df['Date'])
            
            # 2. VERİMLİLİK ANALİZİ: Cihazın ne kadar verimli çalıştığını hesapla
            avg_power = df['PowerConsumption'].mean()  # Ortalama güç tüketimi
            max_power = device_info['MaxPowerConsumption']  # Maksimum güç kapasitesi
            efficiency = (avg_power / max_power) * 100  # Verimlilik yüzdesi
            # YORUM: efficiency < 70% = Düşük verimlilik, optimizasyon gerekli
            #        efficiency > 85% = İyi verimlilik, mevcut durum yeterli
            
            # 3. TREND ANALİZİ: Enerji tüketiminin artış/azalış trendini belirle
            power_trend = df['PowerConsumption'].pct_change().mean()  # Güç trendi (% değişim)
            energy_trend = df['EnergyConsumption'].pct_change().mean()  # Enerji trendi (% değişim)
            # YORUM: Pozitif trend = Artış var, optimizasyon gerekli
            #        Negatif trend = Azalış var, iyi durum
            
            actions = []  # Önerilen aksiyonlar listesi
            
            # 4. VERİMLİLİK ÖNERİLERİ: Düşük verimlilik durumunda öneriler
            if efficiency < 70:
                actions.append({
                    'ActionName': 'Enerji Verimliliği İyileştirmesi',
                    'Description': f'Cihazın enerji verimliliği {efficiency:.1f}% seviyesinde (hedef: >85%). Bakım ve optimizasyon gerekli.',
                    'Category': 'Efficiency',
                    'PotentialSavings': 200.0,  # Aylık tasarruf (TL)
                    'EnergyReduction': 50.0,  # Aylık enerji azaltma (kWh)
                    'ImplementationCost': 1000.0,  # Uygulama maliyeti (TL)
                    'PaybackPeriod': 5,  # Geri ödeme süresi (ay)
                    'Priority': 'High',  # Öncelik seviyesi
                    'Steps': [
                        'Cihazın periyodik bakımını yapın',
                        'Eski parçaları yenileyin',
                        'Kullanım saatlerini optimize edin'
                    ],
                    'ExpectedImpact': f'Verimlilik {efficiency:.1f}% → 85%+ (hedef)'
                })
            
            # 5. ZAMAN BAZLI OPTİMİZASYON: Artış trendi varsa zamanlama önerileri
            if power_trend > 0.1:  # %10'dan fazla artış trendi
                actions.append({
                    'ActionName': 'Zaman Bazlı Kullanım Optimizasyonu',
                    'Description': f'Enerji tüketimi {power_trend*100:.1f}% artış trendinde. Kullanım saatlerini optimize edin.',
                    'Category': 'Schedule',
                    'PotentialSavings': 150.0,  # Aylık tasarruf (TL)
                    'EnergyReduction': 30.0,  # Aylık enerji azaltma (kWh)
                    'ImplementationCost': 500.0,  # Uygulama maliyeti (TL)
                    'PaybackPeriod': 3,  # Geri ödeme süresi (ay)
                    'Priority': 'Medium',  # Öncelik seviyesi
                    'Steps': [
                        'Pik saatlerde kullanımı azaltın',
                        'Gece saatlerinde çalıştırın',
                        'Hafta sonu kullanımını artırın'
                    ],
                    'ExpectedImpact': f'Trend {power_trend*100:.1f}% → 0% (stabil)'
                })
            
            # 6. SICAKLIK OPTİMİZASYONU: Sıcaklık ile enerji tüketimi arasındaki ilişkiyi analiz et
            temp_correlation = df['Temperature'].corr(df['EnergyConsumption'])  # Korelasyon katsayısı
            # YORUM: Korelasyon > 0.5 = Güçlü pozitif ilişki (sıcaklık artarsa enerji artar)
            if temp_correlation > 0.5:
                actions.append({
                    'ActionName': 'Sıcaklık Kontrolü',
                    'Description': f'Sıcaklık-enerji korelasyonu {temp_correlation:.2f} (güçlü ilişki). Yüksek sıcaklık enerji tüketimini artırıyor.',
                    'Category': 'Temperature',
                    'PotentialSavings': 100.0,  # Aylık tasarruf (TL)
                    'EnergyReduction': 20.0,  # Aylık enerji azaltma (kWh)
                    'ImplementationCost': 2000.0,  # Uygulama maliyeti (TL)
                    'PaybackPeriod': 20,  # Geri ödeme süresi (ay)
                    'Priority': 'Medium',  # Öncelik seviyesi
                    'Steps': [
                        'Soğutma sistemini kontrol edin',
                        'Havalandırma iyileştirin',
                        'Gölgelendirme ekleyin'
                    ],
                    'ExpectedImpact': f'Korelasyon {temp_correlation:.2f} → 0.3 (zayıf ilişki)'
                })
            
            # 7. SONUÇ HAZIRLAMA: Tüm önerileri topla ve özet metrikleri hesapla
            total_savings = sum(action['PotentialSavings'] for action in actions)
            total_energy_reduction = sum(action['EnergyReduction'] for action in actions)
            total_cost = sum(action['ImplementationCost'] for action in actions)
            
            return {
                'Actions': actions,  # Tüm önerilen aksiyonlar
                'PotentialSavings': total_savings,  # Toplam potansiyel tasarruf (TL/ay)
                'EnergyReduction': total_energy_reduction,  # Toplam enerji azaltma (kWh/ay)
                'CarbonReduction': total_energy_reduction * 0.4,  # Karbon azaltma (kg CO2/ay) - 1 kWh ≈ 0.4 kg CO2
                'ImplementationCost': total_cost,  # Toplam uygulama maliyeti (TL)
                'PaybackPeriod': max(action['PaybackPeriod'] for action in actions) if actions else 0,  # En uzun geri ödeme süresi
                'ROI': (total_savings * 12 / total_cost * 100) if total_cost > 0 else 0  # Yıllık getiri oranı (%)
            }
            # YORUMLAMA REHBERİ:
            # - PotentialSavings: Aylık tasarruf miktarı (TL)
            # - ROI > 100%: Yatırım 1 yılda kendini amorti eder
            # - PaybackPeriod < 12: Hızlı geri dönüş, öncelikli uygulanmalı
            # - CarbonReduction: Çevresel etki (karbon ayak izi azaltma)
        except Exception as e:
            print(f"Error in energy optimization: {e}")
            return {
                'Actions': [],
                'PotentialSavings': 0.0,
                'EnergyReduction': 0.0,
                'CarbonReduction': 0.0,
                'ImplementationCost': 0.0,
                'PaybackPeriod': 0
            }
    
    def predict_maintenance(self, device_info, historical_data):
        """
        ============================================================
        BAKIM TAHMİNİ - AI/ML SERVİSİ
        ============================================================
        
        AMAÇ: Cihazın performans verilerine dayanarak bakım ihtiyacını tahmin eder.
        
        KULLANILAN YÖNTEM: Predictive Maintenance (Öngörülü Bakım)
        - Zaman bazlı analiz (time-based analysis)
        - Performans bazlı analiz (performance-based analysis)
        - Risk skorlama (risk scoring)
        - Aciliyet belirleme (urgency determination)
        
        GİRDİ VERİLERİ:
        - device_info: Cihaz bilgileri (InstallationDate, LastMaintenance vb.)
        - historical_data: Son 30 günlük performans verileri
        
        ÇIKTI:
        - PredictedMaintenanceDate: Önerilen bakım tarihi
        - UrgencyScore: Aciliyet skoru (0-1 arası, 1 = çok acil)
        - MaintenanceType: Bakım türü (Acil/Planlı/Rutin/Önleyici)
        - RiskLevel: Risk seviyesi (Critical/High/Medium/Low)
        - RecommendedActions: Önerilen bakım aksiyonları
        - EstimatedCost: Tahmini bakım maliyeti (TL)
        
        YORUMLAMA:
        - UrgencyScore > 0.8: Acil bakım gerekli, hemen planlanmalı
        - UrgencyScore 0.6-0.8: Planlı bakım, 1-2 ay içinde yapılmalı
        - UrgencyScore < 0.4: Önleyici bakım, rutin takvimde yapılabilir
        
        ============================================================
        """
        try:
            # 1. VERİ HAZIRLAMA
            df = pd.DataFrame(historical_data)
            df['Date'] = pd.to_datetime(df['Date'])
            
            # 2. CİHAZ YAŞI HESAPLAMA: Kurulum tarihinden itibaren geçen süre
            installation_date = pd.to_datetime(device_info['InstallationDate'])
            device_age_days = (datetime.now() - installation_date).days
            # YORUM: Yaşlı cihazlar daha sık bakım gerektirir
            
            # 3. SON BAKIM ANALİZİ: Son bakımdan bu yana geçen süre
            last_maintenance = device_info.get('LastMaintenance')
            if last_maintenance:
                days_since_maintenance = (datetime.now() - pd.to_datetime(last_maintenance)).days
            else:
                days_since_maintenance = device_age_days  # Hiç bakım yapılmamışsa cihaz yaşı kadar
            # YORUM: Bakım süresi uzadıkça aciliyet artar
            
            # 4. PERFORMANS ANALİZİ: Son 30 günlük verileri analiz et
            recent_data = df.tail(30)  # Son 30 günlük veri
            power_variance = recent_data['PowerConsumption'].var()  # Güç tüketimi varyansı
            efficiency_trend = recent_data['PowerConsumption'].pct_change().mean()  # Verimlilik trendi
            # YORUM: Yüksek varyans = Düzensiz çalışma, bakım gerekli
            #        Negatif trend = Verimlilik düşüyor, bakım gerekli
            
            # 5. ACİLİYET SKORU HESAPLAMA: Bakım ihtiyacının aciliyetini belirle
            urgency_score = min(1.0, days_since_maintenance / 365)  # Yıllık bakım varsayımı (0-1 arası)
            # YORUM: 365 gün geçtiyse skor = 1.0 (maksimum aciliyet)
            
            # Performans bozulması durumunda aciliyeti artır
            if power_variance > recent_data['PowerConsumption'].var() * 1.5:  # Varyans 1.5x'ten fazla
                urgency_score += 0.2  # Aciliyet +20%
                # YORUM: Düzensiz çalışma = Bakım gerekli
            if efficiency_trend < -0.05:  # Verimlilik %5'ten fazla düşüş
                urgency_score += 0.3  # Aciliyet +30%
                # YORUM: Verimlilik düşüşü = Bakım gerekli
            
            urgency_score = min(1.0, urgency_score)  # Maksimum 1.0
            
            # 6. BAKIM TÜRÜ BELİRLEME: Aciliyet skoruna göre bakım türünü belirle
            if urgency_score > 0.8:
                maintenance_type = "Acil Bakım"
                risk_level = "Critical"
                # YORUM: Hemen müdahale gerekli, cihaz arıza riski yüksek
            elif urgency_score > 0.6:
                maintenance_type = "Planlı Bakım"
                risk_level = "High"
                # YORUM: 1-2 ay içinde planlanmalı, risk orta-yüksek
            elif urgency_score > 0.4:
                maintenance_type = "Rutin Bakım"
                risk_level = "Medium"
                # YORUM: Rutin takvimde yapılabilir, risk orta
            else:
                maintenance_type = "Önleyici Bakım"
                risk_level = "Low"
                # YORUM: Önleyici amaçlı, risk düşük
            
            return {
                'PredictedMaintenanceDate': (datetime.now() + timedelta(days=365 - days_since_maintenance)).isoformat(),
                'UrgencyScore': float(urgency_score),
                'MaintenanceType': maintenance_type,
                'RecommendedActions': [
                    'Genel temizlik ve kontrol',
                    'Parça değişimi gerekebilir',
                    'Kalibrasyon kontrolü',
                    'Performans testi'
                ],
                'EstimatedCost': float(500 + urgency_score * 1000),
                'RiskLevel': risk_level
            }
        except Exception as e:
            print(f"Error in maintenance prediction: {e}")
            return {
                'PredictedMaintenanceDate': (datetime.now() + timedelta(days=30)).isoformat(),
                'UrgencyScore': 0.5,
                'MaintenanceType': 'Rutin Bakım',
                'RecommendedActions': ['Genel kontrol'],
                'EstimatedCost': 500.0,
                'RiskLevel': 'Medium'
            }
    
    def calculate_efficiency_score(self, device_info, historical_data):
        """
        ============================================================
        VERİMLİLİK SKORU HESAPLAMA - AI/ML SERVİSİ
        ============================================================
        
        AMAÇ: Cihazın genel verimlilik performansını skorlar ve iyileştirme alanlarını belirler.
        
        KULLANILAN YÖNTEM: Çok Boyutlu Performans Analizi
        - Güç verimliliği analizi (power efficiency)
        - Güç faktörü analizi (power factor)
        - Sıcaklık stabilitesi analizi (temperature stability)
        - Voltaj stabilitesi analizi (voltage stability)
        - Ağırlıklı ortalama skorlama (weighted scoring)
        
        GİRDİ VERİLERİ:
        - device_info: Cihaz bilgileri (MaxPowerConsumption vb.)
        - historical_data: Belirli bir süre (örn. 30 gün) performans verileri
        
        ÇIKTI:
        - OverallScore: Genel verimlilik skoru (0-100 arası)
        - EfficiencyLevel: Verimlilik seviyesi (Excellent/Good/Average/Below Average/Poor)
        - Metrics: Detaylı metrikler (her bir performans göstergesi)
        - ImprovementAreas: İyileştirme gereken alanlar
        - BenchmarkComparison: Endüstri standardı ile karşılaştırma
        
        YORUMLAMA:
        - OverallScore >= 90: Mükemmel (Excellent) - Örnek performans
        - OverallScore 80-89: İyi (Good) - Standart üstü
        - OverallScore 70-79: Orta (Average) - Standart seviye
        - OverallScore 60-69: Ortalamanın altı (Below Average) - İyileştirme gerekli
        - OverallScore < 60: Zayıf (Poor) - Acil iyileştirme gerekli
        
        SKOR HESAPLAMA:
        - Güç Verimliliği: %40 ağırlık (en önemli faktör)
        - Güç Faktörü: %30 ağırlık
        - Sıcaklık Stabilitesi: %15 ağırlık
        - Voltaj Stabilitesi: %15 ağırlık
        
        ============================================================
        """
        try:
            # 1. VERİ HAZIRLAMA
            df = pd.DataFrame(historical_data)
            
            # 2. GÜÇ VERİMLİLİĞİ HESAPLAMA: Ortalama güç / Maksimum güç
            avg_power = df['PowerConsumption'].mean()  # Ortalama güç tüketimi (W)
            max_power = device_info['MaxPowerConsumption']  # Maksimum güç kapasitesi (W)
            power_efficiency = (avg_power / max_power) * 100  # Verimlilik yüzdesi
            # YORUM: %85-95 arası ideal, %70'in altı düşük verimlilik
            #        Yüksek değer = Cihaz kapasitesine yakın çalışıyor (iyi)
            
            # 3. GÜÇ FAKTÖRÜ ANALİZİ: Elektriksel verimlilik göstergesi
            avg_power_factor = df['PowerFactor'].mean()  # Ortalama güç faktörü (0-1 arası)
            power_factor_score = avg_power_factor * 100  # Skor (0-100)
            # YORUM: 0.9-1.0 = Mükemmel, 0.8-0.9 = İyi, <0.8 = Düşük (kompanzasyon gerekli)
            #        Güç faktörü düşükse reaktif güç kaybı var demektir
            
            # 4. SICAKLIK STABİLİTESİ: Sıcaklık değişkenliğini analiz et
            temp_std = df['Temperature'].std()  # Standart sapma (°C)
            temp_stability = max(0, 100 - (temp_std * 2))  # Stabilite skoru (0-100)
            # YORUM: Düşük standart sapma = Yüksek stabilite (iyi)
            #        Yüksek standart sapma = Düşük stabilite (kötü)
            #        Formül: 100 - (std * 2) = Stabilite skoru
            
            # 5. VOLTAJ STABİLİTESİ: Voltaj değişkenliğini analiz et
            voltage_std = df['Voltage'].std()  # Standart sapma (V)
            voltage_stability = max(0, 100 - (voltage_std * 5))  # Stabilite skoru (0-100)
            # YORUM: Düşük standart sapma = Yüksek stabilite (iyi)
            #        Yüksek standart sapma = Düşük stabilite (kötü, elektrik sorunu olabilir)
            #        Formül: 100 - (std * 5) = Stabilite skoru
            
            # 6. GENEL SKOR HESAPLAMA: Ağırlıklı ortalama ile toplam skor
            overall_score = (power_efficiency * 0.4 +      # %40 ağırlık (en önemli)
                           power_factor_score * 0.3 +      # %30 ağırlık
                           temp_stability * 0.15 +         # %15 ağırlık
                           voltage_stability * 0.15)        # %15 ağırlık
            # YORUM: Her metrik farklı ağırlıkla toplam skora katkı sağlar
            #        Güç verimliliği en önemli faktördür (%40)
            
            efficiency_level = self._get_efficiency_level(overall_score)  # Seviye belirleme
            
            # 7. DETAYLI METRİKLER: Her performans göstergesini ayrı ayrı raporla
            metrics = [
                {
                    'MetricName': 'Güç Verimliliği',
                    'Value': float(power_efficiency),  # Gerçek değer (%)
                    'Benchmark': 85.0,  # Endüstri standardı (%)
                    'Score': float(min(100, power_efficiency)),  # Skor (0-100)
                    'Unit': '%',
                    'Interpretation': f'{power_efficiency:.1f}% verimlilik. Hedef: 85%+. ' + 
                                    ('✅ Standart üstü' if power_efficiency >= 85 else '⚠️ İyileştirme gerekli')
                },
                {
                    'MetricName': 'Güç Faktörü',
                    'Value': float(avg_power_factor),  # Gerçek değer (0-1)
                    'Benchmark': 0.9,  # Endüstri standardı
                    'Score': float(power_factor_score),  # Skor (0-100)
                    'Unit': '',
                    'Interpretation': f'{avg_power_factor:.2f} güç faktörü. Hedef: 0.9+. ' +
                                    ('✅ İyi' if avg_power_factor >= 0.9 else '⚠️ Kompanzasyon gerekli' if avg_power_factor < 0.8 else 'ℹ️ Orta')
                },
                {
                    'MetricName': 'Sıcaklık Stabilitesi',
                    'Value': float(temp_stability),  # Stabilite skoru (%)
                    'Benchmark': 90.0,  # Endüstri standardı (%)
                    'Score': float(temp_stability),  # Skor (0-100)
                    'Unit': '%',
                    'Interpretation': f'{temp_stability:.1f}% stabilite. Hedef: 90%+. ' +
                                    ('✅ Stabil' if temp_stability >= 90 else '⚠️ İyileştirme gerekli')
                },
                {
                    'MetricName': 'Voltaj Stabilitesi',
                    'Value': float(voltage_stability),  # Stabilite skoru (%)
                    'Benchmark': 90.0,  # Endüstri standardı (%)
                    'Score': float(voltage_stability),  # Skor (0-100)
                    'Unit': '%',
                    'Interpretation': f'{voltage_stability:.1f}% stabilite. Hedef: 90%+. ' +
                                    ('✅ Stabil' if voltage_stability >= 90 else '⚠️ Elektrik sistemi kontrol edilmeli')
                }
            ]
            
            # 8. İYİLEŞTİRME ALANLARI: Düşük performans gösteren alanları belirle
            improvement_areas = []
            if power_efficiency < 80:
                improvement_areas.append({
                    'Area': 'Enerji Verimliliği',
                    'CurrentValue': f'{power_efficiency:.1f}%',
                    'TargetValue': '85%+',
                    'Priority': 'High',
                    'Description': 'Cihazın enerji verimliliği düşük. Bakım ve optimizasyon gerekli.'
                })
            if avg_power_factor < 0.8:
                improvement_areas.append({
                    'Area': 'Güç Faktörü',
                    'CurrentValue': f'{avg_power_factor:.2f}',
                    'TargetValue': '0.9+',
                    'Priority': 'High',
                    'Description': 'Güç faktörü düşük, reaktif güç kaybı var. Kompanzasyon sistemi gerekli.'
                })
            if temp_stability < 80:
                improvement_areas.append({
                    'Area': 'Sıcaklık Kontrolü',
                    'CurrentValue': f'{temp_stability:.1f}%',
                    'TargetValue': '90%+',
                    'Priority': 'Medium',
                    'Description': 'Sıcaklık değişkenliği yüksek. Soğutma ve havalandırma sistemi iyileştirilmeli.'
                })
            if voltage_stability < 80:
                improvement_areas.append({
                    'Area': 'Voltaj Stabilitesi',
                    'CurrentValue': f'{voltage_stability:.1f}%',
                    'TargetValue': '90%+',
                    'Priority': 'High',
                    'Description': 'Voltaj değişkenliği yüksek. Elektrik sistemi kontrol edilmeli.'
                })
            
            # 9. SONUÇ HAZIRLAMA: Tüm analiz sonuçlarını yapılandırılmış formatta döndür
            return {
                'OverallScore': float(overall_score),  # Genel verimlilik skoru (0-100)
                'EfficiencyLevel': efficiency_level,  # Seviye (Excellent/Good/Average/Below Average/Poor)
                'Metrics': metrics,  # Detaylı metrikler
                'ImprovementAreas': improvement_areas,  # İyileştirme gereken alanlar
                'BenchmarkComparison': float(overall_score - 85),  # Endüstri standardı ile fark
                'Interpretation': f'Genel verimlilik skoru: {overall_score:.1f}/100 ({efficiency_level}). ' +
                                ('✅ Standart üstü performans' if overall_score >= 85 else 
                                 '⚠️ İyileştirme alanları mevcut' if overall_score >= 70 else
                                 '❌ Acil iyileştirme gerekli')
            }
            # YORUMLAMA REHBERİ:
            # - OverallScore: 0-100 arası skor, 85+ = Standart üstü
            # - BenchmarkComparison: Pozitif = Standart üstü, Negatif = Standart altı
            # - ImprovementAreas: Priority'ye göre önceliklendirilmiş iyileştirme alanları
            # - Her metrik için Interpretation: Mevcut durum ve hedef değerler
        except Exception as e:
            print(f"Error in efficiency calculation: {e}")
            return {
                'OverallScore': 0.0,
                'EfficiencyLevel': 'Poor',
                'Metrics': [],
                'ImprovementAreas': ['Veri yetersiz'],
                'BenchmarkComparison': 0.0
            }
    
    def _classify_anomaly(self, row, features):
        """Anomali türünü sınıflandır"""
        if row['PowerConsumption'] > row['EnergyConsumption'] * 2:
            return 'HighConsumption'
        elif row['Temperature'] > 50:
            return 'TemperatureSpike'
        elif row['Voltage'] < 200 or row['Voltage'] > 250:
            return 'VoltageAnomaly'
        else:
            return 'GeneralAnomaly'
    
    def _get_anomaly_recommendation(self, anomaly_type):
        """Anomali türüne göre öneri"""
        recommendations = {
            'HighConsumption': 'Cihazın bakımını kontrol edin',
            'TemperatureSpike': 'Soğutma sistemini kontrol edin',
            'VoltageAnomaly': 'Elektrik sistemini kontrol edin',
            'GeneralAnomaly': 'Genel sistem kontrolü yapın'
        }
        return recommendations.get(anomaly_type, 'Sistem kontrolü gerekli')
    
    def _get_efficiency_level(self, score):
        """Verimlilik seviyesini belirle"""
        if score >= 90:
            return 'Excellent'
        elif score >= 80:
            return 'Good'
        elif score >= 70:
            return 'Average'
        elif score >= 60:
            return 'Below Average'
        else:
            return 'Poor'

# ML servisini başlat
ml_service = EnergyMLService()

@app.route('/predict-energy', methods=['POST'])
def predict_energy():
    data = request.json
    result = ml_service.predict_energy_consumption(
        data['HistoricalData'], 
        data['DaysAhead']
    )
    return jsonify(result)

@app.route('/detect-anomalies', methods=['POST'])
def detect_anomalies():
    data = request.json
    result = ml_service.detect_anomalies(data['Data'])
    return jsonify(result)

@app.route('/optimize-energy', methods=['POST'])
def optimize_energy():
    data = request.json
    result = ml_service.optimize_energy(
        data, 
        data['HistoricalData']
    )
    return jsonify(result)

@app.route('/predict-maintenance', methods=['POST'])
def predict_maintenance():
    data = request.json
    result = ml_service.predict_maintenance(
        data, 
        data['HistoricalData']
    )
    return jsonify(result)

@app.route('/calculate-efficiency', methods=['POST'])
def calculate_efficiency():
    data = request.json
    result = ml_service.calculate_efficiency_score(
        data, 
        data['HistoricalData']
    )
    return jsonify(result)

@app.route('/health', methods=['GET'])
def health_check():
    return jsonify({'status': 'healthy', 'timestamp': datetime.now(timezone.utc).isoformat()})

# ============================================================================
# RabbitMQ Consumer ve API'ye Geri Gönderme
# ============================================================================

# API endpoint ayarları
API_BASE_URL = os.getenv('API_BASE_URL', 'https://localhost:5001')
API_CALLBACK_URL = f"{API_BASE_URL}/api/EnergyApi/ml-results"
API_VERIFY_SSL = os.getenv('API_VERIFY_SSL', 'false').lower() == 'true'

# RabbitMQ ayarları
RABBITMQ_HOST = os.getenv('RABBITMQ_HOST', 'localhost')
RABBITMQ_PORT = int(os.getenv('RABBITMQ_PORT', '5672'))
RABBITMQ_USER = os.getenv('RABBITMQ_USER', 'guest')
RABBITMQ_PASS = os.getenv('RABBITMQ_PASS', 'guest')
RABBITMQ_QUEUE = os.getenv('RABBITMQ_QUEUE', 'sensor-data')
RABBITMQ_RESULTS_QUEUE = os.getenv('RABBITMQ_RESULTS_QUEUE', 'ml-results')


class MLResultSender:
    """ML sonuçlarını API'ye JSON formatında gönderen sınıf"""
    
    def __init__(self):
        self.session = requests.Session()
        if not API_VERIFY_SSL:
            self.session.verify = False
            import urllib3
            urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
        
        # RabbitMQ connection pooling için global connection ve channel
        self._rabbitmq_connection = None
        self._rabbitmq_channel = None
        self._rabbitmq_lock = threading.Lock()
    
    def send_to_api(self, device_id: int, result_type: str, result_data: Dict[str, Any]) -> bool:
        """ML sonuçlarını API'ye JSON formatında gönderir"""
        try:
            payload = {
                'deviceId': device_id,
                'resultType': result_type,
                'resultData': result_data,
                'processedAt': datetime.now(timezone.utc).isoformat(),
                'mlServiceVersion': '1.0'
            }
            
            response = self.session.post(
                API_CALLBACK_URL,
                json=payload,
                headers={'Content-Type': 'application/json'},
                timeout=10
            )
            
            if response.status_code in [200, 201]:
                print(f"✓ ML sonucu API'ye gönderildi: Device {device_id}, Type: {result_type}")
                return True
            else:
                print(f"✗ API gönderim hatası: {response.status_code} - {response.text}")
                return False
                
        except Exception as e:
            print(f"✗ API gönderim hatası: {str(e)}")
            return False
    
    def _ensure_rabbitmq_connection(self) -> bool:
        """RabbitMQ bağlantısını kontrol et ve gerekirse yeniden oluştur"""
        with self._rabbitmq_lock:
            try:
                # Bağlantı var mı ve açık mı kontrol et
                if self._rabbitmq_connection is None or self._rabbitmq_connection.is_closed:
                    self._rabbitmq_connection = pika.BlockingConnection(
                        pika.ConnectionParameters(
                            host=RABBITMQ_HOST,
                            port=RABBITMQ_PORT,
                            credentials=pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASS),
                            heartbeat=600,
                            blocked_connection_timeout=300
                        )
                    )
                    self._rabbitmq_channel = self._rabbitmq_connection.channel()
                    # Results queue'yu tanımla
                    self._rabbitmq_channel.queue_declare(queue=RABBITMQ_RESULTS_QUEUE, durable=True)
                    print("✓ RabbitMQ bağlantısı oluşturuldu (connection pooling)")
                
                # Channel var mı ve açık mı kontrol et
                if self._rabbitmq_channel is None or self._rabbitmq_channel.is_closed:
                    if self._rabbitmq_connection and not self._rabbitmq_connection.is_closed:
                        self._rabbitmq_channel = self._rabbitmq_connection.channel()
                        self._rabbitmq_channel.queue_declare(queue=RABBITMQ_RESULTS_QUEUE, durable=True)
                    else:
                        return False
                
                return True
            except Exception as e:
                print(f"⚠ RabbitMQ bağlantı hatası: {str(e)}")
                # Bağlantıyı temizle, bir sonraki çağrıda yeniden oluşturulacak
                self._rabbitmq_connection = None
                self._rabbitmq_channel = None
                return False
    
    def send_to_rabbitmq(self, result_type: str, result_data: Dict[str, Any]) -> bool:
        """ML sonuçlarını RabbitMQ'ya JSON formatında gönderir (connection pooling ile)"""
        try:
            # Bağlantıyı kontrol et ve gerekirse oluştur
            if not self._ensure_rabbitmq_connection():
                return False
            
            # Mesajı gönder
            payload = {
                'resultType': result_type,
                'resultData': result_data,
                'processedAt': datetime.now(timezone.utc).isoformat()
            }
            
            message = json.dumps(payload)
            
            with self._rabbitmq_lock:
                self._rabbitmq_channel.basic_publish(
                    exchange='',
                    routing_key=RABBITMQ_RESULTS_QUEUE,
                    body=message,
                    properties=pika.BasicProperties(
                        delivery_mode=2,  # Mesajı kalıcı yap
                        content_type='application/json'
                    )
                )
            
            print(f"✓ ML sonucu RabbitMQ'ya gönderildi: Type: {result_type}")
            return True
            
        except Exception as e:
            print(f"✗ RabbitMQ gönderim hatası: {str(e)}")
            # Hata durumunda bağlantıyı temizle
            with self._rabbitmq_lock:
                self._rabbitmq_connection = None
                self._rabbitmq_channel = None
            return False
    
    def close_rabbitmq_connection(self):
        """RabbitMQ bağlantısını kapat (cleanup için)"""
        with self._rabbitmq_lock:
            try:
                if self._rabbitmq_channel and not self._rabbitmq_channel.is_closed:
                    self._rabbitmq_channel.close()
                if self._rabbitmq_connection and not self._rabbitmq_connection.is_closed:
                    self._rabbitmq_connection.close()
            except:
                pass
            finally:
                self._rabbitmq_connection = None
                self._rabbitmq_channel = None


# ML sonuç gönderici
result_sender = MLResultSender()


def process_sensor_data(message_data: Dict[str, Any]) -> None:
    """Sensor verisini işler ve ML analizleri yapar"""
    try:
        device_id = message_data.get('deviceId')
        if not device_id:
            print("✗ DeviceId bulunamadı")
            return
        
        # Tek veri noktası için anomali kontrolü
        single_data_point = {
            'Date': message_data.get('recordedAt', datetime.now(timezone.utc).isoformat()),
            'EnergyConsumption': message_data.get('energyUsed', 0),
            'PowerConsumption': message_data.get('powerConsumption', 0),
            'Temperature': message_data.get('temperature', 0),
            'Voltage': message_data.get('voltage', 0),
            'Current': message_data.get('current', 0),
            'PowerFactor': message_data.get('powerFactor', 0)
        }
        
        # Anomali tespiti
        anomalies = ml_service.detect_anomalies([single_data_point])
        
        if anomalies:
            # Anomali bulundu - API'ye gönder
            anomaly_result = {
                'anomalies': anomalies,
                'deviceId': device_id,
                'originalData': message_data
            }
            result_sender.send_to_api(device_id, 'anomaly_detection', anomaly_result)
            result_sender.send_to_rabbitmq('anomaly_detection', anomaly_result)
        
        # Verimlilik skoru hesaplama (basit)
        efficiency_data = {
            'deviceId': device_id,
            'powerConsumption': message_data.get('powerConsumption', 0),
            'powerFactor': message_data.get('powerFactor', 0),
            'temperature': message_data.get('temperature', 0),
            'voltage': message_data.get('voltage', 0)
        }
        
        # Basit verimlilik skoru
        power_factor_score = efficiency_data['powerFactor'] * 100
        voltage_stability = 100 - abs(efficiency_data['voltage'] - 220) * 2
        temp_stability = 100 - abs(efficiency_data['temperature'] - 25) * 2
        
        overall_score = (power_factor_score * 0.5 + voltage_stability * 0.25 + temp_stability * 0.25)
        
        efficiency_result = {
            'deviceId': device_id,
            'overallScore': float(overall_score),
            'efficiencyLevel': ml_service._get_efficiency_level(overall_score),
            'metrics': [
                {
                    'metricName': 'Güç Faktörü',
                    'value': float(efficiency_data['powerFactor']),
                    'score': float(power_factor_score),
                    'unit': ''
                },
                {
                    'metricName': 'Voltaj Stabilitesi',
                    'value': float(voltage_stability),
                    'score': float(voltage_stability),
                    'unit': '%'
                }
            ],
            'processedAt': datetime.now().isoformat()
        }
        
        # Verimlilik sonuçlarını gönder
        result_sender.send_to_api(device_id, 'efficiency_score', efficiency_result)
        result_sender.send_to_rabbitmq('efficiency_score', efficiency_result)
        
    except Exception as e:
        print(f"✗ Sensor verisi işleme hatası: {str(e)}")


def rabbitmq_callback(ch, method, properties, body):
    """RabbitMQ mesaj callback fonksiyonu"""
    try:
        message_data = json.loads(body.decode('utf-8'))
        device_id = message_data.get('deviceId')
        recorded_at = message_data.get('recordedAt')
        
        # Mesajın zamanını kontrol et - eğer 5 dakikadan eskiyse işleme (eski mesajlar için alert oluşturma)
        if recorded_at:
            try:
                from datetime import timezone
                # ISO format string'i parse et
                message_time_str = recorded_at.replace('Z', '+00:00')
                message_time = datetime.fromisoformat(message_time_str)
                
                # Eğer timezone bilgisi yoksa UTC olarak kabul et
                if message_time.tzinfo is None:
                    message_time = message_time.replace(tzinfo=timezone.utc)
                
                # Şimdiki zamanı UTC olarak al (her zaman UTC kullan)
                now = datetime.now(timezone.utc)
                
                # Zaman farkını hesapla
                time_diff = now - message_time
                if time_diff.total_seconds() > 300:  # 5 dakikadan eski mesajlar
                    print(f"⚠ Eski mesaj atlandı: Device {device_id}, Yaş: {time_diff.total_seconds():.0f} saniye")
                    ch.basic_ack(delivery_tag=method.delivery_tag)
                    return
            except Exception as time_ex:
                print(f"⚠ Tarih parse hatası, mesaj işleniyor: {str(time_ex)}")
        
        print(f"📥 RabbitMQ'dan mesaj alındı: Device {device_id}")
        
        # ML işlemlerini yap
        process_sensor_data(message_data)
        
        # Mesajı onayla
        ch.basic_ack(delivery_tag=method.delivery_tag)
        
    except Exception as e:
        print(f"✗ RabbitMQ callback hatası: {str(e)}")
        import traceback
        traceback.print_exc()
        ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)


def start_rabbitmq_consumer():
    """RabbitMQ consumer'ı başlatır (retry mekanizması ile)"""
    import traceback
    import time
    
    # Exchange adı (.NET tarafıyla aynı olmalı)
    exchange_name = os.getenv('RABBITMQ_EXCHANGE', 'aygaz.sensors')
    routing_key = f'sensor.{RABBITMQ_QUEUE}'  # sensor.sensor-data
    
    # Retry mekanizması: RabbitMQ hazır olana kadar dene
    max_retries = 10
    retry_delay = 3  # 3 saniye
    connection = None
    
    for attempt in range(1, max_retries + 1):
        try:
            print(f"🔄 RabbitMQ bağlantısı deneniyor ({attempt}/{max_retries}): {RABBITMQ_HOST}:{RABBITMQ_PORT}")
            print(f"   Exchange: {exchange_name}, Queue: {RABBITMQ_QUEUE}, RoutingKey: {routing_key}")
            
            connection = pika.BlockingConnection(
                pika.ConnectionParameters(
                    host=RABBITMQ_HOST,
                    port=RABBITMQ_PORT,
                    credentials=pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASS),
                    heartbeat=600,
                    blocked_connection_timeout=300,
                    connection_attempts=3,
                    retry_delay=2
                )
            )
            print(f"✓ RabbitMQ bağlantısı başarılı! (Deneme {attempt})")
            break  # Bağlantı başarılı, döngüden çık
            
        except (pika.exceptions.AMQPConnectionError, Exception) as e:
            if attempt < max_retries:
                print(f"⚠ Deneme {attempt} başarısız: {str(e)}")
                print(f"   {retry_delay} saniye sonra tekrar denenecek...")
                time.sleep(retry_delay)
            else:
                print(f"✗ RabbitMQ bağlantısı kurulamadı ({max_retries} deneme sonrası)")
                print(f"   Host: {RABBITMQ_HOST}, Port: {RABBITMQ_PORT}")
                print("⚠ RabbitMQ bağlantısı kurulamadı, sadece HTTP endpoint'leri çalışacak")
                traceback.print_exc()
                return
    
    if connection is None:
        return
    
    try:
        channel = connection.channel()
        
        # Exchange'i tanımla (Topic exchange)
        channel.exchange_declare(
            exchange=exchange_name,
            exchange_type='topic',
            durable=True,
            auto_delete=False
        )
        
        # Queue'yu tanımla
        channel.queue_declare(queue=RABBITMQ_QUEUE, durable=True)
        
        # Queue'yu exchange'e bind et
        channel.queue_bind(
            exchange=exchange_name,
            queue=RABBITMQ_QUEUE,
            routing_key=routing_key
        )
        
        # Consumer ayarları
        channel.basic_qos(prefetch_count=1)
        channel.basic_consume(
            queue=RABBITMQ_QUEUE,
            on_message_callback=rabbitmq_callback
        )
        
        print(f"✓ RabbitMQ consumer başlatıldı!")
        print(f"   Exchange: {exchange_name}")
        print(f"   Queue: {RABBITMQ_QUEUE}")
        print(f"   RoutingKey: {routing_key}")
        print("📡 Mesaj kuyruğundan veri bekleniyor...")
        
        channel.start_consuming()
        
    except Exception as e:
        print(f"✗ RabbitMQ consumer hatası: {type(e).__name__}: {str(e)}")
        print("⚠ RabbitMQ consumer başlatılamadı, sadece HTTP endpoint'leri çalışacak")
        traceback.print_exc()
        if connection and not connection.is_closed:
            try:
                connection.close()
            except:
                pass


# RabbitMQ consumer'ı ayrı thread'de başlat
def start_consumer_thread():
    """Consumer thread'ini başlatır"""
    consumer_thread = threading.Thread(target=start_rabbitmq_consumer, daemon=True)
    consumer_thread.start()
    return consumer_thread


# API'ye sonuç gönderme endpoint'i (manuel test için)
@app.route('/send-result', methods=['POST'])
def send_result_manually():
    """Manuel olarak ML sonucu gönderme endpoint'i"""
    try:
        data = request.json
        device_id = data.get('deviceId')
        result_type = data.get('resultType', 'custom')
        result_data = data.get('resultData', {})
        
        if not device_id:
            return jsonify({'error': 'deviceId gerekli'}), 400
        
        success = result_sender.send_to_api(device_id, result_type, result_data)
        
        if success:
            return jsonify({
                'status': 'success',
                'message': 'Sonuç API\'ye gönderildi',
                'deviceId': device_id,
                'resultType': result_type
            })
        else:
            return jsonify({
                'status': 'error',
                'message': 'API\'ye gönderim başarısız'
            }), 500
            
    except Exception as e:
        return jsonify({'error': str(e)}), 500


if __name__ == '__main__':
    # RabbitMQ consumer'ı başlat
    try:
        consumer_thread = start_consumer_thread()
        print("✓ RabbitMQ consumer thread başlatıldı")
    except Exception as e:
        print(f"⚠ RabbitMQ consumer başlatılamadı: {str(e)}")
        print("⚠ Sadece HTTP endpoint'leri çalışacak")
    
    # Flask uygulamasını başlat (sadece development için)
    # Production'da gunicorn kullanılır (Dockerfile'da CMD ile)
    print("🚀 Python ML Servisi başlatılıyor (Flask dev server)...")
    print(f"📡 API Callback URL: {API_CALLBACK_URL}")
    print(f"📡 RabbitMQ: {RABBITMQ_HOST}:{RABBITMQ_PORT}")
    app.run(host='0.0.0.0', port=5000, debug=True, threaded=True)

