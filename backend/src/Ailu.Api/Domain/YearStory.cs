namespace Ailu.Api.Domain;

public static class StorySlideKind
{
    public const string Intro = "intro";
    public const string Memory = "memory";
    public const string Start = "start";
    public const string DaysTogether = "days_together";
    public const string Anniversary = "anniversary";
    public const string GoalDone = "goal_done";
    public const string Achievement = "achievement";
    public const string Outro = "outro";
}

// One slide of the film. Like the timeline, the server sends kinds and numbers and whoever shows the slide
// (the app's preview, the renderer) writes the words; titles and places are the couple's own text.
public sealed record StorySlide(string Kind, int DurationMs, DateOnly? Date = null, Guid? MemoryId = null, Guid? PhotoId = null,
    string? Title = null, string? Place = null, int? Days = null, int? Years = null, string? Icon = null, string? Slug = null,
    StoryStats? Stats = null);

public sealed record StoryStats(int DaysTogether, int Memories, int Tasks, int Nur);

// A memory that could go in the film: one with at least one photo, from the year.
public sealed record StoryMemory(Guid Id, DateOnly Date, string Title, string? Place, Guid Cover, int Photos, int Reactions,
    bool Favorite, bool HasText);

// A dated moment of the year without a photo of its own: the start, a milestone, a goal, an achievement.
public sealed record StoryDate(string Kind, DateOnly Date, string Id, int? Days = null, int? Years = null, string? Title = null,
    string? Icon = null, string? Slug = null);

// Picks what goes into the one-minute "story of the year" (roadmap phase 4, P1). The film is an intro,
// the year's best photo memories and its key dates in the order they happened, then the year in numbers.
public static class YearStoryPlan
{
    public const int TotalMs = 60_000;
    public const int IntroMs = 4_000;
    public const int OutroMs = 5_000;
    public const int DateMs = 3_000;
    public const int MemoryMs = 4_000;
    public const int MaxMemoryMs = 6_000;
    public const int MaxDates = 4;

    // Fewer photo memories than this make a slideshow, not a story; the app asks for a few more.
    public const int MinMemories = 5;

    // Dates the couple will care about most come first when there are more than fit.
    private static readonly string[] DatePriority =
        [StorySlideKind.Anniversary, StorySlideKind.Start, StorySlideKind.DaysTogether, StorySlideKind.GoalDone, StorySlideKind.Achievement];

    public static List<StorySlide> Build(int year, IReadOnlyList<StoryMemory> memories, IReadOnlyList<StoryDate> dates, StoryStats stats)
    {
        var keyDates = dates
            .OrderBy(d => Array.IndexOf(DatePriority, d.Kind)).ThenBy(d => d.Date).ThenBy(d => d.Id, StringComparer.Ordinal)
            .Take(MaxDates).ToList();

        var room = TotalMs - IntroMs - OutroMs - keyDates.Count * DateMs;
        var chosen = Pick(memories, room / MemoryMs, keyDates.Select(d => d.Date).ToHashSet());

        // The minute is shared out between the photos, so the film runs a full minute when there are enough
        // of them, and lingers on each photo (up to a point) when there are few.
        var perMemory = chosen.Count == 0 ? 0 : Math.Min(MaxMemoryMs, room / chosen.Count);
        var spare = chosen.Count == 0 ? 0 : Math.Max(0, room - perMemory * chosen.Count);

        var middle = chosen.Select(m => (m.Date, Order: 1, Key: m.Id.ToString("N"), Slide: new StorySlide(StorySlideKind.Memory,
                perMemory, m.Date, m.Id, m.Cover, m.Title, m.Place)))
            .Concat(keyDates.Select(d => (d.Date, Order: 0, Key: d.Id, Slide: new StorySlide(d.Kind, DateMs, d.Date,
                Title: d.Title, Days: d.Days, Years: d.Years, Icon: d.Icon, Slug: d.Slug))))
            .OrderBy(x => x.Date).ThenBy(x => x.Order).ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Slide);

        // Rounding leftovers go to the ending, so the film still adds up to a minute.
        var outroMs = OutroMs + (perMemory == MaxMemoryMs ? 0 : spare);
        return [new StorySlide(StorySlideKind.Intro, IntroMs, Years: year), .. middle,
            new StorySlide(StorySlideKind.Outro, outroMs, Stats: stats)];
    }

    public static int DurationMs(IEnumerable<StorySlide> slides) => slides.Sum(s => s.DurationMs);

    // Favourites first, then a round over the months: the best memory of each month, then the second best,
    // and so on, so one busy holiday doesn't fill the whole film.
    private static List<StoryMemory> Pick(IReadOnlyList<StoryMemory> memories, int slots, HashSet<DateOnly> keyDays)
    {
        if (slots <= 0) return [];
        double Score(StoryMemory m) =>
            1 + Math.Min(m.Reactions, 2) + (m.HasText ? 1 : 0) + (m.Place is null ? 0 : 1)
            + Math.Min(m.Photos - 1, 3) * 0.5 + (keyDays.Contains(m.Date) ? 2 : 0);
        IOrderedEnumerable<StoryMemory> Best(IEnumerable<StoryMemory> ms) =>
            ms.OrderByDescending(Score).ThenBy(m => m.Date).ThenBy(m => m.Id);

        var chosen = Best(memories.Where(m => m.Favorite)).Take(slots).ToList();
        var rest = memories.Where(m => !m.Favorite).GroupBy(m => m.Date.Month)
            .Select(g => new Queue<StoryMemory>(Best(g))).ToList();
        while (chosen.Count < slots && rest.Any(q => q.Count > 0))
        {
            // Within a round, stronger memories go first, in case the round doesn't finish.
            var round = rest.Where(q => q.Count > 0).Select(q => q.Dequeue()).ToList();
            chosen.AddRange(Best(round).Take(slots - chosen.Count));
        }
        return chosen;
    }
}
