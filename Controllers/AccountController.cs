using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using success.Models.Auth;

namespace success.Controllers;

[Route("Account")]
public class AccountController : Controller
{
    private static readonly string[] AllowedRoles = ["Admin", "Scanner"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IHttpClientFactory httpClientFactory, ILogger<AccountController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("Login")]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost("Login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("CentralApi");
            var response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                email = model.Email,
                password = model.Password
            });

            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", ToUserMessage(ReadMessage(body)) ?? "Correo o contrasena incorrectos.");
                return View(model);
            }

            // La API puede devolver el token con distintos nombres.
            var accessToken = ReadText(body, "accessToken", "token", "jwt");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                ModelState.AddModelError("", "No se pudo iniciar sesion. Intenta de nuevo.");
                return View(model);
            }

            var refreshToken = ReadText(body, "refreshToken");
            var displayName = ReadText(body, "fullName", "name", "userName", "email") ?? model.Email;
            var roles = ReadRoles(body);
            if (roles.Count == 0)
            {
                roles = ReadRolesFromJwt(accessToken);
            }

            if (!roles.Any(IsAllowedRole))
            {
                ModelState.AddModelError("", "No tienes permiso para ingresar a esta pantalla. Solo Admin y Scanner pueden acceder.");
                return View(model);
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, displayName),
                new(ClaimTypes.Email, model.Email),
                new("access_token", accessToken)
            };

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                claims.Add(new Claim("refresh_token", refreshToken));
            }

            foreach (var role in roles.Where(IsAllowedRole).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, "Cookies");
            // Guarda la sesion local y conserva el access token como claim.
            await HttpContext.SignInAsync("Cookies", new ClaimsPrincipal(identity));

            return LocalRedirect(IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : Url.Action("Index", "Success")!);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Central API login failed");
            ModelState.AddModelError("", "No se pudo conectar para iniciar sesion.");
            return View(model);
        }
    }

    [HttpGet("Denied")]
    [AllowAnonymous]
    public async Task<IActionResult> Denied()
    {
        await HttpContext.SignOutAsync("Cookies");
        TempData["AuthMessage"] = "No tienes permiso para ingresar a esta pantalla. Solo Admin y Scanner pueden acceder.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost("Logout")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var token = User.FindFirstValue("access_token");

        try
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                // Intenta cerrar tambien la sesion en la API central.
                var client = _httpClientFactory.CreateClient("CentralApi");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                await client.PostAsync("/api/logout", null);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Central API logout failed");
        }

        await HttpContext.SignOutAsync("Cookies");
        return RedirectToAction(nameof(Login));
    }

    private static bool IsLocalUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) && Uri.IsWellFormedUriString(url, UriKind.Relative);
    }

    private static string? ReadMessage(string json)
    {
        return ReadText(json, "message", "error", "detail", "title");
    }

    private static string? ToUserMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var normalized = message.Trim();
        if (normalized.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("incorrect", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("not authorized", StringComparison.OrdinalIgnoreCase))
        {
            return "Correo o contrasena incorrectos.";
        }

        return null;
    }

    private static bool IsAllowedRole(string role)
    {
        return AllowedRoles.Contains(role.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ReadRoles(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var root = JsonNode.Parse(json);
            var node = FindRoleValue(root);
            return ReadRoleValues(node);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ReadRoleValues(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            return array.SelectMany(ReadRoleValues)
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .ToArray();
        }

        if (node is JsonObject obj)
        {
            return obj.SelectMany(item =>
                    string.Equals(item.Key, "name", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Key, "role", StringComparison.OrdinalIgnoreCase)
                        ? ReadRoleValues(item.Value)
                        : [])
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .ToArray();
        }

        if (node is JsonValue value)
        {
            var text = value.GetValueKind() == JsonValueKind.String
                ? value.GetValue<string>()
                : value.ToJsonString().Trim('"');

            return string.IsNullOrWhiteSpace(text) ? [] : [text];
        }

        return [];
    }

    private static IReadOnlyList<string> ReadRolesFromJwt(string accessToken)
    {
        try
        {
            var parts = accessToken.Split('.');
            if (parts.Length < 2)
            {
                return [];
            }

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var root = JsonNode.Parse(json);
            return ReadRoleValues(FindRoleValue(root));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return [];
        }
    }

    private static JsonNode? FindRoleValue(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var item in obj)
            {
                if (IsRoleProperty(item.Key))
                {
                    return item.Value;
                }

                var nested = FindRoleValue(item.Value);
                if (nested != null)
                {
                    return nested;
                }
            }
        }

        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                var nested = FindRoleValue(item);
                if (nested != null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static bool IsRoleProperty(string name)
    {
        return name.Equals("role", StringComparison.OrdinalIgnoreCase)
               || name.Equals("roles", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith("/role", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadText(string json, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(json);
            foreach (var name in names)
            {
                var value = FindValue(root, name);
                if (value == null)
                {
                    continue;
                }

                return value.GetValueKind() == JsonValueKind.String
                    ? value.GetValue<string>()
                    : value.ToJsonString().Trim('"');
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    // Busca una propiedad sin depender de la forma exacta del JSON.
    private static JsonNode? FindValue(JsonNode? node, string name)
    {
        if (node is JsonObject obj)
        {
            foreach (var item in obj)
            {
                if (string.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return item.Value;
                }

                var nested = FindValue(item.Value, name);
                if (nested != null)
                {
                    return nested;
                }
            }
        }

        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                var nested = FindValue(item, name);
                if (nested != null)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
