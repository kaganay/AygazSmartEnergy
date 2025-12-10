"""Gunicorn configuration file"""
import os

# Gunicorn worker sayısı
workers = int(os.getenv('GUNICORN_WORKERS', '4'))
threads = int(os.getenv('GUNICORN_THREADS', '2'))
bind = f"0.0.0.0:{os.getenv('PORT', '5000')}"
timeout = int(os.getenv('GUNICORN_TIMEOUT', '30'))  # 30 saniye (optimize edildi: 120 → 30)
worker_class = 'gthread'
accesslog = '-'
errorlog = '-'
loglevel = 'info'

# RabbitMQ consumer'ı sadece master process'te başlat
def on_starting(server):
    """Gunicorn başlatıldığında çağrılır (master process'te)"""
    print("🚀 Gunicorn başlatılıyor...")
    try:
        # Import'u burada yapıyoruz çünkü --preload kullanmıyoruz
        from app import start_consumer_thread
        consumer_thread = start_consumer_thread()
        print("✓ RabbitMQ consumer thread başlatıldı (master process)")
    except Exception as e:
        print(f"⚠ RabbitMQ consumer başlatılamadı: {str(e)}")
        print("⚠ Sadece HTTP endpoint'leri çalışacak")

