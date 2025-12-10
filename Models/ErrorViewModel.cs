// Hata view modeli: request id gösterimi.
namespace AygazSmartEnergy.Models;

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
