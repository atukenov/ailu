using Ailu.Api.Data;
using Ailu.Api.Features.Auth;
using Ailu.Api.Features.Daily;
using Ailu.Api.Features.Goals;
using Ailu.Api.Features.Memories;
using Ailu.Api.Features.Mood;
using Ailu.Api.Features.Pairing;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var auth = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
if (auth.SigningKey.Length < 32)
    throw new InvalidOperationException("Auth:SigningKey must be set to at least 32 characters.");

builder.Services.AddSingleton(auth);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenIssuer>();
builder.Services.AddSingleton<ISmsSender, LoggingSmsSender>();
if (builder.Configuration.GetValue<bool>("Push:UseExpo")) builder.Services.AddHttpClient<IPushSender, ExpoPushSender>();
else builder.Services.AddSingleton<IPushSender, LoggingPushSender>();

builder.Services.AddDbContext<AiluDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Ailu")));
builder.Services.AddScoped<NurLedgerService>();
builder.Services.AddScoped<ActivityService>();
builder.Services.AddScoped<StreakService>();
builder.Services.AddScoped<DailyContentService>();
builder.Services.AddScoped<DailyPushJob>();
if (builder.Configuration.GetValue("Push:SchedulerEnabled", true)) builder.Services.AddHostedService<DailyPushScheduler>();

var photoRoot = builder.Configuration["Photos:Root"];
if (string.IsNullOrEmpty(photoRoot)) photoRoot = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "photos");
builder.Services.AddSingleton<IPhotoStore>(new DiskPhotoStore(photoRoot));
builder.Services.AddSingleton<PhotoProcessor>();
builder.Services.AddSingleton<PhotoUrls>();
builder.Services.AddScoped<MemoryPurgeJob>();
if (builder.Configuration.GetValue("Photos:PurgeEnabled", true)) builder.Services.AddHostedService<MemoryPurgeScheduler>();
builder.Services.AddScoped<MoodRetentionJob>();
if (builder.Configuration.GetValue("Mood:RetentionEnabled", true)) builder.Services.AddHostedService<MoodRetentionScheduler>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = auth.Issuer,
        ValidAudience = auth.Audience,
        IssuerSigningKey = auth.Key(),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});
// Validate token lifetimes against the app's clock rather than the machine's, so tests can move time.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<TimeProvider>((o, clock) => o.TokenValidationParameters.LifetimeValidator =
        (notBefore, expires, _, p) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            return (notBefore is null || notBefore <= now + p.ClockSkew) && (expires is null || expires > now - p.ClockSkew);
        });
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
// Only the web build of the app (used in development) runs in a browser; native apps don't need CORS.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AiluDb>().Database.Migrate();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

AuthEndpoints.Map(app);
DeviceEndpoints.Map(app);
PairingEndpoints.Map(app);
DailyEndpoints.Map(app);
MemoryEndpoints.Map(app);
MoodEndpoints.Map(app);
GoalEndpoints.Map(app);

app.Run();

public partial class Program;
