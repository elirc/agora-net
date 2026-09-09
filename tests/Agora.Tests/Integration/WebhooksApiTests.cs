using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Agora.Tests.Integration;

public class WebhooksApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private const string Secret = "super-secret-signing-key";

    private static readonly AddressDto Address = new(
        "Hook Handler", "9 Callback Ct", null, "Eventville", "EV", "55555", "US");

    [Fact]
    public async Task Subscriptions_AreAdminOnly()
    {
        var customer = _factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, "hook-nobody@example.com"));

        var anonymous = await _client.PostAsJsonAsync("/api/webhooks",
            new SaveWebhookSubscriptionRequest("https://example.com/hook", Secret, ["order.paid"], null));
        var forbidden = await customer.PostAsJsonAsync("/api/webhooks",
            new SaveWebhookSubscriptionRequest("https://example.com/hook", Secret, ["order.paid"], null));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownEvent_Returns422()
    {
        var admin = await AdminClient();

        var response = await admin.PostAsJsonAsync("/api/webhooks",
            new SaveWebhookSubscriptionRequest(
                "https://example.com/hook", Secret, ["order.launched"], null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Subscription_DoesNotEchoTheSecret()
    {
        var admin = await AdminClient();

        var response = await admin.PostAsJsonAsync("/api/webhooks",
            new SaveWebhookSubscriptionRequest(
                "https://example.com/no-secret", Secret, ["order.paid"], null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Secret, body);
    }

    [Fact]
    public async Task Checkout_FiresCreatedAndPaid_WithValidSignature()
    {
        using var localFactory = new WebhookApiFactory();
        var client = localFactory.CreateClient();
        var admin = localFactory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        var subscription = await Subscribe(admin, "https://example.com/ok",
            ["order.created", "order.paid", "order.fulfilled", "order.refunded"]);

        var order = await PlaceOrder(localFactory, client, "TEE-BLK-S", 1);

        var deliveries = await Deliveries(admin, subscription);
        Assert.Equal(2, deliveries.Count);
        Assert.Contains(deliveries, d => d.EventType == "order.created");
        Assert.Contains(deliveries, d => d.EventType == "order.paid");
        Assert.All(deliveries, d =>
        {
            Assert.Equal("Succeeded", d.Status);
            Assert.Equal(1, d.AttemptCount);
            Assert.Equal(200, d.LastResponseStatusCode);
            Assert.Contains(order.Number, d.Payload);
            // The recorded signature must verify against the payload + secret.
            Assert.Equal(WebhookSigner.ComputeSignature(Secret, d.Payload), d.Signature);
        });
    }

    [Fact]
    public async Task FulfillAndRefund_FireTheirEvents()
    {
        using var localFactory = new WebhookApiFactory();
        var client = localFactory.CreateClient();
        var admin = localFactory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        var subscription = await Subscribe(admin, "https://example.com/lifecycle",
            ["order.fulfilled", "order.refunded"]);

        var order = await PlaceOrder(localFactory, client, "CAP-KHK", 1);
        (await admin.PostAsync($"/api/orders/{order.Number}/fulfill", null)).EnsureSuccessStatusCode();
        await Drain(localFactory);
        (await admin.PostAsync($"/api/orders/{order.Number}/refund", null)).EnsureSuccessStatusCode();
        await Drain(localFactory);

        var deliveries = await Deliveries(admin, subscription);
        Assert.Equal(2, deliveries.Count);
        Assert.Contains(deliveries, d => d.EventType == "order.fulfilled");
        Assert.Contains(deliveries, d => d.EventType == "order.refunded");
        // Checkout events were not subscribed, so nothing else arrived.
        Assert.DoesNotContain(deliveries, d => d.EventType == "order.created");
    }

    [Fact]
    public async Task EventFiltering_OnlySubscribedEventsDeliver()
    {
        using var localFactory = new WebhookApiFactory();
        var client = localFactory.CreateClient();
        var admin = localFactory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        var paidOnly = await Subscribe(admin, "https://example.com/paid-only", ["order.paid"]);
        await PlaceOrder(localFactory, client, "TEE-BLK-M", 1);

        var deliveries = await Deliveries(admin, paidOnly);
        var only = Assert.Single(deliveries);
        Assert.Equal("order.paid", only.EventType);
    }

    [Fact]
    public async Task FailedDelivery_IsLogged_AndRetryable()
    {
        using var localFactory = new WebhookApiFactory();
        var client = localFactory.CreateClient();
        var admin = localFactory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        // The fake sender rejects URLs containing "fail".
        var subscription = await Subscribe(admin, "https://example.com/always-fails", ["order.paid"]);
        await PlaceOrder(localFactory, client, "CHG-65W", 1);

        var deliveries = await Deliveries(admin, subscription);
        var failed = Assert.Single(deliveries);
        Assert.Equal("Failed", failed.Status);
        Assert.Equal(1, failed.AttemptCount);
        Assert.Equal(500, failed.LastResponseStatusCode);

        var retry = await admin.PostAsync($"/api/webhooks/deliveries/{failed.Id}/retry", null);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode); await Drain(localFactory);
        var retried = Assert.Single(await Deliveries(admin, subscription));
        Assert.Equal("Failed", retried!.Status);
        Assert.Equal(2, retried.AttemptCount);
    }

    [Fact]
    public async Task Retry_ExhaustedDelivery_Returns409()
    {
        using var localFactory = new WebhookApiFactory();
        var client = localFactory.CreateClient();
        var admin = localFactory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        var subscription = await Subscribe(admin, "https://example.com/fail-forever", ["order.paid"]);
        await PlaceOrder(localFactory, client, "KET-EMB-1L", 1);
        var failed = Assert.Single(await Deliveries(admin, subscription));

        // First attempt already happened; retry up to the cap of 5.
        for (var attempt = 2; attempt <= 5; attempt++)
        {
            var retry = await admin.PostAsync($"/api/webhooks/deliveries/{failed.Id}/retry", null);
            Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode); await Drain(localFactory);
        }

        var exhausted = await admin.PostAsync($"/api/webhooks/deliveries/{failed.Id}/retry", null);
        Assert.Equal(HttpStatusCode.Conflict, exhausted.StatusCode);
    }

    [Fact]
    public async Task InactiveSubscription_ReceivesNothing()
    {
        using var localFactory = new WebhookApiFactory();
        var client = localFactory.CreateClient();
        var admin = localFactory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        var subscription = await Subscribe(admin, "https://example.com/dormant", ["order.paid"]);
        var update = await admin.PutAsJsonAsync($"/api/webhooks/{subscription}",
            new SaveWebhookSubscriptionRequest(
                "https://example.com/dormant", Secret, ["order.paid"], false));
        update.EnsureSuccessStatusCode();

        await PlaceOrder(localFactory, client, "CDL-CDR-S", 1);

        var deliveries = await Deliveries(admin, subscription);
        Assert.Empty(deliveries);
    }

    private async Task<HttpClient> AdminClient()
    {
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        return admin;
    }

    private static async Task<Guid> Subscribe(HttpClient admin, string url, List<string> events)
    {
        var response = await admin.PostAsJsonAsync("/api/webhooks",
            new SaveWebhookSubscriptionRequest(url, Secret, events, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>())!.Id;
    }

    private static async Task<List<WebhookDeliveryResponse>> Deliveries(
        HttpClient admin, Guid subscriptionId)
    {
        var page = await admin.GetFromJsonAsync<PagedResult<WebhookDeliveryResponse>>(
            $"/api/webhooks/{subscriptionId}/deliveries?pageSize=100");
        return page!.Items.ToList();
    }

    private static async Task<OrderResponse> PlaceOrder(WebApplicationFactory<Program> factory, HttpClient client, string sku, int quantity)
    {
        var cartResponse = await client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var inventory = await client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        (await client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, quantity))).EnsureSuccessStatusCode();

        var checkout = await client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "hooked@example.com", Address, null, "tok_visa"));
        checkout.EnsureSuccessStatusCode();
        var order = (await checkout.Content.ReadFromJsonAsync<OrderResponse>())!;
        await Drain(factory); return order;
    }

    private static async Task Drain(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WebhookOutboxRunner>().RunOnceAsync();
    }
}
