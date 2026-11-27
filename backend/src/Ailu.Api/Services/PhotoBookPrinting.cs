using Ailu.Api.Data;
using Ailu.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Services;

// What happens to an order after the couple pays: the team marks it paid (payment is agreed with the couple
// directly), then this job draws the print file from the book as it was ordered, keeps it, and hands it to
// the print vendor. Running it as a job keeps the minute of PDF drawing out of the request, and retries a
// vendor that was down.
public sealed class PhotoBookPrinting(AiluDb db, BookPdf pdf, IPhotoStore store, IPrintVendor vendor, TimeProvider clock,
    ILogger<PhotoBookPrinting> log)
{
    // The team was paid for the order. An order the couple cancelled meanwhile still goes ahead: they paid,
    // so the book is printed. Marking an order already paid again changes nothing but the note.
    public async Task<bool> MarkPaidAsync(Guid orderId, string? note, CancellationToken ct = default)
    {
        var order = await db.PhotoBookOrders.SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return false;
        if (order.Status is PhotoBookOrderStatus.AwaitingPayment or PhotoBookOrderStatus.Cancelled)
        {
            order.Status = PhotoBookOrderStatus.Paid;
            order.PaidAt = clock.GetUtcNow();
            order.CancelledAt = null;
            log.LogInformation("Photo book order {Order} paid", order.Id);
        }
        if (!string.IsNullOrWhiteSpace(note)) order.PaymentNote = note.Trim();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var due = await db.PhotoBookOrders
            .Where(o => o.Status == PhotoBookOrderStatus.Paid && o.PrintJobId == null)
            .OrderBy(o => o.PaidAt).Take(5).ToListAsync(ct);
        var sent = 0;
        foreach (var order in due)
        {
            try
            {
                var file = await pdf.RenderAsync(order.CoupleId, BookContent.FromJson(order.Content), print: true, ct);
                var key = BookKeys.PrintFile(order.CoupleId, order.Id);
                await store.PutAsync(key, file, ct: ct);
                order.PrintJobId = await vendor.SubmitAsync(new PrintJob(order.Id, order.Year, key, file, order.RecipientName ?? "",
                    order.Phone ?? "", order.City ?? "", order.Address ?? "", order.Comment), ct);
                order.Status = PhotoBookOrderStatus.Printing;
                order.SubmittedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
                sent++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Sending photo book order {Order} to print failed; will retry", order.Id);
            }
        }
        return sent;
    }
}

public sealed class PhotoBookPrintScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<PhotoBookPrintScheduler> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PhotoBookPrinting>().RunOnceAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Photo book printing failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
