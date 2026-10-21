using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Features.Memories;
using Ailu.Api.Features.PhotoBook;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Ailu.Api.Tests;

public class BookBuilderTests
{
    private static int next;

    private static BookMemory Memory(string date, int photos, bool favorite = false, int reactions = 0, string? text = null,
        bool landscape = true) =>
        new(Guid.NewGuid(), DateOnly.Parse(date), DateTimeOffset.UnixEpoch.AddSeconds(next++), $"m{next}", text, null, favorite,
            reactions, Enumerable.Range(0, photos).Select(_ => new BookPhoto(Guid.NewGuid(), landscape ? 4 : 3, landscape ? 3 : 4)).ToList());

    [Fact]
    public void A_busy_year_keeps_every_month_and_the_favorites_in_twenty_spreads()
    {
        // 36 memories, three a month; one favorite in March among two ordinary ones.
        var memories = Enumerable.Range(1, 12).SelectMany(month => Enumerable.Range(1, 3)
            .Select(day => Memory($"2026-{month:00}-{day * 5:00}", 2, favorite: month == 3 && day == 2))).ToList();

        var book = BookBuilder.Build(memories);

        Assert.Equal(BookLayouts.Spreads, book.Spreads.Count);
        Assert.All(book.Spreads, s => Assert.NotNull(s.MemoryId));
        var dates = book.Spreads.Select(s => s.Date!.Value).ToList();
        Assert.Equal(dates.Order(), dates);
        Assert.Equal(12, dates.Select(d => d.Month).Distinct().Count());
        var favorite = memories.Single(m => m.Favorite);
        Assert.Contains(book.Spreads, s => s.MemoryId == favorite.Id);
        Assert.Equal(favorite.Photos[0].Id, book.CoverPhotoId);
    }

    [Fact]
    public void A_quiet_year_spreads_photos_out_and_leaves_the_rest_empty()
    {
        var memories = new[] { Memory("2026-01-02", 1, text: "Каток"), Memory("2026-05-09", 5), Memory("2026-08-30", 12) };

        var book = BookBuilder.Build(memories);

        // 18 photos, one per spread, then two empty spreads at the end.
        Assert.Equal(20, book.Spreads.Count);
        Assert.Equal(18, book.Spreads.Count(s => s.MemoryId is not null));
        Assert.Equal(SpreadLayout.PhotoText, book.Spreads[0].Layout);
        Assert.Equal("Каток", book.Spreads[0].Text);
        Assert.All(book.Spreads.Skip(18), s => Assert.Equal([Guid.Empty, Guid.Empty], s.Photos));
        // Each photo once, and the memory's words only on its first spread.
        var used = book.Spreads.SelectMany(s => s.Photos).Where(p => p != Guid.Empty).ToList();
        Assert.Equal(18, used.Distinct().Count());
        Assert.Single(book.Spreads, s => s.Text is not null);
    }

    [Fact]
    public void Layout_follows_the_number_and_shape_of_the_photos()
    {
        var one = Memory("2026-02-01", 1);
        var portraitTrio = new BookMemory(Guid.NewGuid(), new DateOnly(2026, 3, 1), DateTimeOffset.UnixEpoch, "Трое", null, null,
            false, 0, [new(Guid.NewGuid(), 4, 3), new(Guid.NewGuid(), 4, 3), new(Guid.NewGuid(), 3, 4)]);
        // 81 photos in one memory: twenty spreads of four, the last one left out.
        var many = Memory("2026-04-01", 81);

        Assert.Equal(SpreadLayout.Full, BookBuilder.Build([one]).Spreads[0].Layout);
        var trio = BookBuilder.Build(Enumerable.Repeat(0, 19).Select(i => Memory("2026-01-01", 1)).Append(portraitTrio).ToList())
            .Spreads.Single(s => s.MemoryId == portraitTrio.Id);
        Assert.Equal(SpreadLayout.Trio, trio.Layout);
        Assert.Equal(portraitTrio.Photos[2].Id, trio.Photos[0]);

        var big = BookBuilder.Build([many]);
        Assert.Equal(80, big.Spreads.Sum(s => s.Photos.Count));
        Assert.All(big.Spreads, s => Assert.Equal(SpreadLayout.Quad, s.Layout));
    }

