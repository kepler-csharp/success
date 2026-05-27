namespace success.Models.CentralApi;

internal sealed class ValidateTicketApiResult
{
    public bool IsValid { get; set; }
    public string? Message { get; set; }
    public TicketDetailApiDto? Ticket { get; set; }
}
