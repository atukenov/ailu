using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Features.PhotoBook;

public sealed record BookYear(int Year, int Memories, int Photos, bool HasBook);
public sealed record BookYears(IReadOnlyList<BookYear> Years, int PriceKzt, int Spreads);

public sealed record BookPhotoDto(Guid Id, int Width, int Height, string Thumb, string Screen);
public sealed record SlotDto(Box Box, BookPhotoDto? Photo);
public sealed record SpreadDto(int Position, string Layout, IReadOnlyList<SlotDto> Slots, Box? Story, Box? Caption,
    Guid? MemoryId, string? Title, string? Text, string? Date, int Empty);
public sealed record CoverDto(string Title, string? Subtitle, BookPhotoDto? Photo, Box PhotoBox, Box TitleBox, Box SubtitleBox);
public sealed record LayoutDto(string Layout, IReadOnlyList<Box> Slots, Box? Story);
public sealed record OrderDto(Guid Id, int Year, string Status, int PriceKzt, bool Mine, string? RecipientName, string? Phone,
    string? City, string? Address, string? Comment, string? PayUrl, string? TrackingNumber, DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt, DateTimeOffset? ShippedAt, DateTimeOffset? DeliveredAt);

// Empty counts the boxes still without a photo, the cover's included; the book can be ordered at 0.
// Locked while an order waits for payment, so the book paid for is the book that was ordered.
public sealed record BookDto(Guid Id, int Year, CoverDto Cover, IReadOnlyList<SpreadDto> Spreads, int Empty, bool Locked,
    string PreviewUrl, int PriceKzt, IReadOnlyList<OrderDto> Orders, IReadOnlyList<LayoutDto> Layouts);

public sealed record YearMemoryDto(Guid MemoryId, string Title, DateOnly HappenedOn, IReadOnlyList<BookPhotoDto> Photos);
public sealed record YearPhotos(IReadOnlyList<YearMemoryDto> Memories, IReadOnlyList<Guid> Used);

public sealed record CoverRequest(string? Title, string? Subtitle, Guid? CoverPhotoId);
// PhotoIds lists a photo (or null for an empty box) for each box of the layout, in the layout's order.
public sealed record SpreadRequest(string? Layout, Guid?[]? PhotoIds, string? Title, string? Text);
public sealed record MoveRequest(int To);
public sealed record OrderRequest(string? RecipientName, string? Phone, string? City, string? Address, string? Comment);

public static class PhotoBookEndpoints
{
    public const int TitleMax = 250, TextMax = 1000, CoverTitleMax = 80;

    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/couple/photo-books").WithTags("Photo book").RequireAuthorization();
        g.MapGet("", Years);
        g.MapGet("/{year:int}", Get);
        g.MapPost("/{year:int}", Build);
        g.MapPatch("/{year:int}", UpdateCover);
        g.MapGet("/{year:int}/photos", Photos);
        g.MapPut("/{year:int}/spreads/{position:int}", UpdateSpread);
        g.MapPost("/{year:int}/spreads/{position:int}/move", Move);
        g.MapPost("/{year:int}/orders", Order);
        g.MapPost("/orders/{id:guid}/pay", Pay);
        g.MapDelete("/orders/{id:guid}", Cancel);

