using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Features.Daily;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ailu.Api.Tests;

public class ExtraTaskTests
{
    [Fact]
    public async Task Free_couples_get_one_task_a_day()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        var today = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        Assert.Equal(1, today.TasksPerDay);
        Assert.Empty(today.MoreTasks);

        await Finish(a, b, today.Task.Id);
        Assert.Equal("task_limit", await CodeOf(await a.PostAsync("/v1/couple/tasks/more", null)));
    }

    [Fact]
    public async Task Plus_couples_get_two_more_tasks_after_the_task_of_the_day()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, b) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Plus);

        var today = (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        Assert.Equal(3, today.TasksPerDay);
        // The next one opens only once the current task is done.
        Assert.Equal("task_not_done", await CodeOf(await a.PostAsync("/v1/couple/tasks/more", null)));
        await Finish(a, b, today.Task.Id);

        var second = await More(a);
        Assert.Equal(TaskState.Offered, second.State);
        // The partner asking at the same time gets the same task, not a third one.
        Assert.Equal(second.Id, (await More(b)).Id);
        await Finish(a, b, second.Id);

        var third = await More(b);
        await Finish(a, b, third.Id);
        Assert.Equal("task_limit", await CodeOf(await a.PostAsync("/v1/couple/tasks/more", null)));

        today = (await b.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!;
        Assert.Equal([second.Id, third.Id], today.MoreTasks.Select(t => t.Id));
        Assert.Equal(3, new[] { today.Task.Slug, second.Slug, third.Slug }.Distinct().Count());
        Assert.All(today.MoreTasks, t => Assert.Equal(TaskState.Done, t.State));

        // Нұр for tasks stays at one a day.
        Assert.Equal(Nur.TaskCompleted, today.Phase.Earned);

        // Tomorrow starts again from a single task.
        api.Clock.Advance(TimeSpan.FromDays(1));
        a = await api.SignInAsync("+77010000001");
        Assert.Empty((await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.MoreTasks);
    }

    [Fact]
    public async Task Premium_couples_get_three_tasks_too()
    {
        await using var api = await ApiFactory.StartAsync();
        var (a, _) = await api.PairAsync();
        await SetPlan(api, CouplePlan.Premium);
        Assert.Equal(3, (await a.GetFromJsonAsync<TodayDto>("/v1/couple/today"))!.TasksPerDay);
    }

    private static async Task SetPlan(ApiFactory api, CouplePlan plan)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AiluDb>().Couples
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Plan, plan));
    }

    private static async Task Finish(HttpClient a, HttpClient b, Guid id)
    {
        foreach (var (c, step) in new[] { (a, "accept"), (b, "accept"), (a, "complete"), (b, "complete") })
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/v1/couple/tasks/{id}/{step}", null)).StatusCode);
    }

    private static async Task<TaskDto> More(HttpClient c)
    {
        var res = await c.PostAsync("/v1/couple/tasks/more", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<TaskDto>())!;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
}
