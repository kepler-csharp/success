using success.Services;
using success.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<CentralApiOptions>(builder.Configuration.GetSection("CentralApi"));
builder.Services.AddHttpContextAccessor();

var centralApiOptions = builder.Configuration.GetSection("CentralApi").Get<CentralApiOptions>() ?? new CentralApiOptions();
var centralApiTimeout = TimeSpan.FromSeconds(centralApiOptions.TimeoutSeconds);

builder.Services.AddHttpClient<ITicketService, ApiTicketService>(client =>
{
    client.BaseAddress = new Uri(centralApiOptions.BaseUrl);
    client.Timeout = centralApiTimeout;
});

builder.Services.AddHttpClient("CentralApi", client =>
{
    client.BaseAddress = new Uri(centralApiOptions.BaseUrl);
    client.Timeout = centralApiTimeout;
});

builder.Services.AddAuthentication("Cookies")
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
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

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Success}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