    [Fact]
    public void A_place_already_in_the_title_is_not_printed_twice()
    {
        BookMemory At(string title, string place) => new(Guid.NewGuid(), new DateOnly(2026, 6, 1), DateTimeOffset.UnixEpoch, title,
            null, place, false, 0, [new(Guid.NewGuid(), 4, 3)]);
        Assert.Equal("Бурабай", BookBuilder.Build([At("Бурабай", "бурабай")]).Spreads[0].Title);
        Assert.Equal("Костёр · Бурабай", BookBuilder.Build([At("Костёр", "Бурабай")]).Spreads[0].Title);
    }

    [Fact]
    public void Every_layout_keeps_its_boxes_on_the_paper_and_off_the_fold_but_full()
    {
        foreach (var layout in Enum.GetValues<SpreadLayout>())
        foreach (var box in BookLayouts.Slots(layout))
        {
            Assert.True(box.X >= 0 && box.Y >= 0 && box.X + box.W <= 1 && box.Y + box.H <= 1);
            var crossesFold = box.X < 0.5 && box.X + box.W > 0.5;
            Assert.Equal(layout == SpreadLayout.Full, crossesFold);
        }
        Assert.True(BookLayouts.TryParse("photo_text", out var parsed));
        Assert.Equal(SpreadLayout.PhotoText, parsed);
        Assert.False(BookLayouts.TryParse("PhotoText", out _));
    }
}

