namespace Ailu.Api.Domain;

// A rectangle as fractions of a spread (two facing pages, twice as wide as tall) or of the cover page.
public readonly record struct Box(double X, double Y, double W, double H);

public enum SpreadLayout { Full, PhotoText, Pair, Trio, Quad }

// The book's page designs. The server draws the PDF from these boxes and sends the same boxes to the app,
// so what the couple arranges on the phone is what gets printed. Photos are cropped to fill their box.
public static class BookLayouts
{
    // Print size: 20 × 20 cm pages, so a spread is 40 × 20 cm. Twenty spreads, as the roadmap promises.
    public const int Spreads = 20;
    public const double PageCm = 20;

    // Photos keep a white margin round the page and a band at the bottom of the left page for the caption.
    private const double Left = 0.03, Right = 0.53, PageW = 0.44, Top = 0.06, Tall = 0.82, Half = 0.395, Lower = 0.485;

    public static readonly Box Caption = new(Left, 0.90, PageW, 0.06);
    public static readonly Box StoryText = new(0.07, 0.14, 0.36, 0.68);

    public static IReadOnlyList<Box> Slots(SpreadLayout layout) => layout switch
    {
        SpreadLayout.Full => [new(Left, Top, 0.94, Tall)],
        SpreadLayout.PhotoText => [new(Right, Top, PageW, Tall)],
        SpreadLayout.Pair => [new(Left, Top, PageW, Tall), new(Right, Top, PageW, Tall)],
        SpreadLayout.Trio => [new(Left, Top, PageW, Tall), new(Right, Top, PageW, Half), new(Right, Lower, PageW, Half)],
        SpreadLayout.Quad =>
        [
            new(Left, Top, PageW, Half), new(Left, Lower, PageW, Half),
            new(Right, Top, PageW, Half), new(Right, Lower, PageW, Half)
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(layout))
    };

    // The story layout gives the left page to the memory's words; the others carry one caption line.
    public static bool HasStory(SpreadLayout layout) => layout == SpreadLayout.PhotoText;

    // The cover is one page: a photo, then the title and the subtitle under it.
    public static readonly Box CoverPhoto = new(0.08, 0.08, 0.84, 0.66);
    public static readonly Box CoverTitle = new(0.08, 0.78, 0.84, 0.08);
    public static readonly Box CoverSubtitle = new(0.08, 0.87, 0.84, 0.05);

    public static string Name(SpreadLayout layout) => layout switch
    {
        SpreadLayout.PhotoText => "photo_text",
        var other => other.ToString().ToLowerInvariant()
    };

    public static bool TryParse(string? value, out SpreadLayout layout)
    {
        foreach (var candidate in Enum.GetValues<SpreadLayout>())
            if (Name(candidate) == value)
            {
                layout = candidate;
                return true;
            }
        layout = default;
        return false;
    }
}

// What the builder knows about one memory of the year.
public sealed record BookMemory(Guid Id, DateOnly HappenedOn, DateTimeOffset CreatedAt, string Title, string? Text,
    string? Place, bool Favorite, int Reactions, IReadOnlyList<BookPhoto> Photos);

public sealed record BookPhoto(Guid Id, int Width, int Height)
{
    public bool Landscape => Width > Height;
}

public sealed record BuiltSpread(SpreadLayout Layout, IReadOnlyList<Guid> Photos, Guid? MemoryId, string? Title, string? Text,
    DateOnly? Date);

public sealed record BuiltBook(Guid? CoverPhotoId, IReadOnlyList<BuiltSpread> Spreads);

// Lays out a year of memories as twenty spreads, in the order they happened (roadmap: "the app builds the
// book from the year itself, the couple edits and orders"). One memory is one moment, so it gets a spread
// of its own and its photos stay together:
// - more than twenty memories: every month that has one keeps its best, the rest go by score (a favorite,
//   the partners' hearts, then how many photos it has);
// - fewer: memories with many photos get more spreads, up to one per photo;
// - fewer than twenty photos in all: the spreads at the end stay empty for the couple to fill.
// A spread holds up to four photos. A lone photo gets the story layout (photo and words) unless it is a
// landscape shot with no words, which takes the whole spread.
public static class BookBuilder
{
    private const int MaxPerSpread = 4;

