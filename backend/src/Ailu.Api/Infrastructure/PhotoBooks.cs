using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ailu.Api.Data;
using Ailu.Api.Domain;
using SkiaSharp;

namespace Ailu.Api.Infrastructure;

public sealed class PhotoBookOptions
{
    // Roadmap: 9 900 ₸ a book; print and delivery in Kazakhstan cost about 4 500 ₸. Sold through Kaspi,
    // outside the app stores.
    public int PriceKzt { get; set; } = 9900;

    // Development only: a stand-in for Kaspi whose payment page is a link back to this server that pays
    // at once. Refused at startup outside Development and Testing.
    public bool UseFakeKaspi { get; set; }

    // Lets whoever prints the books (by hand for now) report an order shipped or delivered with
    // POST /v1/print/notices and this value in the X-Print-Secret header. Empty turns the endpoint off.
    public string PrintSecret { get; set; } = "";
}

// What gets printed: the cover and the twenty spreads. Built from the live book for the preview, and kept
// as JSON on each order so the printed copy is the book as it was paid for.
public sealed record BookContent(string Title, string? Subtitle, Guid? CoverPhotoId, IReadOnlyList<SpreadContent> Spreads)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static BookContent Of(PhotoBook book) => new(book.Title, book.Subtitle, book.CoverPhotoId,
        book.Spreads.OrderBy(s => s.Position)
            .Select(s => new SpreadContent(s.Layout, s.PhotoIds, s.Title, s.Text, s.Date)).ToList());

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static BookContent FromJson(string json) => JsonSerializer.Deserialize<BookContent>(json, Json)!;

    public IEnumerable<Guid> PhotoIds() =>
        Spreads.SelectMany(s => s.PhotoIds).Append(CoverPhotoId ?? Guid.Empty).Where(id => id != Guid.Empty).Distinct();
}

public sealed record SpreadContent(SpreadLayout Layout, Guid[] PhotoIds, string? Title, string? Text, DateOnly? Date);

// ---- Kaspi ----

public sealed record PaymentLink(string PaymentId, string Url);

// What Kaspi told the server about a payment: paid, or failed / cancelled on Kaspi's side.
public sealed record PaymentNotice(string PaymentId, bool Paid);

// Kaspi Pay for a merchant account (roadmap: the book is sold through Kaspi, not the app stores). The real
// client needs that account; until then the server runs with UnconfiguredKaspi and orders wait unpaid.
public interface IKaspiPayments
{
    // A payment for the order, and the Kaspi page the app opens to pay it. Throws PaymentUnavailableException
    // when Kaspi can't be asked.
    Task<PaymentLink> CreateAsync(Guid orderId, int amountKzt, string description, CancellationToken ct = default);

    // Reads and checks a notice Kaspi posted to /v1/payments/kaspi. Null when it isn't a genuine one.
    Task<PaymentNotice?> ReadNoticeAsync(HttpRequest request, CancellationToken ct = default);
}

public sealed class PaymentUnavailableException(string message) : Exception(message);

public sealed class UnconfiguredKaspi : IKaspiPayments
{
    public Task<PaymentLink> CreateAsync(Guid orderId, int amountKzt, string description, CancellationToken ct = default) =>
        throw new PaymentUnavailableException("Kaspi payments are not configured on this server.");

    public Task<PaymentNotice?> ReadNoticeAsync(HttpRequest request, CancellationToken ct = default) =>
        Task.FromResult<PaymentNotice?>(null);
}

// Development only (PhotoBook:UseFakeKaspi). The "payment page" is GET /v1/payments/kaspi/fake/{id} on this
// server, which pays the order; notices are plain JSON { paymentId, paid }.
public sealed class FakeKaspi : IKaspiPayments
{
    public Task<PaymentLink> CreateAsync(Guid orderId, int amountKzt, string description, CancellationToken ct = default)
    {
        var id = $"fake-{orderId:N}";
        return Task.FromResult(new PaymentLink(id, $"/v1/payments/kaspi/fake/{id}"));
    }

