using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AygazSmartEnergy.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace AygazSmartEnergy.Services
{
    /// <summary>
    /// RabbitMQ üzerinden JSON payload yayınlayan temel mesaj otobüsü implementasyonu.
    /// EF Core tarafında kaydedilen enerji verileri gibi olayların
    /// diğer mikro servislerle paylaşılmasını sağlar.
    /// </summary>
    public class RabbitMqMessageBus : IMessageBus, IDisposable
    {
        private readonly ILogger<RabbitMqMessageBus> _logger;
        private readonly RabbitMqOptions _options;
        private readonly Lazy<IConnection> _connectionFactory;

        public RabbitMqMessageBus(
            IOptions<RabbitMqOptions> options,
            ILogger<RabbitMqMessageBus> logger)
        {
            _logger = logger;
            _options = options.Value;
            _connectionFactory = new Lazy<IConnection>(CreateConnection);
        }

        public Task PublishAsync(string queueName, object payload, CancellationToken cancellationToken = default)
        {
            if (payload == null)
            {
                return Task.CompletedTask;
            }

            try
            {
                using var channel = _connectionFactory.Value.CreateModel();

                channel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                var body = Encoding.UTF8.GetBytes(json);

                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;

                channel.BasicPublish(
                    exchange: _options.Exchange ?? string.Empty,
                    routingKey: queueName,
                    basicProperties: properties,
                    body: body);

                _logger.LogInformation("RabbitMQ mesajı yayınlandı. Queue: {Queue}, Payload: {Payload}", queueName, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ mesajı yayınlanırken hata oluştu.");
            }

            return Task.CompletedTask;
        }

        private IConnection CreateConnection()
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                DispatchConsumersAsync = true
            };

            _logger.LogInformation("RabbitMQ bağlantısı kuruluyor: {Host}:{Port}/{VHost}", _options.HostName, _options.Port, _options.VirtualHost);
            return factory.CreateConnection();
        }

        public void Dispose()
        {
            if (_connectionFactory.IsValueCreated)
            {
                _connectionFactory.Value.Dispose();
            }
        }
    }
}