    public static BuiltBook Build(IReadOnlyList<BookMemory> memories)
    {
        var withPhotos = memories.Where(m => m.Photos.Count > 0).ToList();
        var chosen = Choose(withPhotos);

        var spreadsFor = chosen.ToDictionary(m => m.Id, _ => 1);
        while (spreadsFor.Values.Sum() < BookLayouts.Spreads)
        {
            // The memory whose photos are most crowded gets one more spread, while any memory has a photo to spare.
            var next = chosen.Where(m => m.Photos.Count > spreadsFor[m.Id])
                .OrderByDescending(m => (double)m.Photos.Count / spreadsFor[m.Id]).ThenBy(m => m.HappenedOn)
                .FirstOrDefault();
            if (next is null) break;
            spreadsFor[next.Id]++;
        }

        var spreads = new List<BuiltSpread>();
        foreach (var memory in chosen)
        {
            var count = spreadsFor[memory.Id];
            var photos = memory.Photos.Take(count * MaxPerSpread).ToList();
            var start = 0;
            for (var i = 0; i < count; i++)
            {
                // Spread the photos evenly: 7 photos over 2 spreads is 4 and 3.
                var size = (photos.Count - start) / (count - i) + ((photos.Count - start) % (count - i) > 0 ? 1 : 0);
                var part = photos.Skip(start).Take(size).ToList();
                start += size;
                // The memory's words go with its first spread only.
                spreads.Add(Spread(memory, part, i == 0 ? memory.Text : null));
            }
        }
        while (spreads.Count < BookLayouts.Spreads) spreads.Add(Empty);

        var cover = withPhotos.OrderByDescending(Score).ThenBy(m => m.HappenedOn).FirstOrDefault()?.Photos[0].Id;
        return new BuiltBook(cover, spreads);
    }

    public static readonly BuiltSpread Empty = new(SpreadLayout.Pair, [Guid.Empty, Guid.Empty], null, null, null, null);

    private static List<BookMemory> Choose(List<BookMemory> memories)
    {
        if (memories.Count <= BookLayouts.Spreads) return Chronological(memories);

        var best = memories.GroupBy(m => m.HappenedOn.Month)
            .Select(g => g.OrderByDescending(Score).ThenBy(m => m.HappenedOn).First()).ToList();
        var rest = memories.Except(best).OrderByDescending(Score).ThenBy(m => m.HappenedOn)
            .Take(BookLayouts.Spreads - best.Count);
        return Chronological(best.Concat(rest));
    }

    private static List<BookMemory> Chronological(IEnumerable<BookMemory> memories) =>
        memories.OrderBy(m => m.HappenedOn).ThenBy(m => m.CreatedAt).ThenBy(m => m.Id).ToList();

    private static double Score(BookMemory m) =>
        (m.Favorite ? 3 : 0) + Math.Min(m.Reactions, 2) + Math.Min(m.Photos.Count, MaxPerSpread) * 0.25;

    private static BuiltSpread Spread(BookMemory memory, List<BookPhoto> photos, string? text)
    {
        var layout = photos.Count switch
        {
            1 when photos[0].Landscape && string.IsNullOrEmpty(text) => SpreadLayout.Full,
            1 => SpreadLayout.PhotoText,
            2 => SpreadLayout.Pair,
            3 => SpreadLayout.Trio,
            _ => SpreadLayout.Quad
        };
        // A trio's big box is a whole page: put a portrait photo there if the memory has one.
        if (layout == SpreadLayout.Trio && photos.FindIndex(p => !p.Landscape) is > 0 and var portrait)
            photos = [photos[portrait], .. photos.Where((_, i) => i != portrait)];
        var title = memory.Place is null ? memory.Title : $"{memory.Title} · {memory.Place}";
        return new BuiltSpread(layout, photos.Select(p => p.Id).ToList(), memory.Id, title, text, memory.HappenedOn);
    }
}