public class PhotoBookTests
{
    private const string Url = "/v1/couple/photo-books";

    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.Coral);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }

    private static async Task<MemoryDto> Memory(HttpClient client, string title, string happenedOn, int photos, string? text = null)
    {
        var res = await client.PostAsJsonAsync("/v1/couple/memories", new { title, text, happenedOn });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var memory = (await res.Content.ReadFromJsonAsync<MemoryDto>())!;
        for (var i = 0; i < photos; i++)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(Jpeg(i % 2 == 0 ? 64 : 48, i % 2 == 0 ? 48 : 64));
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(file, "file", "photo.jpg");
            var upload = await client.PostAsync($"/v1/couple/memories/{memory.Id}/photos", form);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            memory = (await upload.Content.ReadFromJsonAsync<MemoryDto>())!;
        }
        return memory;
    }

    private static async Task<BookDto> Ok(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<BookDto>())!;
    }

    private static async Task<string?> Code(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<Dictionary<string, object>>())?.GetValueOrDefault("code")?.ToString();

    private static readonly object Delivery = new
    {
        recipientName = "Нурай", phone = "8 701 000 00 02", city = "Атырау", address = "ул. Абая 1, кв. 5"
    };

    // A year of 21 photos (a memory holds up to ten): the builder fills every box of the twenty spreads.
    private static async Task<(ApiFactory Api, HttpClient A, HttpClient B, BookDto Book)> FullBook(
        IReadOnlyDictionary<string, string>? settings = null)
    {
        var api = await ApiFactory.StartAsync(settings);
        var (a, b) = await api.PairAsync();
        await Memory(a, "Наурыз", "2026-03-22", 7);
        await Memory(a, "Лето на Алаколе", "2026-07-12", 7, "Вода тёплая, ветер сильный");
        await Memory(b, "Осень в Алматы", "2026-09-20", 7);
        return (api, a, b, await Ok(await a.PostAsync($"{Url}/2026", null)));
    }

    private static async Task<OrderDto> Order(HttpClient client)
    {
        var res = await client.PostAsJsonAsync($"{Url}/2026/orders", Delivery);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    private static async Task<int> Print(ApiFactory api)
    {
        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PhotoBookPrinting>().RunOnceAsync();
    }

    [Fact]
    public async Task The_book_is_laid_out_from_the_years_memories()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await Memory(a, "Первый снег", "2026-01-05", 2);
        var trip = await Memory(b, "Бурабай", "2026-06-20", 3, "Палатка, костёр, звёзды");
        await Memory(a, "Прошлый Новый год", "2025-12-31", 1);
        await Memory(a, "Без фото", "2026-03-01", 0);

        var years = (await a.GetFromJsonAsync<BookYears>(Url))!;
        Assert.Equal((9900, 20), (years.PriceKzt, years.Spreads));
        Assert.Equal([new BookYear(2026, 2, 5, false), new BookYear(2025, 1, 1, false)], years.Years);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"{Url}/2026")).StatusCode);
        Assert.Equal("no_photos", await Code(await a.PostAsync($"{Url}/2024", null)));

        var book = await Ok(await a.PostAsync($"{Url}/2026", null));
        Assert.Equal(("Алмаз & Нурай", "2026"), (book.Cover.Title, book.Cover.Subtitle));
        Assert.NotNull(book.Cover.Photo);
        Assert.Equal(20, book.Spreads.Count);
        // Five photos, one a spread, in the order they happened; fifteen empty spreads of two boxes.
        Assert.Equal(30, book.Empty);
        Assert.Equal("2026-01-05", book.Spreads[0].Date);
        Assert.Equal(trip.Id, book.Spreads[2].MemoryId);
        Assert.Equal("Палатка, костёр, звёзды", book.Spreads[2].Text);
        Assert.Equal("photo_text", book.Spreads[2].Layout);
        Assert.NotNull(book.Spreads[2].Story);
        Assert.Equal(5, book.Layouts.Count);
        Assert.False(book.Locked);

        // The partner sees the same book.
        Assert.Equal(book.Id, (await Ok(await b.GetAsync($"{Url}/2026"))).Id);
        Assert.True((await b.GetFromJsonAsync<BookYears>(Url))!.Years[0].HasBook);

        var photos = (await a.GetFromJsonAsync<YearPhotos>($"{Url}/2026/photos"))!;
        Assert.Equal(["Первый снег", "Бурабай"], photos.Memories.Select(m => m.Title));
        Assert.Equal(5, photos.Used.Count);
    }

    [Fact]
    public async Task Spreads_can_be_changed_and_moved_by_either_partner()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var winter = await Memory(a, "Каток", "2026-02-14", 2);
        var summer = await Memory(a, "Море", "2026-08-01", 2);
        await Ok(await a.PostAsync($"{Url}/2026", null));
        var ids = winter.Photos.Select(p => p.Id).Concat(summer.Photos.Select(p => p.Id)).ToArray();

        // Spread 0 starts as winter's first photo; B makes it a pair with a summer photo first.
        var edited = await Ok(await b.PutAsJsonAsync($"{Url}/2026/spreads/0",
            new { layout = "pair", photoIds = new Guid?[] { ids[2], null }, title = "Лучшее", text = "" }));
        var spread = edited.Spreads[0];
        Assert.Equal(("pair", "Лучшее", (string?)null), (spread.Layout, spread.Title, spread.Text));
        Assert.Equal(ids[2], spread.Slots[0].Photo!.Id);
        Assert.Null(spread.Slots[1].Photo);
        Assert.Equal((summer.Id, "2026-08-01"), (spread.MemoryId, spread.Date));

        Assert.Equal("invalid_photoIds", await Code(await a.PutAsJsonAsync($"{Url}/2026/spreads/0",
            new { layout = "trio", photoIds = new[] { ids[0] } })));
        Assert.Equal("invalid_photoIds", await Code(await a.PutAsJsonAsync($"{Url}/2026/spreads/0",
            new { layout = "pair", photoIds = new[] { ids[0], ids[0] } })));
        Assert.Equal("invalid_layout", await Code(await a.PutAsJsonAsync($"{Url}/2026/spreads/0",
            new { layout = "collage", photoIds = new[] { ids[0] } })));
        Assert.Equal(HttpStatusCode.NotFound, (await a.PutAsJsonAsync($"{Url}/2026/spreads/20",
            new { layout = "full", photoIds = new[] { ids[0] } })).StatusCode);

        // Another couple's photo can't go in.
        var other = await api.SignInAsync("+77010000003", "Ерлан");
        var otherPartner = await api.SignInAsync("+77010000004", "Дана");
        (await other.PostAsJsonAsync("/v1/couple/invitations", new { phone = "+77010000004" })).EnsureSuccessStatusCode();
        (await otherPartner.PostAsync($"/v1/couple/invitations/{api.Sms.LastCode("+77010000004")}/accept", null)).EnsureSuccessStatusCode();
        var foreign = await Memory(other, "Чужое", "2026-05-05", 1);
        Assert.Equal("invalid_photoIds", await Code(await a.PutAsJsonAsync($"{Url}/2026/spreads/1",
            new { layout = "full", photoIds = new[] { foreign.Photos[0].Id } })));
        Assert.Equal("invalid_coverPhotoId", await Code(await a.PatchAsJsonAsync($"{Url}/2026",
            new { coverPhotoId = foreign.Photos[0].Id })));

        var moved = await Ok(await a.PostAsJsonAsync($"{Url}/2026/spreads/0/move", new { to = 3 }));
        Assert.Equal("Лучшее", moved.Spreads[3].Title);
        Assert.Equal("Каток", moved.Spreads[0].Title);

        var cover = await Ok(await a.PatchAsJsonAsync($"{Url}/2026", new { title = "Мы", subtitle = "", coverPhotoId = ids[3] }));
        Assert.Equal(("Мы", (string?)null, ids[3]), (cover.Cover.Title, cover.Cover.Subtitle, cover.Cover.Photo!.Id));

        // A photo deleted with its memory leaves its box empty.
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/v1/couple/memories/{summer.Id}")).StatusCode);
        var after = await Ok(await a.GetAsync($"{Url}/2026"));
        Assert.Null(after.Cover.Photo);
        Assert.Null(after.Spreads[3].Slots[0].Photo);
    }

    [Fact]
    public async Task An_order_keeps_the_book_as_it_was_and_goes_to_print_once_Kaspi_says_paid()
    {
        var (api, a, b, book) = await FullBook();
        await using var owned = api;
        Assert.Equal(0, book.Empty);

        Assert.Equal("invalid_phone", await Code(await a.PostAsJsonAsync($"{Url}/2026/orders",
            new { recipientName = "Нурай", phone = "123", city = "Атырау", address = "Абая 1" })));
        var order = await Order(a);
        Assert.Equal(("awaiting_payment", 9900, "+77010000002", true), (order.Status, order.PriceKzt, order.Phone, order.Mine));
        Assert.StartsWith("/v1/payments/kaspi/fake/", order.PayUrl);

        // While it waits for payment the book can't change, and isn't ordered twice.
        var locked = await Ok(await b.GetAsync($"{Url}/2026"));
        Assert.True(locked.Locked);
        Assert.False(locked.Orders.Single().Mine);
        Assert.Equal("book_locked", await Code(await b.PatchAsJsonAsync($"{Url}/2026", new { title = "Другое" })));
        Assert.Equal("book_locked", await Code(await b.PostAsync($"{Url}/2026", null)));
        Assert.Equal("order_waiting", await Code(await b.PostAsJsonAsync($"{Url}/2026/orders", Delivery)));
        Assert.Equal(order.PayUrl, (await (await b.PostAsync($"{Url}/orders/{order.Id}/pay", null))
            .Content.ReadFromJsonAsync<OrderDto>())!.PayUrl);

        // Paying (the fake Kaspi page) unlocks the book; later edits don't touch the copy being printed.
        Assert.Equal(HttpStatusCode.OK, (await api.CreateClient().GetAsync(order.PayUrl)).StatusCode);
        Assert.Equal("paid", (await Ok(await a.GetAsync($"{Url}/2026"))).Orders.Single().Status);
        await Ok(await a.PatchAsJsonAsync($"{Url}/2026", new { title = "Новое название" }));
        Assert.Equal("order_paid", await Code(await a.DeleteAsync($"{Url}/orders/{order.Id}")));

        Assert.Equal(1, await Print(api));
        Assert.Equal(0, await Print(api));
        var job = Assert.Single(api.Printer.Jobs);
        Assert.Equal((order.Id, "Нурай", "Атырау"), (job.OrderId, job.RecipientName, job.City));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(job.Pdf, 0, 4));
        using (var scope = api.Services.CreateScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<IPhotoStore>().OpenAsync(job.PdfKey);
            Assert.NotNull(stored);
            await stored.DisposeAsync();
            var row = await scope.ServiceProvider.GetRequiredService<AiluDb>().PhotoBookOrders.SingleAsync();
            Assert.Equal("Алмаз & Нурай", BookContent.FromJson(row.Content).Title);
            Assert.Equal(PhotoBookOrderStatus.Printing, row.Status);
        }

        // The printer reports progress with its secret.
        var printer = api.CreateClient();
        var notice = new { jobId = $"job-{order.Id:N}", status = "Shipped", trackingNumber = "KZ123" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await printer.PostAsJsonAsync("/v1/print/notices", notice)).StatusCode);
        printer.DefaultRequestHeaders.Add("X-Print-Secret", "print-secret");
        Assert.Equal(HttpStatusCode.OK, (await printer.PostAsJsonAsync("/v1/print/notices", notice)).StatusCode);
        var shipped = (await Ok(await b.GetAsync($"{Url}/2026"))).Orders.Single();
        Assert.Equal(("shipped", "KZ123", (string?)null), (shipped.Status, shipped.TrackingNumber, shipped.PayUrl));
    }

    [Fact]
    public async Task Without_Kaspi_the_order_is_kept_unpaid_and_can_be_cancelled()
    {
        var (api, a, _, _) = await FullBook(new Dictionary<string, string> { ["PhotoBook:UseFakeKaspi"] = "false" });
        await using var owned = api;

        var order = await Order(a);
        Assert.Equal(("awaiting_payment", (string?)null), (order.Status, order.PayUrl));
        Assert.Null((await (await a.PostAsync($"{Url}/orders/{order.Id}/pay", null)).Content.ReadFromJsonAsync<OrderDto>())!.PayUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().PostAsJsonAsync("/v1/payments/kaspi",
            new { paymentId = "x", paid = true })).StatusCode);

        var cancelled = await a.DeleteAsync($"{Url}/orders/{order.Id}");
        Assert.Equal("cancelled", (await cancelled.Content.ReadFromJsonAsync<OrderDto>())!.Status);
        var book = await Ok(await a.GetAsync($"{Url}/2026"));
        Assert.False(book.Locked);
        Assert.Empty(book.Orders);
        Assert.Equal(0, await Print(api));
    }

    [Fact]
    public async Task A_failed_payment_lets_the_couple_pay_again_and_a_book_with_gaps_cant_be_ordered()
    {
        var (api, a, _, book) = await FullBook();
        await using var owned = api;

        var order = await Order(a);
        var kaspi = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await kaspi.PostAsJsonAsync("/v1/payments/kaspi",
            new { paymentId = $"fake-{order.Id:N}", paid = false })).StatusCode);
        using (var scope = api.Services.CreateScope())
            Assert.Null((await scope.ServiceProvider.GetRequiredService<AiluDb>().PhotoBookOrders.SingleAsync()).PayUrl);
        var again = (await (await a.PostAsync($"{Url}/orders/{order.Id}/pay", null)).Content.ReadFromJsonAsync<OrderDto>())!;
        Assert.NotNull(again.PayUrl);
        await kaspi.PostAsJsonAsync("/v1/payments/kaspi", new { paymentId = $"fake-{order.Id:N}", paid = true });
        Assert.Equal("paid", (await Ok(await a.GetAsync($"{Url}/2026"))).Orders.Single().Status);

        await Ok(await a.PutAsJsonAsync($"{Url}/2026/spreads/5", new
        {
            layout = "pair", photoIds = new Guid?[] { book.Spreads[5].Slots[0].Photo!.Id, null }
        }));
        Assert.Equal("book_incomplete", await Code(await a.PostAsJsonAsync($"{Url}/2026/orders", Delivery)));
    }

    [Fact]
    public async Task The_preview_is_a_signed_pdf_of_the_cover_and_twenty_spreads()
    {
        var (api, a, _, book) = await FullBook();
        await using var owned = api;
        var anonymous = api.CreateClient();

        var res = await anonymous.GetAsync(book.PreviewUrl);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/pdf", res.Content.Headers.ContentType?.MediaType);
        var pdf = Encoding.Latin1.GetString(await res.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("%PDF", pdf);
        Assert.Equal(21, System.Text.RegularExpressions.Regex.Count(pdf, @"/Type\s*/Page\b"));

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(book.PreviewUrl.Replace("sig=", "sig=x"))).StatusCode);
        api.Clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(book.PreviewUrl)).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_account_cancels_an_unpaid_order_and_forgets_the_address()
    {
        var (api, a, b, _) = await FullBook();
        await using var owned = api;
        await Order(b);

        Assert.Equal(HttpStatusCode.NoContent, (await b.DeleteAsync("/v1/me")).StatusCode);

        using var scope = api.Services.CreateScope();
        var order = await scope.ServiceProvider.GetRequiredService<AiluDb>().PhotoBookOrders.SingleAsync();
        Assert.Equal(PhotoBookOrderStatus.Cancelled, order.Status);
        Assert.Equal((null, null, null), (order.RecipientName, order.Phone, order.Address));
    }
}