    public async Task<PaymentNotice?> ReadNoticeAsync(HttpRequest request, CancellationToken ct = default)
    {
        try
        {
            return await request.ReadFromJsonAsync<PaymentNotice>(ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// ---- Printing ----

public sealed record PrintJob(Guid OrderId, int Year, string PdfKey, byte[] Pdf, string RecipientName, string Phone,
    string City, string Address, string? Comment);

public sealed record PrintNotice(string JobId, PhotoBookOrderStatus Status, string? TrackingNumber);

// The print shop. Which one (and its API) is still open; until then ManualPrintVendor keeps the print file
// and logs where it is, a person prints it, and reports progress through /v1/print/notices.
public interface IPrintVendor
{
    // Hands the book over for printing and delivery. Returns the vendor's job id.
    Task<string> SubmitAsync(PrintJob job, CancellationToken ct = default);

    // Reads and checks a progress notice (shipped, delivered). Null when it isn't a genuine one.
    Task<PrintNotice?> ReadNoticeAsync(HttpRequest request, CancellationToken ct = default);
}

public sealed class ManualPrintVendor(PhotoBookOptions options, ILogger<ManualPrintVendor> log) : IPrintVendor
{
    public Task<string> SubmitAsync(PrintJob job, CancellationToken ct = default)
    {
        log.LogWarning("Photo book order {Order} is paid and waits to be printed by hand: print file {Key}, {Name}, {City}",
            job.OrderId, job.PdfKey, job.RecipientName, job.City);
        return Task.FromResult($"manual-{job.OrderId:N}");
    }

    public async Task<PrintNotice?> ReadNoticeAsync(HttpRequest request, CancellationToken ct = default)
    {
        if (options.PrintSecret == "" || request.Headers["X-Print-Secret"] is not [{ } secret] ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(options.PrintSecret)))
            return null;
        try
        {
            return await request.ReadFromJsonAsync<PrintNotice>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter() }
            }, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public static class BookKeys
{
    public static string PrintFile(Guid coupleId, Guid orderId) => $"couples/{coupleId:N}/books/{orderId:N}/print.pdf";
}

// ---- PDF ----

// Draws a book as a PDF: the cover page, then each spread as one wide page (two facing 20 × 20 cm pages),
// the way print shops take photo books. The preview uses screen-size photos; the print file the originals
// the couple keeps (2 560 px, about 330 dpi on a full page).
public sealed class BookPdf(IPhotoStore store)
{
    private const float PointsPerCm = 72f / 2.54f;
    private static readonly float Page = (float)(BookLayouts.PageCm * PointsPerCm);

    private static readonly SKColor Paper = new(0xFF, 0xFD, 0xF9);
    private static readonly SKColor Ink = new(0x2A, 0x1E, 0x18);
    private static readonly SKColor Muted = new(0x8F, 0x85, 0x7B);
    private static readonly SKColor Hole = new(0xEE, 0xE8, 0xE1);

    private static readonly Lazy<SKTypeface> Regular = new(() => Font("Inter-Regular.otf"));
    private static readonly Lazy<SKTypeface> SemiBold = new(() => Font("Inter-SemiBold.otf"));

    public async Task<byte[]> RenderAsync(Guid coupleId, BookContent book, bool print, CancellationToken ct = default)
    {
        var photos = new Dictionary<Guid, SKImage?>();
        try
        {
            foreach (var id in book.PhotoIds()) photos[id] = await LoadAsync(coupleId, id, print, ct);

            using var output = new MemoryStream();
            using (var doc = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata
                   {
                       Title = book.Title, Creator = "Ailu", Producer = "Ailu", RasterDpi = 300, EncodingQuality = 90
                   }))
            {
                DrawCover(doc.BeginPage(Page, Page), book, photos);
                doc.EndPage();
                foreach (var spread in book.Spreads)
                {
                    DrawSpread(doc.BeginPage(Page * 2, Page), spread, photos);
                    doc.EndPage();
                }
                doc.Close();
            }
            return output.ToArray();
        }
        finally
        {
            foreach (var image in photos.Values) image?.Dispose();
        }
    }

    private async Task<SKImage?> LoadAsync(Guid coupleId, Guid photoId, bool print, CancellationToken ct)
    {
        // The screen size may not have been made yet; the original always exists while the photo does.
        var stream = print ? null : await store.OpenAsync(PhotoKeys.File(coupleId, photoId, PhotoProcessor.Screen), ct);
        stream ??= await store.OpenAsync(PhotoKeys.File(coupleId, photoId, PhotoProcessor.Full), ct);
        if (stream is null) return null;
        await using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            // Encoded data goes into the PDF as is: no decoding, no second JPEG pass.
            return SKImage.FromEncodedData(buffer.ToArray());
        }
    }

    private static void DrawCover(SKCanvas canvas, BookContent book, Dictionary<Guid, SKImage?> photos)
    {
        canvas.Clear(Paper);
        DrawPhoto(canvas, Rect(BookLayouts.CoverPhoto, Page, Page), book.CoverPhotoId, photos);
        DrawCentered(canvas, book.Title, Rect(BookLayouts.CoverTitle, Page, Page), SemiBold.Value, 26, Ink);
        if (!string.IsNullOrEmpty(book.Subtitle))
            DrawCentered(canvas, book.Subtitle, Rect(BookLayouts.CoverSubtitle, Page, Page), Regular.Value, 14, Muted);
    }

    private static void DrawSpread(SKCanvas canvas, SpreadContent spread, Dictionary<Guid, SKImage?> photos)
    {
        canvas.Clear(Paper);
        var (w, h) = (Page * 2, Page);
        var boxes = BookLayouts.Slots(spread.Layout);
        for (var i = 0; i < boxes.Count; i++)
            DrawPhoto(canvas, Rect(boxes[i], w, h), i < spread.PhotoIds.Length ? spread.PhotoIds[i] : null, photos);

        var date = spread.Date?.ToString("dd.MM.yyyy");
        if (BookLayouts.HasStory(spread.Layout))
        {
            DrawStory(canvas, Rect(BookLayouts.StoryText, w, h), spread.Title, date, spread.Text);
            return;
        }
        var caption = string.Join(" · ", new[] { spread.Title, date }.Where(x => !string.IsNullOrEmpty(x)));
        if (caption == "") return;
        var box = Rect(BookLayouts.Caption, w, h);
        using var font = new SKFont(Regular.Value, 9);
        using var paint = new SKPaint { Color = Muted, IsAntialias = true };
        canvas.DrawText(Fit(caption, box.Width, font), box.Left, box.MidY + 3, SKTextAlign.Left, font, paint);
    }

    // The story page: the memory's title, its date, then its words, cut with an ellipsis if they don't fit.
    private static void DrawStory(SKCanvas canvas, SKRect box, string? title, string? date, string? text)
    {
        using var paint = new SKPaint { Color = Ink, IsAntialias = true };
        var y = box.Top;
        if (!string.IsNullOrEmpty(title))
        {
            using var heading = new SKFont(SemiBold.Value, 18);
            foreach (var line in Wrap(title, box.Width, heading).Take(3))
            {
                y += 24;
                canvas.DrawText(line, box.Left, y, SKTextAlign.Left, heading, paint);
            }
        }
        if (date is not null)
        {
            using var small = new SKFont(Regular.Value, 10);
            using var muted = new SKPaint { Color = Muted, IsAntialias = true };
            y += 20;
            canvas.DrawText(date, box.Left, y, SKTextAlign.Left, small, muted);
        }
        if (string.IsNullOrEmpty(text)) return;

        using var body = new SKFont(Regular.Value, 11);
        const float leading = 16;
        y += 12;
        var lines = text.Split('\n').SelectMany(p => p.Trim() == "" ? [""] : Wrap(p, box.Width, body)).ToList();
        var room = (int)((box.Bottom - y) / leading);
        if (lines.Count > room && room > 0) lines = [.. lines.Take(room - 1), Fit(lines[room - 1] + "…", box.Width, body)];
        foreach (var line in lines.Take(room))
        {
            y += leading;
            canvas.DrawText(line, box.Left, y, SKTextAlign.Left, body, paint);
        }
    }

    // Fills the box, cropping the photo round its centre. A missing photo leaves a soft grey box (only a
    // preview can have one: an order needs every box filled).
    private static void DrawPhoto(SKCanvas canvas, SKRect box, Guid? id, Dictionary<Guid, SKImage?> photos)
    {
        if (id is not { } photoId || photoId == Guid.Empty || photos.GetValueOrDefault(photoId) is not { } image)
        {
            using var hole = new SKPaint { Color = Hole };
            canvas.DrawRect(box, hole);
            return;
        }
        var scale = Math.Max(box.Width / image.Width, box.Height / image.Height);
        var (w, h) = (image.Width * scale, image.Height * scale);
        canvas.Save();
        canvas.ClipRect(box);
        canvas.DrawImage(image, SKRect.Create(box.MidX - w / 2, box.MidY - h / 2, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Restore();
    }

    private static void DrawCentered(SKCanvas canvas, string text, SKRect box, SKTypeface face, float size, SKColor color)
    {
        using var font = new SKFont(face, size);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(Fit(text, box.Width, font), box.MidX, box.MidY + size / 3, SKTextAlign.Center, font, paint);
    }

    private static IEnumerable<string> Wrap(string text, float width, SKFont font)
    {
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line == "" ? word : $"{line} {word}";
            if (line != "" && font.MeasureText(candidate) > width)
            {
                yield return line;
                line = word;
            }
            else line = candidate;
        }
        if (line != "") yield return Fit(line, width, font);
    }

    private static string Fit(string text, float width, SKFont font)
    {
        if (font.MeasureText(text) <= width) return text;
        while (text.Length > 1 && font.MeasureText(text + "…") > width) text = text[..^1];
        return text.TrimEnd() + "…";
    }

    private static SKRect Rect(Box b, float w, float h) =>
        SKRect.Create((float)(b.X * w), (float)(b.Y * h), (float)(b.W * w), (float)(b.H * h));

    private static SKTypeface Font(string file)
    {
        using var stream = typeof(BookPdf).Assembly.GetManifestResourceStream($"Fonts.{file}")
                           ?? throw new InvalidOperationException($"Font {file} is not embedded.");
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException($"Font {file} can't be read.");
    }
}

// The PDF preview opens in the phone's browser, which can't send the bearer token, so its URL is signed
// like a photo's: it works for one to two hours and names nothing but the book.
public sealed class BookUrls(AuthOptions auth, TimeProvider clock)
{
    private readonly byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes("books:" + auth.SigningKey));

    public string Preview(Guid bookId)
    {
        var expires = (clock.GetUtcNow().ToUnixTimeSeconds() / 3600 + 2) * 3600;
        return $"/v1/photo-books/{bookId:N}/preview.pdf?exp={expires}&sig={Sign(bookId, expires)}";
    }

    public bool IsValid(Guid bookId, long expires, string? sig) =>
        sig is not null && expires >= clock.GetUtcNow().ToUnixTimeSeconds() &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(bookId, expires)), Encoding.ASCII.GetBytes(sig));

    private string Sign(Guid bookId, long expires) =>
        Base64Url.EncodeToString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"{bookId:N}:{expires}")))[..32];
}
