using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Success.Models.Responses;
using success.Services.Interfaces;

namespace success.Services;

public class ApiTicketService : ITicketService
{
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
        if (code.Length < 4)
        {
            return Response(false, "Codigo invalido", "Escanea de nuevo o escribe un codigo mas largo.", "error");
        }

        try
        {
            ApplyAuthorizationHeader();

            var body = new Dictionary<string, string>
            {
                [_options.ValidateCodeProperty] = code
            };

            var apiResponse = await _httpClient.PostAsJsonAsync(_options.ValidateTicketPath, body);

            if (apiResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return Response(false, "No autorizado", "La API rechazo la solicitud. Revisa el token del escaner.", "error");
            }

            if (!apiResponse.IsSuccessStatusCode)
            {
                return Response(false, "Error de API", "La API central no pudo validar el ticket en este momento.", "error");
            }

            var json = await apiResponse.Content.ReadAsStringAsync();
            return ParseValidationResponse(json, code);
        }
        catch (HttpRequestException)
        {
            return Response(false, "API no disponible", "No se pudo conectar con la API central de tickets.", "error");
        }
        catch (TaskCanceledException)
        {
            return Response(false, "Tiempo agotado", "La API central tardo demasiado en responder.", "error");
        }
    }

    private void ApplyAuthorizationHeader()
    {
        var token = _httpContextAccessor.HttpContext?.User.FindFirst("access_token")?.Value
                    ?? _options.BearerToken;

        if (!string.IsNullOrWhiteSpace(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static TicketValidationResponse ParseValidationResponse(string json, string scannedCode)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Response(false, "Respuesta vacia", "La API central no devolvio datos de validacion.", "error");
        }

        try
        {
            var root = JsonNode.Parse(json);
            if (root == null)
            {
                return Response(false, "Respuesta invalida", "La API devolvio JSON vacio o incorrecto.", "error");
            }

            var ticketNode = FirstObject(root, "ticket", "data", "result", "validation") ?? root;
            var success = GetBool(root, "success", "isValid", "wasSuccessful", "valid")
                          ?? GetBool(ticketNode, "success", "isValid", "wasSuccessful", "valid")
                          ?? false;

            var failureReason = FirstText(root, "failureReason", "reason", "error", "message")
                                ?? FirstText(ticketNode, "failureReason", "reason", "error", "message");

            var title = FirstText(root, "title")
                        ?? (success ? "Acceso permitido" : "Acceso rechazado");

            var message = FirstText(root, "message", "detail", "description")
                          ?? failureReason
                          ?? (success ? "El ticket es valido. Puede ingresar." : "La API no autorizo el ingreso.");

            var type = FirstText(root, "type", "status")
                       ?? FirstText(ticketNode, "type", "status")
                       ?? (success ? "success" : "error");

            return new TicketValidationResponse
            {
                Success = success,
                Title = title,
                Message = message,
                Type = NormalizeType(type, success),
                Ticket = BuildTicketInfo(ticketNode, scannedCode)
            };
        }
        catch (JsonException)
        {
            return Response(false, "Respuesta invalida", "La API devolvio un formato que Success no pudo leer.", "error");
        }
    }

    private static TicketValidationResponse.TicketInfo? BuildTicketInfo(JsonNode? node, string scannedCode)
    {
        if (node == null)
        {
            return null;
        }

        var user = FirstObject(node, "user", "customer", "client", "owner", "buyer") ?? node;
        var ticket = FirstObject(node, "ticket") ?? node;
        var orderItem = FirstObject(node, "orderItem") ?? node;
        var order = FirstObject(node, "order") ?? node;
        var eventNode = FirstObject(node, "event") ?? node;
        var showtime = FirstObject(node, "showtime") ?? node;
        var seat = FirstObject(node, "seat") ?? node;
        var venue = FirstObject(node, "venue") ?? eventNode;

        var code = FirstText(ticket, "qrCode", "code", "number", "externalId", "id")
                   ?? FirstText(node, "qrCode", "code", "number", "externalId")
                   ?? scannedCode;

        var clientName = FirstText(user, "fullName", "name", "userName", "email")
                         ?? FirstText(order, "fullName", "customerName", "buyerName")
                         ?? "";

        return new TicketValidationResponse.TicketInfo
        {
            Code = code,
            ClientName = clientName,
            Email = FirstText(user, "email", "normalizedEmail") ?? "",
            PhoneNumber = FirstText(user, "phoneNumber", "phone") ?? "",
            EventName = FirstText(eventNode, "name", "eventName", "title") ?? "",
            TicketType = FirstText(ticket, "type", "ticketType", "name") ?? FirstText(orderItem, "type", "ticketType") ?? "",
            SeatNumber = FirstText(seat, "number", "seatNumber", "name") ?? FirstText(node, "seatNumber") ?? "",
            Row = FirstText(seat, "row", "seatRow") ?? "",
            VenueName = FirstText(venue, "venueName", "name") ?? "",
            Status = FirstText(ticket, "status") ?? FirstText(node, "status") ?? "",
            EntryMode = FirstText(ticket, "entryMode", "provider", "type") ?? "",
            ScanTime = GetDate(node, "validatedAt", "usedAt", "scanTime", "createdAt") ?? DateTime.UtcNow,
            ShowtimeStart = GetDate(showtime, "startTime", "startsAt"),
            PhotoUrl = FirstText(user, "photoUrl", "avatarUrl", "imageUrl") ?? ""
        };
    }

    private static JsonNode? FirstObject(JsonNode? node, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetValue(node, name);
            if (value is JsonObject)
            {
                return value;
            }
        }

        return null;
    }

    private static string? FirstText(JsonNode? node, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetValue(node, name);
            var text = value?.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value?.ToJsonString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim('"');
            }
        }

        return null;
    }

    private static bool? GetBool(JsonNode? node, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetValue(node, name);
            if (value == null)
            {
                continue;
            }

            if (value.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetValue<bool>();
            }

            if (bool.TryParse(FirstText(node, name), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? GetInt(JsonNode? node, params string[] names)
    {
        foreach (var name in names)
        {
            var text = FirstText(node, name);
            if (int.TryParse(text, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static DateTime? GetDate(JsonNode? node, params string[] names)
    {
        foreach (var name in names)
        {
            var text = FirstText(node, name);
            if (DateTime.TryParse(text, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static JsonNode? GetValue(JsonNode? node, string name)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        foreach (var property in obj)
        {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string NormalizeType(string type, bool success)
    {
        if (success)
        {
            return "success";
        }

        var normalized = type.Trim().ToLowerInvariant();
        return normalized is "used" or "duplicate" or "expired" or "cancelled" or "not-found"
            ? normalized
            : "error";
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
}
