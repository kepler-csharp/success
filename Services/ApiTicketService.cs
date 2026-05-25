using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Success.Models.Responses;
using success.Services.Interfaces;

namespace success.Services;

public class ApiTicketService : ITicketService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CentralApiOptions _options;

    public ApiTicketService(
        HttpClient httpClient,
        IHttpContextAccessor httpContextAccessor,
        IOptions<CentralApiOptions> options)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.BearerToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.BearerToken);
        }
    }

    public async Task<TicketValidationResponse> ValidateTicketAsync(string scanCode)
    {
        var code = (scanCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            return Response(false, "Code required", "Scan the QR or type the ticket code.", "error");
        }

        try
        {
            ApplyAuthorizationHeader();

            // La API central recibe el codigo y una descripcion del escaner.
            var apiResponse = await _httpClient.PostAsJsonAsync(
                _options.ValidateTicketPath,
                new ValidateTicketApiRequest(code, GetDeviceInfo()),
                JsonOptions);

            if (apiResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Response(false, "Could not check ticket", "This entry station is not authorized.", "error");
            }

            var json = await apiResponse.Content.ReadAsStringAsync();
            var validation = ReadApiResponse(json);

            if (validation?.Data != null)
            {
                return MapValidationResult(validation.Data, validation.Message, code);
            }

            if (!apiResponse.IsSuccessStatusCode)
            {
                return Response(false, "Could not check ticket", "The system could not check this ticket right now.", "error");
            }

            return Response(false, "Could not check ticket", "The system did not return ticket information.", "error");
        }
        catch (JsonException)
        {
            return Response(false, "Could not check ticket", "The information received could not be read.", "error");
        }
        catch (HttpRequestException)
        {
            return Response(false, "No connection", "Could not reach the ticket system.", "error");
        }
        catch (TaskCanceledException)
        {
            return Response(false, "Timed out", "The system took too long to respond.", "error");
        }
    }

    private void ApplyAuthorizationHeader()
    {
        // Prioriza el token del operador autenticado; si no existe usa el token fijo.
        var token = _httpContextAccessor.HttpContext?.User.FindFirst("access_token")?.Value
                    ?? _options.BearerToken;

        if (!string.IsNullOrWhiteSpace(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private string GetDeviceInfo()
    {
        var context = _httpContextAccessor.HttpContext;
        var name = context?.User.FindFirstValue(ClaimTypes.Name) ?? context?.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? "Web scanner" : $"Web scanner - {name}";
    }

    private static ScannerApiResponse? ReadApiResponse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ScannerApiResponse>(json, JsonOptions);
    }

    private static TicketValidationResponse MapValidationResult(
        ValidateTicketApiResult result,
        string? wrapperMessage,
        string scannedCode)
    {
        // Convierte la respuesta externa al formato que consume la pantalla.
        var message = FirstText(result.Message, wrapperMessage)
                      ?? (result.IsValid ? "Valid ticket. Access granted." : "Entry was not authorized.");

        return new TicketValidationResponse
        {
            Success = result.IsValid,
            Title = result.IsValid ? "Entry allowed" : "Entry rejected",
            Message = message,
            Type = result.IsValid ? "success" : "error",
            Ticket = result.Ticket == null ? null : MapTicket(result.Ticket, scannedCode)
        };
    }

    private static TicketValidationResponse.TicketInfo MapTicket(TicketDetailApiDto ticket, string scannedCode)
    {
        var ticketId = JsonValueToString(ticket.TicketId);

        return new TicketValidationResponse.TicketInfo
        {
            Code = string.IsNullOrWhiteSpace(ticketId) ? scannedCode : ticketId,
            ClientName = ticket.HolderEmail ?? "",
            Email = ticket.HolderEmail ?? "",
            EventName = ticket.EventName ?? "",
            VenueName = ticket.VenueName ?? "",
            SeatNumber = ticket.SeatLabel ?? "",
            Status = ticket.WasAlreadyUsed ? "Already used" : "",
            ScanTime = ticket.UsedAt ?? DateTime.UtcNow,
            ShowtimeStart = ticket.ShowtimeStart
        };
    }

    private static string? JsonValueToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string? FirstText(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static TicketValidationResponse Response(bool success, string title, string message, string type)
    {
        return new TicketValidationResponse
        {
            Success = success,
            Title = title,
            Message = message,
            Type = type
        };
    }

    private sealed record ValidateTicketApiRequest(
        [property: JsonPropertyName("qrCode")] string QRCode,
        [property: JsonPropertyName("deviceInfo")] string DeviceInfo);

    private sealed class ScannerApiResponse
    {
        public string? Message { get; set; }
        public ValidateTicketApiResult? Data { get; set; }
    }

    private sealed class ValidateTicketApiResult
    {
        public bool IsValid { get; set; }
        public string? Message { get; set; }
        public TicketDetailApiDto? Ticket { get; set; }
    }

    private sealed class TicketDetailApiDto
    {
        public JsonElement TicketId { get; set; }
        public string? HolderEmail { get; set; }
        public string? EventName { get; set; }
        public string? VenueName { get; set; }
        public DateTime? ShowtimeStart { get; set; }
        public string? SeatLabel { get; set; }
        public bool WasAlreadyUsed { get; set; }
        public DateTime? UsedAt { get; set; }
    }
}
