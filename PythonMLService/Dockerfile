FROM python:3.11-slim

WORKDIR /app

# Sistem bağımlılıklarını yükle
RUN apt-get update && apt-get install -y \
    gcc \
    && rm -rf /var/lib/apt/lists/*

# Python bağımlılıklarını kopyala ve yükle
COPY requirements.txt .
# Timeout ve retry ayarları ile pip install (network sorunlarını önlemek için)
RUN pip install --no-cache-dir --timeout=300 --retries=5 -r requirements.txt

# Uygulama dosyalarını kopyala
COPY app.py .
COPY gunicorn_config.py .

# Port
EXPOSE 5000

# Ortam değişkenleri
ENV PYTHONUNBUFFERED=1
ENV FLASK_APP=app.py
ENV FLASK_ENV=production

# Servisi başlat (gunicorn ile - 4 worker thread ile aynı anda birden fazla istek işlenebilir)
# --preload kullanmıyoruz çünkü RabbitMQ consumer'ı on_starting hook'unda başlatıyoruz
CMD ["gunicorn", "--config", "gunicorn_config.py", "app:app"]

