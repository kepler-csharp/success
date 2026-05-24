namespace success.Services;

public class CentralApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5201";
    public string ValidateTicketPath { get; set; } = "/api/scanner/validate";
    public string ValidateCodeProperty { get; set; } = "qrCode";
    public int TimeoutSeconds { get; set; } = 30;
    public string? BearerToken { get; set; }
}
