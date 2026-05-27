using System.Text.Json.Serialization;

namespace success.Models.CentralApi;

internal sealed record ValidateTicketApiRequest(
    [property: JsonPropertyName("qrCode")] string QRCode,
    [property: JsonPropertyName("deviceInfo")] string DeviceInfo);
