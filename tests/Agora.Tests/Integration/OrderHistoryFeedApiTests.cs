using System.Net;
using System.Net.Http.Json;
using Agora.Api.Queries;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class OrderHistoryFeedApiTests
{
    [Fact]
    public async Task Tied_keys_traverse_two_two_one_without_duplicates_and_new_orders_stay_beyond_cutoff()
    {
        using var scenario = await ReportTestScenario.Create(); using var owner = await AccountTestHelpers.Create(scenario, "feed-owner");
        var cutoff = scenario.Clock.Instant;
        await scenario.Db(async db => { db.Orders.AddRange(new[] { "A", "B", "C", "D", "E" }.Select(s => Order(owner.Id, "HIST-" + s, cutoff))); await db.SaveChangesAsync(); });
        scenario.Commands.Statements.Clear();
        var firstResponse = await owner.Client.GetAsync("/api/me/orders/feed?limit=2"); Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode); Assert.True(firstResponse.Headers.CacheControl!.NoStore);
        var first = (await firstResponse.Content.ReadFromJsonAsync<OrderHistoryFeedResponse>())!;
        Assert.Equal(new[] { "HIST-E", "HIST-D" }, first.Items.Select(o => o.Number)); Assert.True(first.HasMore);
        await scenario.Db(async db => { db.Orders.Add(Order(owner.Id, "HIST-NEW", cutoff.AddSeconds(1))); db.Orders.Remove(await db.Orders.SingleAsync(o => o.Number == "HIST-E")); await db.SaveChangesAsync(); });
        scenario.Clock.Instant = cutoff.AddSeconds(2);
        var second = (await owner.Client.GetFromJsonAsync<OrderHistoryFeedResponse>(Path(first.NextCursor!, 2)))!;
        var third = (await owner.Client.GetFromJsonAsync<OrderHistoryFeedResponse>(Path(second.NextCursor!, 2)))!;
        Assert.Equal(new[] { "HIST-C", "HIST-B" }, second.Items.Select(o => o.Number)); Assert.True(second.HasMore);
        Assert.Equal(new[] { "HIST-A" }, third.Items.Select(o => o.Number)); Assert.False(third.HasMore); Assert.Null(third.NextCursor);
        Assert.Equal(5, first.Items.Concat(second.Items).Concat(third.Items).Select(o => o.Number).Distinct().Count());
        var sql = string.Join("\n", scenario.Commands.Statements);
        Assert.Contains("COLLATE BINARY", sql); Assert.DoesNotContain("OFFSET", sql); Assert.DoesNotContain("COUNT(*)", sql);
    }

    [Fact]
    public async Task Cursor_is_bound_to_owner_limit_expiry_and_protection_keys_with_generic_errors()
    {
        using var scenario = await ReportTestScenario.Create(); using var owner = await AccountTestHelpers.Create(scenario, "feed-a"); using var other = await AccountTestHelpers.Create(scenario, "feed-b");
        await scenario.Db(async db => { db.Orders.AddRange(Enumerable.Range(0, 3).Select(i => Order(owner.Id, "BOUND-" + i, scenario.Clock.Instant))); await db.SaveChangesAsync(); });
        var first = (await owner.Client.GetFromJsonAsync<OrderHistoryFeedResponse>("/api/me/orders/feed?limit=1"))!;
        var cursor = first.NextCursor!;
        var invalidPaths = new[] { Path(cursor, 2), Path(cursor[..^4] + "xxxx", 1), Path("not-protected-json", 1), "/api/me/orders/feed?limit=0", "/api/me/orders/feed?limit=101" };
        foreach (var path in invalidPaths) Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.Client.GetAsync(Path(cursor, 1))).StatusCode);
        Assert.Empty((await other.Client.GetFromJsonAsync<OrderHistoryFeedResponse>("/api/me/orders/feed?limit=100"))!.Items);
        using var anonymous = scenario.App.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me/orders/feed")).StatusCode);
        scenario.Clock.Instant = scenario.Clock.Instant.AddHours(24);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.GetAsync(Path(cursor, 1))).StatusCode);
    }

    [Fact]
    public async Task Backdated_insert_can_join_later_pages_and_index_matches_binary_seek_order()
    {
        using var scenario = await ReportTestScenario.Create(); using var owner = await AccountTestHelpers.Create(scenario, "feed-plan"); var now = scenario.Clock.Instant;
        await scenario.Db(async db => { db.Orders.AddRange(Enumerable.Range(0, 2000).Select(i => Order(owner.Id, "PLAN-" + i.ToString("D5"), now))); await db.SaveChangesAsync(); });
        var first = (await owner.Client.GetFromJsonAsync<OrderHistoryFeedResponse>("/api/me/orders/feed?limit=1"))!;
        await scenario.Db(async db => { db.Orders.Add(Order(owner.Id, "PLAN-01998Z", now)); await db.SaveChangesAsync(); });
        var next = (await owner.Client.GetFromJsonAsync<OrderHistoryFeedResponse>(Path(first.NextCursor!, 1)))!;
        Assert.Equal("PLAN-01998Z", next.Items.Single().Number);
        await scenario.Db(async db =>
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "EXPLAIN QUERY PLAN SELECT Id FROM Orders WHERE CustomerId = $owner AND CreatedAt <= $cutoff AND (CreatedAt < $last OR (CreatedAt = $last AND Number COLLATE BINARY < $number)) ORDER BY CreatedAt DESC, Number COLLATE BINARY DESC LIMIT 26";
            void Parameter(string name, object value) { var p = command.CreateParameter(); p.ParameterName = name; p.Value = value; command.Parameters.Add(p); }
            Parameter("$owner", owner.Id); Parameter("$cutoff", now.UtcTicks); Parameter("$last", now.UtcTicks); Parameter("$number", "PLAN-01999");
            using var reader = await command.ExecuteReaderAsync(); var plan = new List<string>(); while (await reader.ReadAsync()) plan.Add(reader.GetString(3));
            Assert.Contains(plan, line => line.Contains("IX_Orders_CustomerId_CreatedAt_Number")); Assert.DoesNotContain(plan, line => line.Contains("TEMP B-TREE"));
        });
    }

    private static string Path(string cursor, int limit) => $"/api/me/orders/feed?limit={limit}&cursor={Uri.EscapeDataString(cursor)}";
    private static Order Order(Guid owner, string number, DateTimeOffset created) => new() { CustomerId = owner, Number = number, Email = "contact@example.test", CreatedAt = created };
}
