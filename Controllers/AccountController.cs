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
                ModelState.AddModelError("", ToUserMessage(ReadMessage(body)) ?? "Email or password is incorrect.");
                return View(model);
            }

            var accessToken = ReadText(body, "accessToken", "token", "jwt");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                ModelState.AddModelError("", "Could not sign in. Try again.");
                return View(model);
            }

            var refreshToken = ReadText(body, "refreshToken");
            var displayName = ReadText(body, "fullName", "name", "userName", "email") ?? model.Email;
            var role = ReadText(body, "role", "roles") ?? "";

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

            if (!string.IsNullOrWhiteSpace(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, "Cookies");
            await HttpContext.SignInAsync("Cookies", new ClaimsPrincipal(identity));

            return LocalRedirect(IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : Url.Action("Index", "Success")!);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Central API login failed");
            ModelState.AddModelError("", "Could not connect to sign in.");
            return View(model);
        }
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

        return message
            .Replace("API", "system", StringComparison.OrdinalIgnoreCase)
            .Replace("token", "session", StringComparison.OrdinalIgnoreCase);
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
