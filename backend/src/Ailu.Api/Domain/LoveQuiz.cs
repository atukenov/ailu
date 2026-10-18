namespace Ailu.Api.Domain;

// The love-languages quiz (gamification doc): twelve questions, each a choice between four of the
// five languages. The texts live in the app, in both languages; the server only knows which language
// each option stands for, so it can score answers without trusting the app's arithmetic.
//
// The result tunes which tasks the couple is offered; it is not a diagnosis of either person.
public static class LoveQuiz
{
    public const string Words = "words";
    public const string Time = "time";
    public const string Gifts = "gifts";
    public const string Service = "service";
    public const string Touch = "touch";

    public static readonly string[] Languages = [Words, Time, Gifts, Service, Touch];

    // Question n (1-based) leaves out one language in turn, and the options are rotated so no language
    // sits in the same place every time. Each language is offered nine or ten times.
    public static readonly IReadOnlyList<string[]> Questions = Build();

    private static IReadOnlyList<string[]> Build()
    {
        // Matches the order the app's texts were written for; question 4 is the mockup's "обычный вторник".
        string[] leftOut = [Words, Time, Gifts, Touch, Service, Words, Time, Gifts, Touch, Service, Words, Time];
        return leftOut.Select((skip, i) =>
        {
            var offered = Languages.Where(l => l != skip).ToArray();
            var shift = i % offered.Length;
            return offered.Skip(shift).Concat(offered.Take(shift)).ToArray();
        }).ToList();
    }

    // One pick per question, in order, each among that question's options.
    public static bool Valid(IReadOnlyList<string>? picks) =>
        picks is not null && picks.Count == Questions.Count &&
        picks.Select((p, i) => Questions[i].Contains(p)).All(ok => ok);

    // 0–100 per language: how often it was picked out of the times it was offered.
    public static Dictionary<string, int> Scores(IReadOnlyList<string> picks)
    {
        var offered = Languages.ToDictionary(l => l, l => Questions.Count(q => q.Contains(l)));
        return Languages.ToDictionary(l => l,
            l => (int)Math.Round(100.0 * picks.Count(p => p == l) / offered[l], MidpointRounding.AwayFromZero));
    }

    // The highest score, all of them on a tie.
    public static string[] Top(IReadOnlyDictionary<string, int> scores)
    {
        var max = scores.Values.Max();
        return Languages.Where(l => scores[l] == max).ToArray();
    }

    // Task categories a language favours: "паре с «временем вместе» чаще предлагаются выходы, паре с
    // «подарками» — простые радости" (gamification doc). The library has no service or touch tasks yet.
    public static IEnumerable<string> TaskCategories(string language) => language switch
    {
        Time => ["time-together", "dates"],
        Words => ["words"],
        Gifts => ["simple-joys"],
        _ => []
    };

    // Нұр for the quiz is once a calendar quarter (1 / квартал); the quarter's first day keys the award.
    public static DateOnly QuarterStart(DateOnly day) => new(day.Year, (day.Month - 1) / 3 * 3 + 1, 1);
}
