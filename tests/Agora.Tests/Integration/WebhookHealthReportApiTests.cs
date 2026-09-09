using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Agora.Tests.Integration;

public class WebhookHealthReportApiTests
{
    private const string Path = "/api/admin/reports/webhook-health";
    private static string Date(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));
    private static string Window(DateTimeOffset from, DateTimeOffset to) => $"from={Date(from)}&to={Date(to)}";
    private static WebhookSubscription Subscription(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), Url = "https://example.test/hook", Secret = "NEVER-RETURN-SECRET", Events = [WebhookEvents.OrderPaid],
    };
    private static WebhookDelivery Delivery(Guid subscription, DateTimeOffset created, int attempts, bool success, DateTimeOffset attempted)
    {
        var delivery = new WebhookDelivery
        {
            SubscriptionId = subscription, CreatedAt = created, EventType = WebhookEvents.OrderPaid,
            Payload = "NEVER-RETURN-PAYLOAD", Signature = "NEVER-RETURN-SIGNATURE",
        };
        for (var i = 0; i < attempts; i++) delivery.RecordAttempt(success && i == attempts - 1, success && i == attempts - 1 ? 200 : 503, attempted);
        return delivery;
    }

    [Fact]
    public async Task Default_cohort_reports_current_outcomes_and_lifetime_attempts_without_delivery_work()
    {
        using var scenario = await Scenario.Create();
        var now = scenario.Clock.Instant;
        var subscription = Subscription();
        var rows = new[]
        {
            Delivery(subscription.Id, now.AddDays(-1), 0, false, now),
            Delivery(subscription.Id, now.AddDays(-1), 2, false, now),
            Delivery(subscription.Id, now.AddDays(-1), 3, true, now),
            Delivery(subscription.Id, now.AddDays(-1), 5, false, now),
        };
        await scenario.Db(async db => { db.Add(subscription); db.AddRange(rows); await db.SaveChangesAsync(); });
        scenario.Commands.Statements.Clear();
        var response = await scenario.Admin.GetAsync(Path + "?subscriptionId=" + subscription.Id);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("NEVER-RETURN", text);
        var report = (await response.Content.ReadFromJsonAsync<WebhookHealthResponse>())!;
        Assert.Equal(now, report.AsOf);
        Assert.Equal(now.AddDays(-7), report.From);
        Assert.Equal(now, report.To);
        Assert.Equal(new WebhookHealthTotals(4, 1, 1, 2, 1, 10), report.Overall);
        Assert.Equal(0.25m, report.Overall.SuccessRatio);
        Assert.Equal(report.Overall, Assert.Single(report.Subscriptions.Items).Totals);
        Assert.Equal(0, scenario.Sender.Calls);
        var commands = scenario.Commands.Statements.ToArray();
        Assert.InRange(commands.Length, 1, 6);
        Assert.All(commands, command =>
        {
            Assert.DoesNotContain("INSERT INTO", command);
            Assert.DoesNotContain("UPDATE ", command);
            Assert.DoesNotContain("DELETE FROM", command);
            Assert.DoesNotContain("Payload", command);
            Assert.DoesNotContain("Signature", command);
            Assert.DoesNotContain("Secret", command);
        });
        await scenario.Db(async db =>
        {
            foreach (var row in rows)
            {
                var actual = await db.WebhookDeliveries.SingleAsync(d => d.Id == row.Id);
                Assert.Equal(row.Status, actual.Status);
                Assert.Equal(row.AttemptCount, actual.AttemptCount);
                Assert.Equal(row.LastAttemptAt, actual.LastAttemptAt);
            }
        });
    }

    [Fact]
    public async Task Half_open_cohort_paging_uses_whole_cohort_totals_and_reflects_later_retry_outcomes()
    {
        using var scenario = await Scenario.Create();
        var now = scenario.Clock.Instant;
        var from = now.AddDays(-3);
        var to = from.AddDays(1);
        var a = Subscription(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var b = Subscription(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var empty = Subscription();
        var failed = Delivery(a.Id, to.AddTicks(-1), 1, false, now);
        await scenario.Db(async db =>
        {
            db.AddRange(a, b, empty);
            db.WebhookDeliveries.AddRange(
                Delivery(a.Id, from.AddTicks(-1), 0, false, now),
                Delivery(a.Id, from, 2, true, now), failed,
                Delivery(a.Id, to, 0, false, now),
                Delivery(b.Id, from.AddHours(1), 1, true, now));
            await db.SaveChangesAsync();
        });
        var query = Window(from, to);
        scenario.Commands.Statements.Clear();
        var first = (await scenario.Admin.GetFromJsonAsync<WebhookHealthResponse>(Path + "?" + query + "&pageSize=1"))!;
        var firstQueryCount = scenario.Commands.Statements.Count;
        scenario.Commands.Statements.Clear();
        var second = (await scenario.Admin.GetFromJsonAsync<WebhookHealthResponse>(Path + "?" + query + "&pageSize=1&page=2"))!;
        Assert.Equal(firstQueryCount, scenario.Commands.Statements.Count);
        Assert.InRange(firstQueryCount, 1, 6);
        Assert.Equal(2, first.Subscriptions.TotalCount);
        Assert.Equal(a.Id, Assert.Single(first.Subscriptions.Items).SubscriptionId);
        Assert.Equal(b.Id, Assert.Single(second.Subscriptions.Items).SubscriptionId);
        Assert.Equal(first.Overall, second.Overall);
        Assert.Equal(new WebhookHealthTotals(3, 0, 2, 1, 0, 4), first.Overall);
        Assert.Equal(2m / 3, first.Overall.SuccessRatio);
        Assert.True(first.Subscriptions.HasNextPage);
        Assert.True(second.Subscriptions.HasPreviousPage);
        var nothing = (await scenario.Admin.GetFromJsonAsync<WebhookHealthResponse>(Path + "?" + query + "&subscriptionId=" + empty.Id))!;
        Assert.Equal(0, nothing.Overall.Total);
        Assert.Null(nothing.Overall.SuccessRatio);
        Assert.Empty(nothing.Subscriptions.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Admin.GetAsync(Path + "?" + query + "&subscriptionId=" + Guid.NewGuid())).StatusCode);
        // The attempt happens after the creation window, but changes this cohort's current outcome.
        await scenario.Db(async db => { (await db.WebhookDeliveries.SingleAsync(d => d.Id == failed.Id)).RecordAttempt(true, 200, now); await db.SaveChangesAsync(); });
        var after = (await scenario.Admin.GetFromJsonAsync<WebhookHealthResponse>(Path + "?" + query))!;
        Assert.Equal(3, after.Overall.Succeeded);
        Assert.Equal(0, after.Overall.Failed);
        Assert.Equal(5, after.Overall.CohortLifetimeAttemptCount);
        Assert.Equal(1m, after.Overall.SuccessRatio);
        Assert.Equal(0, scenario.Sender.Calls);
    }

    [Fact]
    public async Task Date_bounds_pagination_and_admin_access_are_enforced()
    {
        using var scenario = await Scenario.Create();
        var now = scenario.Clock.Instant;
        foreach (var query in new[]
        {
            "from=" + Date(now.AddDays(-1)), "to=" + Date(now), Window(now, now), Window(now, now.AddDays(-1)),
            Window(now.AddDays(-31), now), "page=2147483647&pageSize=100", "pageSize=101", "page=0", "subscriptionId=not-a-guid",
        }) Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.GetAsync(Path + "?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.GetAsync(Path + "?" + Window(now.AddDays(-30), now))).StatusCode);
        using var anonymous = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path)).StatusCode);
        using var customer = scenario.App.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, Guid.NewGuid().ToString("N") + "@health.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(Path)).StatusCode);
        Assert.Equal(0, scenario.Sender.Calls);
    }

    private sealed class FrozenClock : TimeProvider
    {
        public DateTimeOffset Instant { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Instant;
    }
    private sealed class CountingSender : IWebhookSender
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public Task<WebhookSendResult> SendAsync(string url, string payload, string signature, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new WebhookSendResult(true, 200));
        }
    }
    private sealed class CommandLog : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Statements { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (id.Id == 20101) Statements.Enqueue(formatter(state, exception)); }
        public void Dispose() { }
    }
    private sealed class Scenario : IDisposable
    {
        private readonly AgoraApiFactory _source = new();
        public FrozenClock Clock { get; } = new();
        public CountingSender Sender { get; } = new();
        public CommandLog Commands { get; } = new();
        public WebApplicationFactory<Program> App { get; }
        public HttpClient Admin { get; }
        private Scenario()
        {
            App = _source.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.RemoveAll<IWebhookSender>();
                services.AddSingleton<IWebhookSender>(Sender);
            }));
            Admin = App.CreateClient();
            App.Services.GetRequiredService<ILoggerFactory>().AddProvider(Commands);
        }
        public static async Task<Scenario> Create()
        {
            var scenario = new Scenario();
            await scenario.Admin.AuthenticateAsAdminAsync();
            scenario.Clock.Instant = DateTimeOffset.UtcNow;
            return scenario;
        }
        public async Task Db(Func<AgoraDbContext, Task> action)
        {
            using var scope = App.Services.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<AgoraDbContext>());
        }
        public void Dispose() { Admin.Dispose(); App.Dispose(); _source.Dispose(); }
    }
}
