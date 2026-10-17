using Ailu.Api.Data;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Features.Auth;

public sealed record DeviceRequest(string PushToken, string Platform);

public static class DeviceEndpoints
{
    private static readonly string[] Platforms = ["ios", "android"];

    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/me/devices").WithTags("Me").RequireAuthorization();
        g.MapPut("/", Register);
        g.MapPost("/unregister", Unregister);
    }

    // Called on every app start. A token that belonged to someone else (the phone changed hands, or
    // the partner signed in on it) now belongs to the caller, so pushes never reach the wrong person.
    private static async Task<IResult> Register(DeviceRequest body, HttpContext http, AiluDb db, TimeProvider clock,
        CancellationToken ct)
    {
        var token = body.PushToken?.Trim() ?? "";
        if (token.Length is 0 or > 200) return Errors.Validation("pushToken", "Нужен токен уведомлений.");
        if (!Platforms.Contains(body.Platform)) return Errors.Validation("platform", "Платформа: ios или android.");

        var userId = CurrentUser.Id(http.User);
        var now = clock.GetUtcNow();
        var device = await db.Devices.SingleOrDefaultAsync(d => d.PushToken == token, ct);
        if (device is null)
        {
            db.Devices.Add(new Device
            {
                Id = Guid.NewGuid(), UserId = userId, PushToken = token, Platform = body.Platform,
                CreatedAt = now, LastSeenAt = now
            });
        }
        else
        {
            device.UserId = userId;
            device.Platform = body.Platform;
            device.LastSeenAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // Sign-out stops pushes to this device. Only the caller's own device can be removed.
    private static async Task<IResult> Unregister(DeviceRequest body, HttpContext http, AiluDb db, CancellationToken ct)
    {
        var userId = CurrentUser.Id(http.User);
        await db.Devices.Where(d => d.PushToken == body.PushToken && d.UserId == userId).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }
}
