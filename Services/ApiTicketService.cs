using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.Json;
using success.Models.CentralApi;
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
    private readonly ILogger<ApiTicketService> _logger;
    private readonly string _validateTicketPath;
    private readonly string _validateCodeProperty;
    private readonly string? _bearerToken;

    public ApiTicketService(
        HttpClient httpClient,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ApiTicketService> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _validateTicketPath = configuration["CentralApi:ValidateTicketPath"] ?? "/api/scanner/validate";
        _validateCodeProperty = NormalizeCodeProperty(configuration["CentralApi:ValidateCodeProperty"]);
        _bearerToken = configuration["CentralApi:BearerToken"];

        if (!string.IsNullOrWhiteSpace(_bearerToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _bearerToken);
        }
    }

    public async Task<TicketValidationResponse> ValidateTicketAsync(string scanCode)
    {
        var rawCode = (scanCode ?? "").Trim();
        var code = NormalizeScannedCode(rawCode);
        if (string.IsNullOrWhiteSpace(code))
        {
            return Response(false, "Codigo requerido", "Escanea el QR o escribe el codigo del ticket.", "error");
        }

        try
        {
            ApplyAuthorizationHeader();

            // La API central recibe el codigo y una descripcion del escaner.
            var apiResponse = await _httpClient.PostAsJsonAsync(
                _validateTicketPath,
                BuildValidateRequest(code),
                JsonOptions);

            var json = await apiResponse.Content.ReadAsStringAsync();
            var validation = ReadApiResponse(json, apiResponse.IsSuccessStatusCode, apiResponse.StatusCode, code);

            if (validation != null)
            {
                return MapValidationResult(validation);
            }

            if (apiResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(
                    "Central API rejected ticket validation with 401. Path: {Path}. Response: {Response}",
                    _validateTicketPath,
                    Truncate(json));

                return Response(false, "Sesion expirada", "Cierre sesion e ingrese nuevamente.", "error");
            }

            if (apiResponse.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "Central API rejected ticket validation with 403. Path: {Path}. Response: {Response}",
                    _validateTicketPath,
                    Truncate(json));

                return Response(false, "Estacion no autorizada", "Este usuario o punto de entrada no tiene permiso para validar tickets aqui.", "error");
            }

            if (!apiResponse.IsSuccessStatusCode)
            {
                return Response(false, "No se pudo validar el ticket", "El sistema no pudo validar este ticket en este momento.", "error");
            }

            return Response(false, "No se pudo validar el ticket", "El sistema no devolvio informacion del ticket.", "error");
        }
        catch (JsonException)
        {
            return Response(false, "No se pudo validar el ticket", "No se pudo leer la informacion recibida.", "error");
        }
        catch (HttpRequestException)
        {
            return Response(false, "Sin conexion", "No se pudo conectar con el sistema de tickets.", "error");
        }
        catch (TaskCanceledException)
        {
            return Response(false, "Tiempo agotado", "El sistema tardo demasiado en responder.", "error");
        }
    }

    private JsonObject BuildValidateRequest(string code)
    {
        return new JsonObject
        {
            [_validateCodeProperty] = code,
            ["deviceInfo"] = GetDeviceInfo()
        };
    }

    private void ApplyAuthorizationHeader()
    {
        // Prioriza el token del operador autenticado; si no existe usa el token fijo.
        var token = _httpContextAccessor.HttpContext?.User.FindFirst("access_token")?.Value
                    ?? _bearerToken;

        if (!string.IsNullOrWhiteSpace(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private string GetDeviceInfo()
    {
        var context = _httpContextAccessor.HttpContext;
        var name = context?.User.FindFirstValue(ClaimTypes.Name) ?? context?.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? "Escaner web" : $"Escaner web - {name}";
    }

    private static string NormalizeCodeProperty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "qrCode" : value.Trim();
    }

    private static string NormalizeScannedCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = value.Trim();
        return TryExtractCodeFromJson(text)
               ?? TryExtractCodeFromUrl(text)
               ?? text;
    }

    private static string? TryExtractCodeFromJson(string text)
    {
        if (!text.StartsWith('{') && !text.StartsWith('['))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return FindCode(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindCode(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var code = GetString(element, "qrCode", "ticketCode", "code", "ticketId", "id", "token");
            if (!string.IsNullOrWhiteSpace(code))
            {
                return code;
            }

            foreach (var property in element.EnumerateObject())
            {
                var nestedCode = FindCode(property.Value);
                if (!string.IsNullOrWhiteSpace(nestedCode))
                {
                    return nestedCode;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nestedCode = FindCode(item);
                if (!string.IsNullOrWhiteSpace(nestedCode))
                {
                    return nestedCode;
                }
            }
        }

        return null;
    }

    private static string? TryExtractCodeFromUrl(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var code = GetQueryValue(uri.Query, "qrCode", "ticketCode", "code", "ticketId", "id", "token");
        if (!string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        var lastSegment = uri.Segments.LastOrDefault()?.Trim('/');
        return string.IsNullOrWhiteSpace(lastSegment)
            ? null
            : Uri.UnescapeDataString(lastSegment);
    }

    private static string? GetQueryValue(string query, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var pairs = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace("+", " "));
            if (!names.Any(name => name.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace("+", " ")) : "";
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Length <= 500 ? value : value[..500];
    }

    private static ParsedTicketValidation? ReadApiResponse(
        string json,
        bool httpSuccess,
        HttpStatusCode statusCode,
        string scannedCode)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var wrapperMessage = GetString(root, "message", "error", "detail", "title");
        var result = TryGetProperty(root, "data", out var data) && data.ValueKind != JsonValueKind.Null
            ? data
            : root;

        var explicitValidity = GetBool(result, "isValid", "valid", "allowed", "success")
                               ?? GetBool(root, "isValid", "valid", "allowed", "success");

        var ticket = TryMapTicket(result, scannedCode) ?? TryMapTicket(root, scannedCode);
        var message = FirstText(
            GetString(result, "message", "error", "detail", "title"),
            wrapperMessage);
        var wasAlreadyUsed = ticket?.Status.Equals("Already used", StringComparison.OrdinalIgnoreCase) == true
                             || TextLooksUsed(message);
        var doesNotExist = statusCode == HttpStatusCode.NotFound || TextLooksMissing(message);

        if (doesNotExist)
        {
            return new ParsedTicketValidation
            {
                IsValid = false,
                Reason = TicketValidationReason.NotFound,
                Message = message,
                Ticket = ticket
            };
        }

        if (wasAlreadyUsed)
        {
            return new ParsedTicketValidation
            {
                IsValid = false,
                Reason = TicketValidationReason.AlreadyScanned,
                Message = message,
                Ticket = ticket
            };
        }

        if (explicitValidity == null && ticket == null && string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var isValid = explicitValidity ?? (ticket != null && httpSuccess);

        return new ParsedTicketValidation
        {
            IsValid = isValid,
            Reason = isValid ? TicketValidationReason.Valid : TicketValidationReason.Rejected,
            Message = message,
            Ticket = ticket
        };
    }

    private static TicketValidationResponse MapValidationResult(ParsedTicketValidation result)
    {
        // Convierte la respuesta externa al formato que consume la pantalla.
        var response = new TicketValidationResponse
        {
            Success = result.IsValid,
            Title = result.IsValid ? "Ingreso permitido" : "Ingreso rechazado",
            Message = result.IsValid ? "Ticket valido. Acceso permitido." : "El ingreso no fue autorizado.",
            Type = result.IsValid ? "success" : "error",
            Ticket = result.IsValid ? result.Ticket : null
        };

        if (result.Reason == TicketValidationReason.NotFound)
        {
            response.Title = "Ticket no existe";
            response.Message = "Este ticket no existe.";
        }
        else if (result.Reason == TicketValidationReason.AlreadyScanned)
        {
            response.Title = "Ticket ya escaneado";
            response.Message = "Ya se escaneo anteriormente.";
        }
        else if (result.Reason == TicketValidationReason.Valid)
        {
            response.Title = "Ticket correcto";
            response.Message = "Es correcto, siga.";
        }

        return response;
    }

    private static TicketValidationResponse.TicketInfo? TryMapTicket(JsonElement result, string scannedCode)
    {
        if (TryGetProperty(result, "ticket", out var ticket)
            || TryGetProperty(result, "ticketInfo", out ticket)
            || TryGetProperty(result, "ticketData", out ticket))
        {
            return MapTicket(ticket, scannedCode);
        }

        return LooksLikeTicket(result) ? MapTicket(result, scannedCode) : null;
    }

    private static TicketValidationResponse.TicketInfo MapTicket(JsonElement ticket, string scannedCode)
    {
        var usedAt = GetDateTime(ticket, "usedAt", "scannedAt", "checkedAt", "scanTime");
        var wasAlreadyUsed = GetBool(ticket, "wasAlreadyUsed", "alreadyUsed", "isUsed", "used")
                             ?? TextLooksUsed(GetString(ticket, "status", "state"));
        var status = wasAlreadyUsed == true
            ? "Ya usado"
            : GetString(ticket, "status", "state") ?? "";

        return new TicketValidationResponse.TicketInfo
        {
            Code = FirstText(
                GetHumanText(ticket, "ticketId", "id", "code", "ticketCode"),
                ShortDisplayCode(scannedCode)) ?? "",
            ClientName = FirstText(
                BuildName(ticket),
                BuildNestedName(ticket, "holder"),
                BuildNestedName(ticket, "user"),
                BuildNestedName(ticket, "customer"),
                BuildNestedName(ticket, "attendee"),
                BuildNestedName(ticket, "participant"),
                GetHumanText(ticket, "holderName", "clientName", "customerName", "fullName", "name", "attendeeName", "participantName"),
                GetNestedHumanText(ticket, "holder", "name", "fullName", "email"),
                GetNestedHumanText(ticket, "user", "name", "fullName", "email"),
                GetNestedHumanText(ticket, "customer", "name", "fullName", "email"),
                GetNestedHumanText(ticket, "attendee", "name", "fullName", "email"),
                GetNestedHumanText(ticket, "participant", "name", "fullName", "email"),
                GetHumanText(ticket, "holderEmail", "email", "clientEmail", "userEmail")) ?? "",
            Email = FirstText(
                GetString(ticket, "holderEmail", "email", "clientEmail", "userEmail"),
                GetNestedString(ticket, "holder", "email"),
                GetNestedString(ticket, "user", "email"),
                GetNestedString(ticket, "customer", "email")) ?? "",
            EventName = FirstText(
                GetString(ticket, "eventName"),
                GetNestedString(ticket, "event", "name", "title")) ?? "",
            VenueName = FirstText(
                GetString(ticket, "venueName"),
                GetNestedString(ticket, "venue", "name")) ?? "",
            SeatNumber = GetString(ticket, "seatLabel", "seatNumber", "seat") ?? "",
            Row = GetString(ticket, "row", "rowLabel") ?? "",
            TicketType = GetString(ticket, "ticketType", "type") ?? "",
            EntryMode = GetString(ticket, "entryMode") ?? "",
            Status = status,
            ScanTime = usedAt ?? DateTime.UtcNow,
            ShowtimeStart = GetDateTime(ticket, "showtimeStart", "eventStart", "startsAt", "startTime"),
            PhoneNumber = FirstText(
                GetString(ticket, "phoneNumber", "phone"),
                GetNestedString(ticket, "holder", "phoneNumber", "phone"),
                GetNestedString(ticket, "user", "phoneNumber", "phone"),
                GetNestedString(ticket, "customer", "phoneNumber", "phone")) ?? "",
            PhotoUrl = FirstText(
                GetString(ticket, "photoUrl", "avatarUrl"),
                GetNestedString(ticket, "holder", "photoUrl", "avatarUrl"),
                GetNestedString(ticket, "user", "photoUrl", "avatarUrl"),
                GetNestedString(ticket, "customer", "photoUrl", "avatarUrl")) ?? ""
        };
    }

    private static bool LooksLikeTicket(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object
               && (TryGetProperty(element, "ticketId", out _)
                   || TryGetProperty(element, "ticketCode", out _)
                   || TryGetProperty(element, "qrCode", out _)
                   || TryGetProperty(element, "holderEmail", out _)
                   || TryGetProperty(element, "eventName", out _)
                   || TryGetProperty(element, "seatLabel", out _)
                   || TryGetProperty(element, "wasAlreadyUsed", out _)
                   || TryGetProperty(element, "usedAt", out _));
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

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var value))
            {
                var text = JsonValueToString(value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string? GetNestedString(JsonElement element, string parentName, params string[] names)
    {
        return TryGetProperty(element, parentName, out var parent)
            ? GetString(parent, names)
            : null;
    }

    private static string? GetHumanText(JsonElement element, params string[] names)
    {
        var text = GetString(element, names);
        return LooksLikeMachinePayload(text) ? null : text;
    }

    private static string? GetNestedHumanText(JsonElement element, string parentName, params string[] names)
    {
        return TryGetProperty(element, parentName, out var parent)
            ? GetHumanText(parent, names)
            : null;
    }

    private static string? BuildName(JsonElement element)
    {
        var firstName = GetHumanText(element, "firstName", "first_name", "names", "nombres");
        var lastName = GetHumanText(element, "lastName", "last_name", "surname", "apellidos");
        var fullName = string.Join(" ", new[] { firstName, lastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(fullName) ? null : fullName;
    }

    private static string? BuildNestedName(JsonElement element, string parentName)
    {
        return TryGetProperty(element, parentName, out var parent) ? BuildName(parent) : null;
    }

    private static string ShortDisplayCode(string value)
    {
        var code = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            return "";
        }

        if (!LooksLikeMachinePayload(code) && code.Length <= 36)
        {
            return code;
        }

        return code.Length <= 16 ? code : $"{code[..10]}...{code[^6..]}";
    }

    private static bool LooksLikeMachinePayload(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        return text.Length > 80
               || text.StartsWith('{')
               || text.StartsWith('[')
               || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || text.Length >= 32 && !text.Any(char.IsWhiteSpace) && text.All(character =>
                   char.IsLetterOrDigit(character) || character is '_' or '-' or '=')
               || text.Count(character => character == '.') == 2 && text.All(character =>
                   char.IsLetterOrDigit(character) || character is '_' or '-' or '.');
    }

    private static bool? GetBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                continue;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }

            var text = JsonValueToString(value);
            if (bool.TryParse(text, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static DateTime? GetDateTime(JsonElement element, params string[] names)
    {
        var text = GetString(element, names);
        return DateTime.TryParse(text, out var parsed) ? parsed : null;
    }

    private static bool TextLooksUsed(string? status)
    {
        return !string.IsNullOrWhiteSpace(status)
               && (status.Contains("already", StringComparison.OrdinalIgnoreCase)
                   || status.Contains("used", StringComparison.OrdinalIgnoreCase)
                   || status.Contains("scanned", StringComparison.OrdinalIgnoreCase)
                   || status.Contains("escaneado", StringComparison.OrdinalIgnoreCase)
                   || status.Contains("usado", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TextLooksMissing(string? message)
    {
        return !string.IsNullOrWhiteSpace(message)
               && (message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("no existe", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("no encontrado", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("no encontrada", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("inexistente", StringComparison.OrdinalIgnoreCase));
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

    private sealed class ParsedTicketValidation
    {
        public bool IsValid { get; set; }
        public TicketValidationReason Reason { get; set; } = TicketValidationReason.Rejected;
        public string? Message { get; set; }
        public TicketValidationResponse.TicketInfo? Ticket { get; set; }
    }

    private enum TicketValidationReason
    {
        Valid,
        AlreadyScanned,
        NotFound,
        Rejected
    }
}
