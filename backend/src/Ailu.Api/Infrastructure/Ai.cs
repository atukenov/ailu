using System.Text.Json;
using Ailu.Api.Data;
using Ailu.Api.Services;
using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace Ailu.Api.Infrastructure;

// What the model is told about a couple. Nothing in it names or identifies either person: no names,
// phones, answers, memories or moods leave the server, only the couple's leanings and the library
// tasks they did (privacy doc: personal data stays in Kazakhstan).
public sealed record CoupleProfile(
    DateOnly Today,
    int DaysTogether,
    // Each partner's strongest love languages from the latest quiz, or empty.
    IReadOnlyList<string> LanguagesA,
    IReadOnlyList<string> LanguagesB,
    // Titles of library tasks they finished lately, newest first, in Russian.
    IReadOnlyList<string> RecentTasks,
    // Categories of the goals they work on together.
    IReadOnlyList<string> GoalCategories);

public sealed record IdeaRequest(AiPickKind Kind, AiBudget Budget, string? Note, CoupleProfile Couple, IReadOnlyList<string> Avoid);

public sealed record AiIdea(string Title, string Body, string TitleKk, string BodyKk, int CostKzt, int DurationMin);

public sealed record TaskRequest(CoupleProfile Couple, IReadOnlyList<string> Categories, IReadOnlyList<string> Avoid);

public sealed record AiTask(string Category, string Title, string Body, string TitleKk, string BodyKk, int CostKzt, int DurationMin);

// Writes ideas and personal tasks. ClaudeAiWriter asks the model; StubAiWriter picks from a small
// built-in set and is used until an API key is configured (Ai:ApiKey), and in tests.
public interface IAiWriter
{
    Task<IReadOnlyList<AiIdea>> IdeasAsync(IdeaRequest request, CancellationToken ct = default);

    Task<AiTask> TaskAsync(TaskRequest request, CancellationToken ct = default);
}

public sealed class AiUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class AiOptions
{
    // An Anthropic API key. Empty means the built-in stub writes the ideas.
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-opus-5-5";

    // Short creative texts need little deliberation; "low" keeps each answer quick and cheap.
    public string Effort { get; set; } = "low";
    public int TimeoutSeconds { get; set; } = 40;

    public bool Configured => ApiKey != "";
}

public sealed class ClaudeAiWriter(AiOptions options, ILogger<ClaudeAiWriter> log) : IAiWriter
{
    // Refusals are re-served by a fallback model the API picks for the refusal's category.
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    private readonly AnthropicClient client = new()
    {
        ApiKey = options.ApiKey,
        Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
        MaxRetries = 1
    };

    public async Task<IReadOnlyList<AiIdea>> IdeasAsync(IdeaRequest request, CancellationToken ct = default)
    {
        var json = await AskAsync(AiPrompts.IdeasSystem, AiPrompts.IdeasUser(request), AiPrompts.IdeasSchema, ct);
        return AiPrompts.ParseIdeas(json);
    }

    public async Task<AiTask> TaskAsync(TaskRequest request, CancellationToken ct = default)
    {
        var json = await AskAsync(AiPrompts.TaskSystem, AiPrompts.TaskUser(request), AiPrompts.TaskSchema(request.Categories), ct);
        return AiPrompts.ParseTask(json, request.Categories) ?? throw new AiUnavailableException("The model wrote no usable task.");
    }

    public static MessageCreateParams Request(AiOptions options, string system, string user, Dictionary<string, JsonElement> schema) => new()
    {
        Model = options.Model,
        MaxTokens = 8000,
        Betas = [FallbackBeta],
        Fallbacks = new Default(),
        System = system,
        OutputConfig = new BetaOutputConfig
        {
            Effort = options.Effort,
            Format = new BetaJsonOutputFormat { Schema = schema }
        },
        Messages = [new() { Role = Role.User, Content = user }]
    };

    private async Task<JsonElement> AskAsync(string system, string user, Dictionary<string, JsonElement> schema, CancellationToken ct)
    {
        BetaMessage response;
        try
        {
            response = await client.Beta.Messages.Create(Request(options, system, user, schema), ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // API errors, timeouts and network failures alike: the caller falls back or says "try later".
            log.LogWarning(e, "The model call failed");
            throw new AiUnavailableException("The model did not answer.", e);
        }

        if (response.StopReason != "end_turn")
        {
            log.LogWarning("The model stopped with {Reason}", response.StopReason?.ToString());
            throw new AiUnavailableException($"The model stopped with {response.StopReason}.");
        }
        var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException e)
        {
            throw new AiUnavailableException("The model wrote something that isn't JSON.", e);
        }
    }
}
