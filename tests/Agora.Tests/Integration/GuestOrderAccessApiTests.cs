using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public sealed class GuestOrderAccessApiTests
{
    private static readonly AddressDto Address = new("Guest", "1 Main", null, "Seattle", "WA", "98101", "US");

    [Fact]
    public async Task Capability_closes_old_reads_and_allows_only_guest_order_and_return_actions()
    {
        using var scenario = await ReportTestScenario.Create();
        var guest = scenario.App.CreateClient();
        var order = await Checkout(guest, "TEE-BLK-S", "guest-capability@example.test");
        (await scenario.Admin.PostAsync($"/api/orders/{order.Number}/fulfill", null)).EnsureSuccessStatusCode();
        var issued = await Rotate(scenario.Admin, order.Number);

        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/orders/{order.Number}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/orders/{order.Number}/fulfillments")).StatusCode);
        guest.DefaultRequestHeaders.Add("X-Agora-Order-Access", issued.GuestOrderAccessToken);
        var readable = await guest.GetAsync($"/api/orders/{order.Number}");
        Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
        var body = await readable.Content.ReadAsStringAsync();
        Assert.DoesNotContain(issued.GuestOrderAccessToken, body);
        Assert.DoesNotContain("paymentTransactionId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("giftCardCode", body, StringComparison.OrdinalIgnoreCase);

        var create = await guest.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto("wrong-email@example.test", "Damaged", "Broken", [new(order.ItemId, 1)]));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var rma = await create.Content.ReadFromJsonAsync<CustomerReturnResponse>();
        Assert.NotNull(rma);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/returns/{rma.Number}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await guest.PostAsJsonAsync($"/api/returns/{rma.Number}/cancel",
            new CancelReturnRequestDto("somebody-else@example.test"))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await guest.PostAsync($"/api/orders/{order.Number}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await guest.PostAsync($"/api/orders/{order.Number}/refund", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await guest.PostAsync($"/api/returns/{rma.Number}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Token_is_order_bound_and_rotation_reveals_replacement_once_and_revokes_old()
    {
        using var scenario = await ReportTestScenario.Create();
        var client = scenario.App.CreateClient();
        var a = await Checkout(client, "TEE-BLK-M", "guest-a@example.test");
        var b = await Checkout(client, "CAP-KHK", "guest-b@example.test");
        var first = await Rotate(scenario.Admin, a.Number);

        client.DefaultRequestHeaders.Add("X-Agora-Order-Access", first.GuestOrderAccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/orders/{a.Number}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/orders/{b.Number}")).StatusCode);

        var replacement = await Rotate(scenario.Admin, a.Number);
        Assert.NotEqual(first.GuestOrderAccessToken, replacement.GuestOrderAccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/orders/{a.Number}")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Agora-Order-Access");
        client.DefaultRequestHeaders.Add("X-Agora-Order-Access", replacement.GuestOrderAccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/orders/{a.Number}")).StatusCode);
        Assert.DoesNotContain(replacement.GuestOrderAccessToken,
            await client.GetStringAsync($"/api/orders/{a.Number}"));
    }

    [Fact]
    public async Task Account_order_rejects_matching_email_and_foreign_guest_capability_but_owner_and_admin_can_read()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "cap-owner");
        var accountOrder = await Checkout(owner.Client, "TEE-WHT-M", owner.Email);
        var anonymous = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/orders/{accountOrder.Number}")).StatusCode);

        var guestOrder = await Checkout(anonymous, "CAP-KHK", owner.Email);
        var guestToken = await Rotate(scenario.Admin, guestOrder.Number);
        anonymous.DefaultRequestHeaders.Add("X-Agora-Order-Access", guestToken.GuestOrderAccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/orders/{accountOrder.Number}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync($"/api/orders/{accountOrder.Number}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.GetAsync($"/api/orders/{accountOrder.Number}")).StatusCode);

        var stranger = await AccountTestHelpers.Create(scenario, "cap-stranger");
        using (stranger)
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/orders/{accountOrder.Number}")).StatusCode);
    }

    private static async Task<(string Number, Guid ItemId)> Checkout(HttpClient client, string sku, string email)
    {
        var cartResponse = await client.PostAsync("/api/carts", null); cartResponse.EnsureSuccessStatusCode();
        var cart = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!;
        var inventory = (await client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}"))!;
        (await client.PostAsJsonAsync($"/api/carts/{cart.Token}/items", new AddCartItemRequest(inventory.ProductVariantId, 1))).EnsureSuccessStatusCode();
        var checkout = await client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(cart.Token, email, Address, null, "tok_visa")); checkout.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await checkout.Content.ReadAsStringAsync());
        var root = json.RootElement;
        // Checkout may expose the receipt as a flat order contract or under `order`.
        var order = root.TryGetProperty("order", out var nested) ? nested : root;
        return (order.GetProperty("number").GetString()!, order.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    private static async Task<GuestCredentialResponse> Rotate(HttpClient admin, string number)
    {
        var response = await admin.PostAsync($"/api/admin/orders/{number}/guest-access/rotate", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GuestCredentialResponse>())!;
    }
}
