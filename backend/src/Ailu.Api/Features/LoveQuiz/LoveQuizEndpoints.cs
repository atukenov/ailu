using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Quiz = Ailu.Api.Domain.LoveQuiz;

namespace Ailu.Api.Features.LoveQuiz;

public sealed record LoveQuizAnswersRequest(string[]? Answers);

// Options are language codes in the order the app shows them; the app holds the texts.
public sealed record LoveQuizQuestionDto(int Id, IReadOnlyList<string> Options);
public sealed record LoveQuizScoresDto(IReadOnlyDictionary<string, int> Scores, IReadOnlyList<string> Top);

// The round waiting for answers. The partner's picks never leave the server before both have answered;
// only the fact that they did.
public sealed record LoveQuizRoundDto(bool MyAnswered, bool PartnerAnswered);

// Partner is null when their side is gone (a deleted account).
public sealed record LoveQuizResultDto(DateTimeOffset CompletedAt, LoveQuizScoresDto? Me, LoveQuizScoresDto? Partner, int NurAwarded);

public sealed record LoveQuizDto(bool Available, int Nur, IReadOnlyList<LoveQuizQuestionDto> Questions,
    LoveQuizRoundDto? Round, LoveQuizResultDto? Result);

public static class LoveQuizEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/couple/love-quiz").WithTags("LoveQuiz").RequireAuthorization();
        g.MapGet("", Get);
        g.MapPost("/answers", Answer);
    }

    private static async Task<IResult> Get(HttpContext http, AiluDb db, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        return Results.Ok(await Load(scope, db, ct));
    }

    // All twelve picks at once, when the person finishes. They join the round waiting for answers, or
    // start a new one; the partner's answers complete it and earn the couple нұр once a quarter.
    private static async Task<IResult> Answer(LoveQuizAnswersRequest body, HttpContext http, AiluDb db,
        ActivityService activity, NurLedgerService nur, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        if (!Plans.LoveQuiz(scope.Couple.Plan))
            return Errors.Forbidden("quiz_plan", "Квиз о языках любви открыт в Plus и Premium.");
        if (!Quiz.Valid(body.Answers))
            return Errors.Validation("answers", "Ответьте на все вопросы квиза.");

        var now = clock.GetUtcNow();
        var today = CoupleCalendar.Today(scope.Couple, clock);

        // The couple row is locked so both partners finishing at once land in the same round.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Couples\" WHERE \"Id\" = {scope.Couple.Id} FOR UPDATE", ct);

        var round = await db.LoveQuizRounds.Include(r => r.Answers)
            .SingleOrDefaultAsync(r => r.CoupleId == scope.Couple.Id && r.CompletedAt == null, ct);
        if (round is null)
        {
            round = new LoveQuizRound { Id = Guid.NewGuid(), CoupleId = scope.Couple.Id, StartedAt = now };
            db.LoveQuizRounds.Add(round);
        }
        else if (round.Answers.Any(a => a.UserId == scope.UserId))
            return Errors.Conflict("quiz_answered", "Вы уже прошли квиз. Результат откроется, когда его пройдёт партнёр.");

        round.Answers.Add(new LoveQuizAnswer
        {
            RoundId = round.Id, UserId = scope.UserId, Picks = body.Answers!, SubmittedAt = now
        });

        if (scope.Partner is not null && round.Answers.Any(a => a.UserId == scope.Partner.UserId))
        {
            round.CompletedAt = now;
            var quarter = Quiz.QuarterStart(today);
            if (await nur.AwardAsync(scope.Couple, NurSource.LoveQuiz, quarter.ToString("yyyyMM"), Nur.LoveQuizByBoth, quarter, ct))
                round.NurAwarded = Nur.LoveQuizByBoth;
        }

        await activity.RecordAsync(scope.Couple, scope.UserId, today, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Only a racing request for the same person gets here: the lock serialises the rest.
            return Errors.Conflict("quiz_answered", "Вы уже прошли квиз. Результат откроется, когда его пройдёт партнёр.");
        }
        return Results.Ok(await Load(scope, db, ct));
    }

    private static async Task<LoveQuizDto> Load(CoupleScope scope, AiluDb db, CancellationToken ct)
    {
        var rounds = db.LoveQuizRounds.AsNoTracking().Include(r => r.Answers).Where(r => r.CoupleId == scope.Couple.Id);
        var open = await rounds.SingleOrDefaultAsync(r => r.CompletedAt == null, ct);
        var done = await rounds.Where(r => r.CompletedAt != null).OrderByDescending(r => r.CompletedAt).FirstOrDefaultAsync(ct);

        var questions = Quiz.Questions.Select((options, i) => new LoveQuizQuestionDto(i + 1, options)).ToList();
        var round = open is null ? null : new LoveQuizRoundDto(
            open.Answers.Any(a => a.UserId == scope.UserId),
            open.Answers.Any(a => a.UserId != scope.UserId));
        var result = done is null ? null : new LoveQuizResultDto(done.CompletedAt!.Value,
            ScoresOf(done.Answers.SingleOrDefault(a => a.UserId == scope.UserId)),
            ScoresOf(done.Answers.SingleOrDefault(a => a.UserId != scope.UserId)),
            done.NurAwarded);
        return new LoveQuizDto(Plans.LoveQuiz(scope.Couple.Plan), Nur.LoveQuizByBoth, questions, round, result);
    }

    private static LoveQuizScoresDto? ScoresOf(LoveQuizAnswer? answer)
    {
        if (answer is null) return null;
        var scores = Quiz.Scores(answer.Picks);
        return new LoveQuizScoresDto(scores, Quiz.Top(scores));
    }

}
