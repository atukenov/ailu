using Ailu.Api.Data;
using Ailu.Api.Features.Auth;
using Ailu.Api.Features.Daily;
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

builder.Services.AddDbContext<AiluDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Ailu")));
builder.Services.AddScoped<NurLedgerService>();
builder.Services.AddScoped<ActivityService>();
builder.Services.AddScoped<DailyContentService>();

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

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AiluDb>().Database.Migrate();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

AuthEndpoints.Map(app);
PairingEndpoints.Map(app);
DailyEndpoints.Map(app);

app.Run();

public partial class Program;
