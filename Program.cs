using success.Services;
using success.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();

var centralApiBaseUrl = builder.Configuration["CentralApi:BaseUrl"];
if (string.IsNullOrWhiteSpace(centralApiBaseUrl))
{
    throw new InvalidOperationException("CentralApi:BaseUrl is required in configuration.");
}

var centralApiTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("CentralApi:TimeoutSeconds", 30));

// Cliente tipado usado por ITicketService para consultar tickets.
builder.Services.AddHttpClient<ITicketService, ApiTicketService>(client =>
{
    client.BaseAddress = new Uri(centralApiBaseUrl);
    client.Timeout = centralApiTimeout;
});

// Cliente nombrado usado por AccountController para login y logout.
builder.Services.AddHttpClient("CentralApi", client =>
{
    client.BaseAddress = new Uri(centralApiBaseUrl);
    client.Timeout = centralApiTimeout;
});

// Sesion local por cookies despues de autenticar contra la API central.
builder.Services.AddAuthentication("Cookies")
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Denied";
    });

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseRouting();

// El orden importa: primero autenticar, luego autorizar.
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Success}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
