using Ailu.Api.Data;
using Ailu.Api.Features.Achievements;
using Ailu.Api.Features.Ai;
using Ailu.Api.Features.Auth;
using Ailu.Api.Features.Billing;
using Ailu.Api.Features.Daily;
using Ailu.Api.Features.Goals;
using Ailu.Api.Features.LoveQuiz;
using Ailu.Api.Features.Memories;
using Ailu.Api.Features.Mood;
using Ailu.Api.Features.Pairing;
using Ailu.Api.Features.Timeline;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var auth = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
auth.Validate();

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
builder.Services.AddScoped<AccountDeletion>();
builder.Services.AddScoped<DemoCouple>();
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

var billing = builder.Configuration.GetSection("Billing").Get<BillingOptions>() ?? new BillingOptions();
if (billing.Products.Count == 0) billing.Products = BillingOptions.DefaultProducts();
builder.Services.AddSingleton(billing);
if (billing.UseFakeStore)
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
        throw new InvalidOperationException("Billing:UseFakeStore is for development only.");
    builder.Services.AddSingleton<IStoreClient>(sp => new FakeStore(StoreProvider.Apple, sp.GetRequiredService<TimeProvider>()));
    builder.Services.AddSingleton<IStoreClient>(sp => new FakeStore(StoreProvider.Google, sp.GetRequiredService<TimeProvider>()));
}
else
{
    builder.Services.AddSingleton(billing.Apple);
    builder.Services.AddSingleton(billing.Google);
    builder.Services.AddHttpClient<AppleStoreClient>();
    builder.Services.AddHttpClient<GoogleStoreClient>();
    builder.Services.AddTransient<IStoreClient>(sp => billing.Apple.Configured
        ? sp.GetRequiredService<AppleStoreClient>() : new UnconfiguredStore(StoreProvider.Apple));
    builder.Services.AddTransient<IStoreClient>(sp => billing.Google.Configured
        ? sp.GetRequiredService<GoogleStoreClient>() : new UnconfiguredStore(StoreProvider.Google));
}
builder.Services.AddScoped<BillingService>();

// AI selections and personal tasks. Without an API key the built-in stub writes them.
var ai = builder.Configuration.GetSection("Ai").Get<AiOptions>() ?? new AiOptions();
builder.Services.AddSingleton(ai);
if (ai.Configured) builder.Services.AddSingleton<IAiWriter, ClaudeAiWriter>();
else builder.Services.AddSingleton<IAiWriter, StubAiWriter>();
builder.Services.AddScoped<AiService>();
if (builder.Configuration.GetValue("Billing:ExpiryEnabled", true)) builder.Services.AddHostedService<BillingScheduler>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = auth.Issuer,
        ValidAudience = auth.Audience,
        IssuerSigningKey = auth.Key(),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
    // An access token outlives a deleted account by up to an hour; it must stop working at once.
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async ctx =>
        {
            var id = CurrentUser.Id(ctx.Principal!);
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AiluDb>();
            if (!await db.Users.AnyAsync(u => u.Id == id && u.DeletedAt == null, ctx.HttpContext.RequestAborted))
                ctx.Fail("Account deleted.");
        }
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
LoveQuizEndpoints.Map(app);
TimelineEndpoints.Map(app);
AchievementEndpoints.Map(app);
AiEndpoints.Map(app);
BillingEndpoints.Map(app);

app.Run();

public partial class Program;
