using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AygazSmartEnergy.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

// RabbitMQ mesaj otobüsü: JSON payload yayınlar (Topic exchange + queue bind).
namespace AygazSmartEnergy.Services
{
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

                // Exchange tanımla (yoksa oluştur)
                var exchangeName = _options.Exchange ?? "aygaz.sensors";
                channel.ExchangeDeclare(
                    exchange: exchangeName,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false);

                // Queue'yu tanımla
                channel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);

                // Queue'yu exchange'e bind et
                var routingKey = $"sensor.{queueName}"; // sensor.sensor-data
                channel.QueueBind(
                    queue: queueName,
                    exchange: exchangeName,
                    routingKey: routingKey);

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                var body = Encoding.UTF8.GetBytes(json);

                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;

                channel.BasicPublish(
                    exchange: exchangeName,
                    routingKey: routingKey,
                    basicProperties: properties,
                    body: body);

                _logger.LogInformation("RabbitMQ mesajı yayınlandı. Exchange: {Exchange}, RoutingKey: {RoutingKey}, Queue: {Queue}", exchangeName, routingKey, queueName);
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

