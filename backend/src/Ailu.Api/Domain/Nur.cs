namespace Ailu.Api.Domain;

// Amounts from the gamification design. The client never sends an amount; it only reports
// what happened, and the server decides what that is worth.
public static class Nur
{
    public const int TaskCompleted = 50;
    public const int QuestionAnsweredByBoth = 30;
    public const int MemoryAdded = 20;
    public const int DateHappened = 150;
    public const int GoalStepClosed = 100;
    public const int AchievementEarned = 80;
    public const int MoodByBoth = 10;
    public const int LoveQuizByBoth = 120;
}
