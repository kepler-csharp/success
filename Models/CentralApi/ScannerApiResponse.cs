namespace success.Models.CentralApi;

internal sealed class ScannerApiResponse
{
    public string? Message { get; set; }
    public ValidateTicketApiResult? Data { get; set; }
}