        // Anonymous: the signature is the permission (see BookUrls), Kaspi and the printer check their own.
        app.MapGet("/v1/photo-books/{id:guid}/preview.pdf", Preview).WithTags("Photo book");
        app.MapPost("/v1/payments/kaspi", KaspiNotice).WithTags("Photo book");
        app.MapPost("/v1/print/notices", PrintNotice).WithTags("Photo book");
        if (app.ServiceProvider.GetRequiredService<PhotoBookOptions>().UseFakeKaspi)
            app.MapGet("/v1/payments/kaspi/fake/{paymentId}", FakePay).WithTags("Photo book");
    }

    // The years the couple has photos from, newest first: each can become a book.
    private static async Task<IResult> Years(HttpContext http, AiluDb db, PhotoBookOptions options, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();

        var memories = await LiveMemories(db, scope.Couple.Id).Where(m => m.Photos.Any())
            .Select(m => new { m.HappenedOn.Year, Photos = m.Photos.Count }).ToListAsync(ct);
        var books = await db.PhotoBooks.Where(b => b.CoupleId == scope.Couple.Id).Select(b => b.Year).ToListAsync(ct);
        var years = memories.GroupBy(m => m.Year)
            .Select(y => new BookYear(y.Key, y.Count(), y.Sum(m => m.Photos), books.Contains(y.Key)))
            .Concat(books.Where(y => memories.All(m => m.Year != y)).Select(y => new BookYear(y, 0, 0, true)))
            .OrderByDescending(y => y.Year).ToList();
        return Results.Ok(new BookYears(years, options.PriceKzt, BookLayouts.Spreads));
    }

    private static async Task<IResult> Get(int year, HttpContext http, AiluDb db, PhotoUrls urls, BookUrls bookUrls,
        PhotoBookOptions options, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var book = await Book(db, scope, year, ct);
        return book is null ? NotFound() : Results.Ok(await ToDto(book, scope, db, urls, bookUrls, options, ct));
    }

    // Lays the book out from the year's memories, or lays it out again: this replaces every spread and the
    // cover photo (the title stays).
    private static async Task<IResult> Build(int year, HttpContext http, AiluDb db, PhotoUrls urls, BookUrls bookUrls,
        PhotoBookOptions options, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        if (year is < 1950 or > 9999) return Errors.Validation("year", "Такого года нет.");

        var memories = await LiveMemories(db, scope.Couple.Id)
            .Where(m => m.HappenedOn.Year == year && m.Photos.Any())
            .Select(m => new BookMemory(m.Id, m.HappenedOn, m.CreatedAt, m.Title, m.Text, m.Place, m.Favorite,
                m.Reactions.Count, m.Photos.OrderBy(p => p.Position).Select(p => new BookPhoto(p.Id, p.Width, p.Height)).ToList()))
            .ToListAsync(ct);
        if (memories.Count == 0) return Errors.Conflict("no_photos", "В этом году у вас нет воспоминаний с фотографиями.");

        var book = await Book(db, scope, year, ct);
        if (book is not null && await Locked(db, book, ct)) return LockedError();
        var built = BookBuilder.Build(memories);
        var now = clock.GetUtcNow();
        if (book is null)
        {
            var names = scope.Couple.Members.Where(m => m.LeftAt is null).OrderBy(m => m.Role)
                .Select(m => m.User?.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();
            book = new Data.PhotoBook
            {
                Id = Guid.NewGuid(), CoupleId = scope.Couple.Id, Year = year, CreatedAt = now,
                Title = names.Count > 0 ? string.Join(" & ", names) : "Ailu", Subtitle = year.ToString(),
                Spreads = Enumerable.Range(0, BookLayouts.Spreads).Select(i => new PhotoBookSpread { Position = i }).ToList()
            };
            db.PhotoBooks.Add(book);
        }
        book.CoverPhotoId = built.CoverPhotoId;
        book.UpdatedAt = now;
        foreach (var spread in book.Spreads)
        {
            var b = built.Spreads[spread.Position];
            Set(spread, b.Layout, [.. b.Photos], b.MemoryId, Cut(b.Title, TitleMax), Cut(b.Text, TextMax), b.Date);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The partner built the same year's book at the same moment; theirs is the one.
            db.ChangeTracker.Clear();
            book = (await Book(db, scope, year, ct))!;
        }
        return Results.Ok(await ToDto(book, scope, db, urls, bookUrls, options, ct));
    }

    private static async Task<IResult> UpdateCover(int year, CoverRequest body, HttpContext http, AiluDb db, PhotoUrls urls,
        BookUrls bookUrls, PhotoBookOptions options, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var book = await Book(db, scope, year, ct);
        if (book is null) return NotFound();
        if (await Locked(db, book, ct)) return LockedError();

        if (body.Title is not null)
        {
            var title = body.Title.Trim();
            if (title.Length is 0 or > CoverTitleMax) return Errors.Validation("title", "Название от 1 до 80 символов.");
            book.Title = title;
        }
        if (body.Subtitle is not null)
        {
            var subtitle = body.Subtitle.Trim();
            if (subtitle.Length > CoverTitleMax) return Errors.Validation("subtitle", "Подзаголовок до 80 символов.");
            book.Subtitle = subtitle == "" ? null : subtitle;
        }
        if (body.CoverPhotoId is Guid cover)
        {
            if ((await LivePhotos(db, scope.Couple.Id, [cover], ct)).Count == 0)
                return Errors.Validation("coverPhotoId", "Этой фотографии больше нет.");
            book.CoverPhotoId = cover;
        }
        book.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToDto(book, scope, db, urls, bookUrls, options, ct));
    }

    // The year's memories with photos, to pick from in the spread editor, and which photos the book uses.
    private static async Task<IResult> Photos(int year, HttpContext http, AiluDb db, PhotoUrls urls, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var book = await Book(db, scope, year, ct);

        var memories = await LiveMemories(db, scope.Couple.Id).Include(m => m.Photos)
            .Where(m => m.HappenedOn.Year == year && m.Photos.Any())
            .OrderBy(m => m.HappenedOn).ThenBy(m => m.CreatedAt).ToListAsync(ct);
        var used = book is null ? [] : BookContent.Of(book).PhotoIds().ToList();
        return Results.Ok(new YearPhotos(
            memories.Select(m => new YearMemoryDto(m.Id, m.Title, m.HappenedOn,
                m.Photos.OrderBy(p => p.Position).Select(p => Photo(p, urls)).ToList())).ToList(),
            used));
    }

    private static async Task<IResult> UpdateSpread(int year, int position, SpreadRequest body, HttpContext http, AiluDb db,
        PhotoUrls urls, BookUrls bookUrls, PhotoBookOptions options, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var book = await Book(db, scope, year, ct);
        if (book is null) return NotFound();
        var spread = book.Spreads.SingleOrDefault(s => s.Position == position);
        if (spread is null) return Errors.NotFound("spread_not_found", "Такого разворота нет.");
        if (await Locked(db, book, ct)) return LockedError();

        if (!BookLayouts.TryParse(body.Layout, out var layout)) return Errors.Validation("layout", "Такого макета нет.");
        var ids = (body.PhotoIds ?? []).Select(id => id ?? Guid.Empty).ToArray();
        if (ids.Length != BookLayouts.Slots(layout).Count)
            return Errors.Validation("photoIds", "Фотографий должно быть столько же, сколько мест в макете.");
        var chosen = ids.Where(id => id != Guid.Empty).ToList();
        if (chosen.Count != chosen.Distinct().Count())
            return Errors.Validation("photoIds", "Одна фотография дважды на одном развороте.");
        var live = await LivePhotos(db, scope.Couple.Id, chosen, ct);
        if (live.Count != chosen.Count) return Errors.Validation("photoIds", "Какой-то из этих фотографий больше нет.");

        var title = string.IsNullOrWhiteSpace(body.Title) ? null : body.Title.Trim();
        if (title?.Length > TitleMax) return Errors.Validation("title", "Подпись до 250 символов.");
        var text = string.IsNullOrWhiteSpace(body.Text) ? null : body.Text.Trim();
        if (text?.Length > TextMax) return Errors.Validation("text", "Текст до 1000 символов.");

        // The spread belongs to the moment its first photo comes from: that memory's day is the date printed.
        var (memoryId, date) = (spread.MemoryId, spread.Date);
        if (chosen.Count > 0 && !spread.PhotoIds.Contains(chosen[0]))
        {
            var first = live[chosen[0]];
            (memoryId, date) = (first.MemoryId, first.HappenedOn);
        }
        Set(spread, layout, ids, memoryId, title, text, date);
        book.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToDto(book, scope, db, urls, bookUrls, options, ct));
    }

    // Moves a spread to another place; the ones between shift by one.
    private static async Task<IResult> Move(int year, int position, MoveRequest body, HttpContext http, AiluDb db,
        PhotoUrls urls, BookUrls bookUrls, PhotoBookOptions options, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var book = await Book(db, scope, year, ct);
        if (book is null) return NotFound();
        if (position is < 0 or >= BookLayouts.Spreads) return Errors.NotFound("spread_not_found", "Такого разворота нет.");
        if (body.To is < 0 or >= BookLayouts.Spreads) return Errors.Validation("to", "Такого места в книге нет.");
        if (await Locked(db, book, ct)) return LockedError();

        // Rows keep their positions; their contents move.
        var rows = book.Spreads.OrderBy(s => s.Position).ToList();
        var contents = rows.Select(s => (s.Layout, s.PhotoIds, s.MemoryId, s.Title, s.Text, s.Date)).ToList();
        var moving = contents[position];
        contents.RemoveAt(position);
        contents.Insert(body.To, moving);
        for (var i = 0; i < rows.Count; i++)
        {
            var c = contents[i];
            Set(rows[i], c.Layout, c.PhotoIds, c.MemoryId, c.Title, c.Text, c.Date);
        }
        book.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToDto(book, scope, db, urls, bookUrls, options, ct));
    }

    // Orders a printed copy: keeps the book as it is now and asks Kaspi for a payment. If Kaspi can't be
    // reached the order is still kept, without a payment link; "pay" asks again.
    private static async Task<IResult> Order(int year, OrderRequest body, HttpContext http, AiluDb db, IKaspiPayments kaspi,
        PhotoBookOptions options, TimeProvider clock, ILogger<PhotoBookOrder> log, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var book = await Book(db, scope, year, ct);
        if (book is null) return NotFound();
        if (await Locked(db, book, ct))
            return Errors.Conflict("order_waiting", "Эта книга уже заказана и ждёт оплаты.");

        var name = body.RecipientName?.Trim() ?? "";
        if (name.Length is 0 or > 100) return Errors.Validation("recipientName", "Имя получателя от 1 до 100 символов.");
        if (PhoneNumber.Normalize(body.Phone) is not { } phone) return Errors.Validation("phone", "Нужен казахстанский номер.");
        var city = body.City?.Trim() ?? "";
        if (city.Length is 0 or > 60) return Errors.Validation("city", "Город от 1 до 60 символов.");
        var address = body.Address?.Trim() ?? "";
        if (address.Length is 0 or > 250) return Errors.Validation("address", "Адрес от 1 до 250 символов.");
        var comment = string.IsNullOrWhiteSpace(body.Comment) ? null : body.Comment.Trim();
        if (comment?.Length > 250) return Errors.Validation("comment", "Комментарий до 250 символов.");

        var content = BookContent.Of(book);
        if (await EmptyBoxes(db, scope.Couple.Id, content, ct) > 0)
            return Errors.Conflict("book_incomplete", "В книге остались пустые места. Добавьте в них фотографии.");

        var order = new PhotoBookOrder
        {
            Id = Guid.NewGuid(), BookId = book.Id, CoupleId = scope.Couple.Id, OrderedById = scope.UserId, Year = year,
            Status = PhotoBookOrderStatus.AwaitingPayment, PriceKzt = options.PriceKzt, RecipientName = name, Phone = phone,
            City = city, Address = address, Comment = comment, Content = content.ToJson(), CreatedAt = clock.GetUtcNow()
        };
        db.PhotoBookOrders.Add(order);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The partner ordered the same book a moment ago.
            return Errors.Conflict("order_waiting", "Эта книга уже заказана и ждёт оплаты.");
        }
        await RequestPaymentAsync(order, db, kaspi, log, ct);
        return Results.Created($"/v1/couple/photo-books/orders/{order.Id}", ToDto(order, scope));
    }

    private static async Task<IResult> Pay(Guid id, HttpContext http, AiluDb db, IKaspiPayments kaspi,
        ILogger<PhotoBookOrder> log, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var order = await db.PhotoBookOrders.SingleOrDefaultAsync(o => o.Id == id && o.CoupleId == scope.Couple.Id, ct);
        if (order is null) return OrderNotFound();
        if (order.Status != PhotoBookOrderStatus.AwaitingPayment)
            return Errors.Conflict("order_not_waiting", "Этот заказ уже оплачен или отменён.");
        if (order.PayUrl is null) await RequestPaymentAsync(order, db, kaspi, log, ct);
        return Results.Ok(ToDto(order, scope));
    }

    private static async Task<IResult> Cancel(Guid id, HttpContext http, AiluDb db, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var order = await db.PhotoBookOrders.SingleOrDefaultAsync(o => o.Id == id && o.CoupleId == scope.Couple.Id, ct);
        if (order is null) return OrderNotFound();
        if (order.Status == PhotoBookOrderStatus.Cancelled) return Results.Ok(ToDto(order, scope));
        if (order.Status != PhotoBookOrderStatus.AwaitingPayment)
            return Errors.Conflict("order_paid", "Оплаченный заказ отменить в приложении нельзя. Напишите нам.");
        order.Status = PhotoBookOrderStatus.Cancelled;
        order.CancelledAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(order, scope));
    }

    private static async Task<IResult> Preview(Guid id, long exp, string? sig, AiluDb db, BookUrls bookUrls, BookPdf pdf,
        CancellationToken ct)
    {
        if (!bookUrls.IsValid(id, exp, sig)) return Results.NotFound();
        var book = await db.PhotoBooks.Include(b => b.Spreads).AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return Results.NotFound();
        // A photo deleted since the layout was made shows as an empty box, as in the app.
        var content = BookContent.Of(book);
        var live = (await LivePhotos(db, book.CoupleId, content.PhotoIds().ToList(), ct)).Keys.ToHashSet();
        content = content with
        {
            CoverPhotoId = content.CoverPhotoId is { } c && live.Contains(c) ? c : null,
            Spreads = content.Spreads.Select(s => s with { PhotoIds = s.PhotoIds.Select(p => live.Contains(p) ? p : Guid.Empty).ToArray() }).ToList()
        };
        var file = await pdf.RenderAsync(book.CoupleId, content, print: false, ct);
        return Results.File(file, "application/pdf", $"ailu-{book.Year}.pdf");
    }

    private static async Task<IResult> KaspiNotice(HttpRequest request, IKaspiPayments kaspi, PhotoBookPrinting printing,
        CancellationToken ct)
    {
        if (await kaspi.ReadNoticeAsync(request, ct) is not { } notice) return Results.Unauthorized();
        if (notice.Paid) await printing.MarkPaidAsync(notice.PaymentId, ct);
        else await printing.MarkFailedAsync(notice.PaymentId, ct);
        return Results.Ok();
    }

    private static async Task<IResult> FakePay(string paymentId, PhotoBookPrinting printing, CancellationToken ct) =>
        await printing.MarkPaidAsync(paymentId, ct)
            ? Results.Content("<!doctype html><meta charset=utf-8><p>Оплачено (тестовый Kaspi). Вернитесь в Ailu.</p>", "text/html")
            : Results.NotFound();

    private static async Task<IResult> PrintNotice(HttpRequest request, IPrintVendor vendor, AiluDb db, TimeProvider clock,
        CancellationToken ct)
    {
        if (await vendor.ReadNoticeAsync(request, ct) is not { } notice) return Results.Unauthorized();
        var order = await db.PhotoBookOrders.SingleOrDefaultAsync(o => o.PrintJobId == notice.JobId, ct);
        if (order is null) return Results.NotFound();
        var now = clock.GetUtcNow();
        switch (notice.Status)
        {
            case PhotoBookOrderStatus.Shipped when order.Status == PhotoBookOrderStatus.Printing:
                order.Status = PhotoBookOrderStatus.Shipped;
                order.ShippedAt = now;
                break;
            case PhotoBookOrderStatus.Delivered when order.Status is PhotoBookOrderStatus.Printing or PhotoBookOrderStatus.Shipped:
                order.Status = PhotoBookOrderStatus.Delivered;
                order.ShippedAt ??= now;
                order.DeliveredAt = now;
                break;
            case PhotoBookOrderStatus.Shipped or PhotoBookOrderStatus.Delivered:
                break; // a repeat or late notice
            default:
                return Results.BadRequest();
        }
        if (notice.TrackingNumber is { Length: > 0 and <= 100 } tracking) order.TrackingNumber = tracking;
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    }

    private static async Task RequestPaymentAsync(PhotoBookOrder order, AiluDb db, IKaspiPayments kaspi,
        ILogger<PhotoBookOrder> log, CancellationToken ct)
    {
        try
        {
            var link = await kaspi.CreateAsync(order.Id, order.PriceKzt, $"Ailu: фотокнига {order.Year}", ct);
            order.PaymentId = link.PaymentId;
            order.PayUrl = link.Url;
            await db.SaveChangesAsync(ct);
        }
        catch (PaymentUnavailableException e)
        {
            log.LogWarning("No Kaspi payment for photo book order {Order}: {Reason}", order.Id, e.Message);
        }
    }

    private static void Set(PhotoBookSpread spread, SpreadLayout layout, Guid[] photos, Guid? memoryId, string? title, string? text,
        DateOnly? date)
    {
        spread.Layout = layout;
        spread.PhotoIds = photos;
        spread.MemoryId = memoryId;
        spread.Title = title;
        spread.Text = text;
        spread.Date = date;
    }

    private static async Task<BookDto> ToDto(Data.PhotoBook book, CoupleScope scope, AiluDb db, PhotoUrls urls, BookUrls bookUrls,
        PhotoBookOptions options, CancellationToken ct)
    {
        var content = BookContent.Of(book);
        var live = await LivePhotos(db, scope.Couple.Id, content.PhotoIds().ToList(), ct);
        BookPhotoDto? PhotoOf(Guid? id) => id is { } x && live.TryGetValue(x, out var p) ? Photo(p.Photo, urls) : null;

        var spreads = book.Spreads.OrderBy(s => s.Position).Select(s =>
        {
            var boxes = BookLayouts.Slots(s.Layout);
            var slots = boxes.Select((box, i) => new SlotDto(box, PhotoOf(i < s.PhotoIds.Length ? s.PhotoIds[i] : null))).ToList();
            var story = BookLayouts.HasStory(s.Layout);
            return new SpreadDto(s.Position, BookLayouts.Name(s.Layout), slots, story ? BookLayouts.StoryText : null,
                story ? null : BookLayouts.Caption, s.MemoryId, s.Title, s.Text, s.Date?.ToString("yyyy-MM-dd"),
                slots.Count(x => x.Photo is null));
        }).ToList();
        var cover = PhotoOf(book.CoverPhotoId);
        var orders = await db.PhotoBookOrders.Where(o => o.BookId == book.Id && o.Status != PhotoBookOrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAt).ToListAsync(ct);

        return new BookDto(book.Id, book.Year,
            new CoverDto(book.Title, book.Subtitle, cover, BookLayouts.CoverPhoto, BookLayouts.CoverTitle, BookLayouts.CoverSubtitle),
            spreads, spreads.Sum(s => s.Empty) + (cover is null ? 1 : 0),
            orders.Any(o => o.Status == PhotoBookOrderStatus.AwaitingPayment), bookUrls.Preview(book.Id), options.PriceKzt,
            orders.Select(o => ToDto(o, scope)).ToList(),
            Enum.GetValues<SpreadLayout>().Select(l =>
                new LayoutDto(BookLayouts.Name(l), BookLayouts.Slots(l), BookLayouts.HasStory(l) ? BookLayouts.StoryText : null)).ToList());
    }

    private static OrderDto ToDto(PhotoBookOrder o, CoupleScope scope) => new(o.Id, o.Year, Status(o.Status), o.PriceKzt,
        o.OrderedById == scope.UserId, o.RecipientName, o.Phone, o.City, o.Address, o.Comment,
        o.Status == PhotoBookOrderStatus.AwaitingPayment ? o.PayUrl : null, o.TrackingNumber, o.CreatedAt, o.PaidAt,
        o.ShippedAt, o.DeliveredAt);

    private static string Status(PhotoBookOrderStatus status) => status switch
    {
        PhotoBookOrderStatus.AwaitingPayment => "awaiting_payment",
        var other => other.ToString().ToLowerInvariant()
    };

    private static BookPhotoDto Photo(MemoryPhoto p, PhotoUrls urls) =>
        new(p.Id, p.Width, p.Height, urls.For(p.Id, PhotoProcessor.Thumb), urls.For(p.Id, PhotoProcessor.Screen));

    private static IQueryable<Memory> LiveMemories(AiluDb db, Guid coupleId) =>
        db.Memories.Where(m => m.CoupleId == coupleId && m.DeletedAt == null);

    private sealed record LivePhoto(MemoryPhoto Photo, Guid MemoryId, DateOnly HappenedOn);

    // The couple's photos among these ids whose memory still exists.
    private static async Task<Dictionary<Guid, LivePhoto>> LivePhotos(AiluDb db, Guid coupleId, List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        return await db.MemoryPhotos.AsNoTracking()
            .Where(p => p.CoupleId == coupleId && ids.Contains(p.Id))
            .Join(db.Memories.Where(m => m.DeletedAt == null), p => p.MemoryId, m => m.Id, (p, m) => new LivePhoto(p, m.Id, m.HappenedOn))
            .ToDictionaryAsync(x => x.Photo.Id, ct);
    }

    private static async Task<int> EmptyBoxes(AiluDb db, Guid coupleId, BookContent content, CancellationToken ct)
    {
        var live = await LivePhotos(db, coupleId, content.PhotoIds().ToList(), ct);
        return content.Spreads.SelectMany(s => s.PhotoIds).Count(id => !live.ContainsKey(id)) +
               (content.CoverPhotoId is { } c && live.ContainsKey(c) ? 0 : 1);
    }

    private static Task<Data.PhotoBook?> Book(AiluDb db, CoupleScope scope, int year, CancellationToken ct) =>
        db.PhotoBooks.Include(b => b.Spreads).SingleOrDefaultAsync(b => b.CoupleId == scope.Couple.Id && b.Year == year, ct);

    private static Task<bool> Locked(AiluDb db, Data.PhotoBook book, CancellationToken ct) =>
        db.PhotoBookOrders.AnyAsync(o => o.BookId == book.Id && o.Status == PhotoBookOrderStatus.AwaitingPayment, ct);

    private static string? Cut(string? text, int max) => text is null || text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    private static IResult NotFound() => Errors.NotFound("photo_book_not_found", "Книги за этот год ещё нет.");
    private static IResult OrderNotFound() => Errors.NotFound("order_not_found", "Такого заказа нет.");

    private static IResult LockedError() =>
        Errors.Conflict("book_locked", "Книга заказана и ждёт оплаты. Чтобы её изменить, отмените заказ.");
}
