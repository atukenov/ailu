namespace Ailu.Api.Domain;

public sealed record Phase(int Index, string Slug, string Name, string Moon, int Threshold);

public sealed record PhaseProgress(int Cycle, Phase Phase, int Earned, int PhaseStart, int? NextAt, Phase? Next)
{
    // Share of the current phase already filled, 0..1. The moon on the home screen fills by this.
    public double Fill => NextAt is int next ? (double)(Earned - PhaseStart) / (next - PhaseStart) : 1.0;
}

// Six phases of one lunar cycle. After the supermoon the couple starts cycle II with the same names.
public static class Phases
{
    public static readonly Phase[] All =
    [
        new(0, "new-moon", "Знакомство", "Жаңа ай · новолуние", 0),
        new(1, "waxing-crescent", "Первые искры", "Молодой месяц", 500),
        new(2, "first-quarter", "Свой ритм", "Первая четверть", 1_500),
        new(3, "waxing-gibbous", "Мы двое", "Растущая луна", 3_000),
        new(4, "full-moon", "Родственные души", "Толық ай · полнолуние", 4_500),
        new(5, "supermoon", "Неразлучные", "Суперлуние", 7_000)
    ];

    // The design names cycle II but not its thresholds. Until it does, each cycle is 10 000 нұр
    // long and repeats the same ladder, so the supermoon of cycle I lasts 3 000 нұр like a phase should.
    public const int CycleLength = 10_000;

    // Earned нұр never decreases, so the phase never drops either.
    public static PhaseProgress For(int earned)
    {
        if (earned < 0) earned = 0;
        var cycle = earned / CycleLength;
        var within = earned % CycleLength;
        var offset = cycle * CycleLength;

        var phase = All.Last(p => p.Threshold <= within);
        var next = phase.Index + 1 < All.Length ? All[phase.Index + 1] : All[0];
        var nextAt = phase.Index + 1 < All.Length ? offset + next.Threshold : offset + CycleLength;

        return new PhaseProgress(cycle + 1, phase, earned, offset + phase.Threshold, nextAt, next);
    }
}
