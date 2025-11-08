from flask import Flask, request, jsonify
import pandas as pd
import numpy as np
from sklearn.ensemble import IsolationForest
from sklearn.preprocessing import StandardScaler
from sklearn.linear_model import LinearRegression
from sklearn.metrics import mean_absolute_error
import joblib
import os
from datetime import datetime, timedelta
import warnings
warnings.filterwarnings('ignore')

app = Flask(__name__)

# Model dosyaları için klasör
MODEL_DIR = 'models'
if not os.path.exists(MODEL_DIR):
    os.makedirs(MODEL_DIR)

class EnergyMLService:
    def __init__(self):
        self.scaler = StandardScaler()
        self.anomaly_detector = IsolationForest(contamination=0.1, random_state=42)
        self.energy_predictor = LinearRegression()
        
    def predict_energy_consumption(self, historical_data, days_ahead):
        """Enerji tüketimi tahmini"""
        try:
            df = pd.DataFrame(historical_data)
            df['Date'] = pd.to_datetime(df['Date'])
            df = df.sort_values('Date')
            
            # Özellik mühendisliği
            df['DayOfWeek'] = df['Date'].dt.dayofweek
            df['Hour'] = df['Date'].dt.hour
            df['Month'] = df['Date'].dt.month
            
            # Trend hesaplama
            df['EnergyTrend'] = df['EnergyConsumption'].rolling(window=7).mean()
            df['PowerTrend'] = df['PowerConsumption'].rolling(window=7).mean()
            
            # Eksik değerleri doldur
            df = df.fillna(method='ffill').fillna(method='bfill')
            
            # Özellikler
            features = ['EnergyConsumption', 'PowerConsumption', 'Temperature', 
                      'Voltage', 'Current', 'PowerFactor', 'DayOfWeek', 'Hour', 'Month']
            
            X = df[features].values
            y = df['EnergyConsumption'].values
            
            # Model eğitimi
            self.energy_predictor.fit(X, y)
            
            # Tahmin için son veriyi kullan
            last_data = df.iloc[-1][features].values.reshape(1, -1)
            
            # Gelecek günler için tahmin
            predictions = []
            current_data = last_data.copy()
            
            for day in range(1, days_ahead + 1):
                # Tarih güncelleme
                future_date = df['Date'].iloc[-1] + timedelta(days=day)
                current_data[0][6] = future_date.dayofweek  # DayOfWeek
                current_data[0][7] = 12  # Saat (varsayılan)
                current_data[0][8] = future_date.month  # Month
                
                pred = self.energy_predictor.predict(current_data)[0]
                predictions.append(pred)
                
                # Sonraki tahmin için veriyi güncelle
                current_data[0][0] = pred  # EnergyConsumption
            
            # Güven aralığı hesaplama
            mae = mean_absolute_error(y, self.energy_predictor.predict(X))
            confidence = max(0.1, min(0.9, 1 - (mae / np.mean(y))))
            
            return {
                'PredictionDate': (datetime.now() + timedelta(days=days_ahead)).isoformat(),
                'PredictedEnergyConsumption': float(predictions[-1]),
                'ConfidenceLevel': float(confidence),
                'MinPrediction': float(predictions[-1] * 0.8),
                'MaxPrediction': float(predictions[-1] * 1.2),
                'Factors': [
                    {
                        'FactorName': 'Tarihsel Trend',
                        'Impact': float(np.mean(np.diff(predictions))),
                        'Description': 'Geçmiş verilere dayalı trend analizi'
                    },
                    {
                        'FactorName': 'Sıcaklık Etkisi',
                        'Impact': float(df['Temperature'].corr(df['EnergyConsumption'])),
                        'Description': 'Sıcaklık ile enerji tüketimi korelasyonu'
                    }
                ]
            }
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
        """Anomali tespiti"""
        try:
            df = pd.DataFrame(data)
            df['Date'] = pd.to_datetime(df['Date'])
            
            # Özellikler
            features = ['EnergyConsumption', 'PowerConsumption', 'Temperature', 
                      'Voltage', 'Current', 'PowerFactor']
            
            X = df[features].values
            
            # Anomali tespiti
            anomaly_labels = self.anomaly_detector.fit_predict(X)
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
            return []
    
    def optimize_energy(self, device_info, historical_data):
        """Enerji optimizasyonu önerileri"""
        try:
            df = pd.DataFrame(historical_data)
            df['Date'] = pd.to_datetime(df['Date'])
            
            # Verimlilik analizi
            avg_power = df['PowerConsumption'].mean()
            max_power = device_info['MaxPowerConsumption']
            efficiency = (avg_power / max_power) * 100
            
            # Trend analizi
            power_trend = df['PowerConsumption'].pct_change().mean()
            energy_trend = df['EnergyConsumption'].pct_change().mean()
            
            actions = []
            
            # Verimlilik önerileri
            if efficiency < 70:
                actions.append({
                    'ActionName': 'Enerji Verimliliği İyileştirmesi',
                    'Description': 'Cihazın enerji verimliliği düşük. Bakım ve optimizasyon gerekli.',
                    'Category': 'Efficiency',
                    'PotentialSavings': 200.0,
                    'EnergyReduction': 50.0,
                    'ImplementationCost': 1000.0,
                    'PaybackPeriod': 5,
                    'Priority': 'High',
                    'Steps': [
                        'Cihazın periyodik bakımını yapın',
                        'Eski parçaları yenileyin',
                        'Kullanım saatlerini optimize edin'
                    ]
                })
            
            # Zaman bazlı optimizasyon
            if power_trend > 0.1:
                actions.append({
                    'ActionName': 'Zaman Bazlı Kullanım Optimizasyonu',
                    'Description': 'Enerji tüketimi artış trendinde. Kullanım saatlerini optimize edin.',
                    'Category': 'Schedule',
                    'PotentialSavings': 150.0,
                    'EnergyReduction': 30.0,
                    'ImplementationCost': 500.0,
                    'PaybackPeriod': 3,
                    'Priority': 'Medium',
                    'Steps': [
                        'Pik saatlerde kullanımı azaltın',
                        'Gece saatlerinde çalıştırın',
                        'Hafta sonu kullanımını artırın'
                    ]
                })
            
            # Sıcaklık optimizasyonu
            temp_correlation = df['Temperature'].corr(df['EnergyConsumption'])
            if temp_correlation > 0.5:
                actions.append({
                    'ActionName': 'Sıcaklık Kontrolü',
                    'Description': 'Yüksek sıcaklık enerji tüketimini artırıyor.',
                    'Category': 'Temperature',
                    'PotentialSavings': 100.0,
                    'EnergyReduction': 20.0,
                    'ImplementationCost': 2000.0,
                    'PaybackPeriod': 20,
                    'Priority': 'Medium',
                    'Steps': [
                        'Soğutma sistemini kontrol edin',
                        'Havalandırma iyileştirin',
                        'Gölgelendirme ekleyin'
                    ]
                })
            
            return {
                'Actions': actions,
                'PotentialSavings': sum(action['PotentialSavings'] for action in actions),
                'EnergyReduction': sum(action['EnergyReduction'] for action in actions),
                'CarbonReduction': sum(action['EnergyReduction'] for action in actions) * 0.4,
                'ImplementationCost': sum(action['ImplementationCost'] for action in actions),
                'PaybackPeriod': max(action['PaybackPeriod'] for action in actions) if actions else 0
            }
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
        """Bakım tahmini"""
        try:
            df = pd.DataFrame(historical_data)
            df['Date'] = pd.to_datetime(df['Date'])
            
            # Cihaz yaşı
            installation_date = pd.to_datetime(device_info['InstallationDate'])
            device_age_days = (datetime.now() - installation_date).days
            
            # Son bakım
            last_maintenance = device_info.get('LastMaintenance')
            if last_maintenance:
                days_since_maintenance = (datetime.now() - pd.to_datetime(last_maintenance)).days
            else:
                days_since_maintenance = device_age_days
            
            # Performans analizi
            recent_data = df.tail(30)  # Son 30 gün
            power_variance = recent_data['PowerConsumption'].var()
            efficiency_trend = recent_data['PowerConsumption'].pct_change().mean()
            
            # Aciliyet skoru
            urgency_score = min(1.0, days_since_maintenance / 365)  # Yıllık bakım varsayımı
            if power_variance > recent_data['PowerConsumption'].var() * 1.5:
                urgency_score += 0.2
            if efficiency_trend < -0.05:  # Verimlilik düşüşü
                urgency_score += 0.3
            
            # Bakım türü belirleme
            if urgency_score > 0.8:
                maintenance_type = "Acil Bakım"
                risk_level = "Critical"
            elif urgency_score > 0.6:
                maintenance_type = "Planlı Bakım"
                risk_level = "High"
            elif urgency_score > 0.4:
                maintenance_type = "Rutin Bakım"
                risk_level = "Medium"
            else:
                maintenance_type = "Önleyici Bakım"
                risk_level = "Low"
            
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
        """Verimlilik skoru hesaplama"""
        try:
            df = pd.DataFrame(historical_data)
            
            # Temel metrikler
            avg_power = df['PowerConsumption'].mean()
            max_power = device_info['MaxPowerConsumption']
            power_efficiency = (avg_power / max_power) * 100
            
            avg_power_factor = df['PowerFactor'].mean()
            power_factor_score = avg_power_factor * 100
            
            # Sıcaklık stabilitesi
            temp_std = df['Temperature'].std()
            temp_stability = max(0, 100 - (temp_std * 2))
            
            # Voltaj stabilitesi
            voltage_std = df['Voltage'].std()
            voltage_stability = max(0, 100 - (voltage_std * 5))
            
            # Genel skor
            overall_score = (power_efficiency * 0.4 + power_factor_score * 0.3 + 
                           temp_stability * 0.15 + voltage_stability * 0.15)
            
            efficiency_level = self._get_efficiency_level(overall_score)
            
            metrics = [
                {
                    'MetricName': 'Güç Verimliliği',
                    'Value': float(power_efficiency),
                    'Benchmark': 85.0,
                    'Score': float(min(100, power_efficiency)),
                    'Unit': '%'
                },
                {
                    'MetricName': 'Güç Faktörü',
                    'Value': float(avg_power_factor),
                    'Benchmark': 0.9,
                    'Score': float(power_factor_score),
                    'Unit': ''
                },
                {
                    'MetricName': 'Sıcaklık Stabilitesi',
                    'Value': float(temp_stability),
                    'Benchmark': 90.0,
                    'Score': float(temp_stability),
                    'Unit': '%'
                }
            ]
            
            improvement_areas = []
            if power_efficiency < 80:
                improvement_areas.append('Enerji verimliliği iyileştirilmeli')
            if avg_power_factor < 0.8:
                improvement_areas.append('Güç faktörü düşük, kompanzasyon gerekli')
            if temp_stability < 80:
                improvement_areas.append('Sıcaklık kontrolü iyileştirilmeli')
            
            return {
                'OverallScore': float(overall_score),
                'EfficiencyLevel': efficiency_level,
                'Metrics': metrics,
                'ImprovementAreas': improvement_areas,
                'BenchmarkComparison': float(overall_score - 85)
            }
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
    return jsonify({'status': 'healthy', 'timestamp': datetime.now().isoformat()})

if __name__ == '__main__':
    app.run(host='0.0.0.0', port=5000, debug=True)

