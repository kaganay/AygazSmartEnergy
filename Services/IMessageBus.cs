using System.Threading;
using System.Threading.Tasks;

namespace AygazSmartEnergy.Services
{
    /// <summary>
    /// Uygulama içerisindeki servislerin mesaj kuyruğu/geri plan işlemcilerine
    /// asenkron bildirim göndermesini sağlayan soyut servis sözleşmesi.
    /// </summary>
    public interface IMessageBus
    {
        /// <summary>
        /// Verilen kuyruğa JSON formatında payload yayınlar.
        /// </summary>
        /// <param name="queueName">RabbitMQ kuyruğu.</param>
        /// <param name="payload">Serileştirilecek veri.</param>
        /// <param name="cancellationToken">İptal belirteci.</param>
        Task PublishAsync(string queueName, object payload, CancellationToken cancellationToken = default);
    }
}

