using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class ReviewsApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly AddressDto Address = new(
        "Rev Iewer", "5 Feedback Lane", null, "Rateville", "RT", "11111", "US");

    [Fact]
    public async Task Post_WithoutPurchase_Returns422()
    {
        var client = await NewCustomer("no-purchase@example.com");
        var productId = await ProductIdBySlug("classic-cotton-tee");

        var response = await client.PostAsJsonAsync($"/api/products/{productId}/reviews",
            new CreateReviewRequest(5, "Great", "Never bought it though."));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Anonymous_Returns401()
    {
        var productId = await ProductIdBySlug("classic-cotton-tee");

        var response = await _client.PostAsJsonAsync($"/api/products/{productId}/reviews",
            new CreateReviewRequest(5, null, "Anonymous praise."));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifiedPurchase_ReviewFlow_PendingUntilApproved_ThenAggregates()
    {
        var client = await NewCustomer("buyer-flow@example.com", "Flow Buyer");
        var productId = await ProductIdBySlug("ember-pour-over-kettle");
        await BuyProduct(client, "KET-EMB-1L");

        // Submit -> 201 pending.
        var create = await client.PostAsJsonAsync($"/api/products/{productId}/reviews",
            new CreateReviewRequest(5, "Lovely kettle", "Pours like a dream."));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var review = await create.Content.ReadFromJsonAsync<ReviewResponse>();
        Assert.Equal("Pending", review!.Status);
        Assert.Equal("Flow Buyer", review.ReviewerName);

        // Not publicly visible while pending; no aggregate yet.
        var pendingList = await _client.GetFromJsonAsync<PagedResult<ReviewResponse>>(
            $"/api/products/{productId}/reviews");
        Assert.Equal(0, pendingList!.TotalCount);
        var productBefore = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}");
        Assert.Null(productBefore!.AverageRating);
        Assert.Equal(0, productBefore.ReviewCount);

        // Approve as admin -> visible + aggregated.
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        var approve = await admin.PostAsync($"/api/reviews/{review.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var list = await _client.GetFromJsonAsync<PagedResult<ReviewResponse>>(
            $"/api/products/{productId}/reviews");
        Assert.Equal(1, list!.TotalCount);
        Assert.Equal("Approved", list.Items[0].Status);

        var productAfter = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}");
        Assert.Equal(5m, productAfter!.AverageRating);
        Assert.Equal(1, productAfter.ReviewCount);
    }

    [Fact]
    public async Task SecondReviewForSameProduct_Returns409()
    {
        var client = await NewCustomer("double-review@example.com");
        var productId = await ProductIdBySlug("canvas-weekender-cap");
        await BuyProduct(client, "CAP-KHK");

        var first = await client.PostAsJsonAsync($"/api/products/{productId}/reviews",
            new CreateReviewRequest(4, null, "Nice cap."));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync($"/api/products/{productId}/reviews",
            new CreateReviewRequest(2, null, "Changed my mind."));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Rejected_NeverAppearsPublicly()
    {
        var client = await NewCustomer("rejected@example.com");
        var productId = await ProductIdBySlug("volt-65w-gan-charger");
        await BuyProduct(client, "CHG-65W");
        var review = await PostReview(client, productId, 1, "SPAM", "Buy pills at spam.example");

        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        var reject = await admin.PostAsJsonAsync($"/api/reviews/{review.Id}/reject",
            new RejectReviewRequest("Spam link"));
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        var rejected = await reject.Content.ReadFromJsonAsync<ReviewResponse>();
        Assert.Equal("Rejected", rejected!.Status);
        Assert.Equal("Spam link", rejected.ModerationNote);

        var list = await _client.GetFromJsonAsync<PagedResult<ReviewResponse>>(
            $"/api/products/{productId}/reviews");
        Assert.Equal(0, list!.TotalCount);
    }

    [Fact]
    public async Task ModerationQueue_AdminOnly()
    {
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        var ok = await admin.GetAsync("/api/reviews?status=pending");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var customer = await NewCustomer("mod-nope@example.com");
        var forbidden = await customer.GetAsync("/api/reviews");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var approveAnon = await _client.PostAsync($"/api/reviews/{Guid.NewGuid()}/approve", null);
        Assert.Equal(HttpStatusCode.Unauthorized, approveAnon.StatusCode);
    }

    [Fact]
    public async Task AverageRating_AveragesApprovedOnly()
    {
        var productId = await ProductIdBySlug("nimbus-mechanical-keyboard");
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        // Two approved reviews (5 and 2) and one left pending (1).
        foreach (var (email, rating, approve) in new[]
                 {
                     ("kb-a@example.com", 5, true),
                     ("kb-b@example.com", 2, true),
                     ("kb-c@example.com", 1, false),
                 })
        {
            var client = await NewCustomer(email);
            await BuyProduct(client, "KB-NIM-BRN");
            var review = await PostReview(client, productId, rating, null, $"Rating {rating}.");
            if (approve)
            {
                (await admin.PostAsync($"/api/reviews/{review.Id}/approve", null))
                    .EnsureSuccessStatusCode();
            }
        }

        var product = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}");
        Assert.Equal(3.5m, product!.AverageRating); // (5 + 2) / 2
        Assert.Equal(2, product.ReviewCount);

        // Search list carries the same aggregate.
        var search = await _client.GetFromJsonAsync<PagedResult<ProductResponse>>(
            "/api/products?search=Nimbus");
        var listed = Assert.Single(search!.Items);
        Assert.Equal(3.5m, listed.AverageRating);
        Assert.Equal(2, listed.ReviewCount);
    }

    [Fact]
    public async Task HelpfulVotes_OnePerCustomer_AndRemovable()
    {
        var author = await NewCustomer("author@example.com");
        var productId = await ProductIdBySlug("cedar-scented-candle");
        await BuyProduct(author, "CDL-CDR-S");
        var review = await PostReview(author, productId, 4, "Cosy", "Smells great.");

        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        (await admin.PostAsync($"/api/reviews/{review.Id}/approve", null)).EnsureSuccessStatusCode();

        var voter = await NewCustomer("voter@example.com");
        var vote = await voter.PostAsync($"/api/reviews/{review.Id}/helpful", null);
        Assert.Equal(HttpStatusCode.OK, vote.StatusCode);
        var voted = await vote.Content.ReadFromJsonAsync<ReviewResponse>();
        Assert.Equal(1, voted!.HelpfulCount);

        var duplicate = await voter.PostAsync($"/api/reviews/{review.Id}/helpful", null);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var unvote = await voter.DeleteAsync($"/api/reviews/{review.Id}/helpful");
        Assert.Equal(HttpStatusCode.OK, unvote.StatusCode);
        var unvoted = await unvote.Content.ReadFromJsonAsync<ReviewResponse>();
        Assert.Equal(0, unvoted!.HelpfulCount);
    }

    [Fact]
    public async Task HelpfulVote_OnPendingReview_Returns404()
    {
        var author = await NewCustomer("pending-author@example.com");
        var productId = await ProductIdBySlug("trailblazer-hoodie");
        await BuyProduct(author, "HOOD-GRY-M");
        var review = await PostReview(author, productId, 3, null, "Still pending.");

        var voter = await NewCustomer("pending-voter@example.com");
        var vote = await voter.PostAsync($"/api/reviews/{review.Id}/helpful", null);

        Assert.Equal(HttpStatusCode.NotFound, vote.StatusCode);
    }

    [Fact]
    public async Task EditingApprovedReview_SendsItBackToModeration()
    {
        var client = await NewCustomer("editor@example.com");
        var productId = await ProductIdBySlug("aurora-wireless-earbuds");
        await BuyProduct(client, "EAR-AUR-BLK");
        var review = await PostReview(client, productId, 5, "Perfect", "No complaints.");

        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        (await admin.PostAsync($"/api/reviews/{review.Id}/approve", null)).EnsureSuccessStatusCode();

        var update = await client.PutAsJsonAsync($"/api/reviews/{review.Id}",
            new CreateReviewRequest(3, "Update", "Battery degraded."));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<ReviewResponse>();
        Assert.Equal("Pending", updated!.Status);

        // Off the public list again.
        var list = await _client.GetFromJsonAsync<PagedResult<ReviewResponse>>(
            $"/api/products/{productId}/reviews");
        Assert.Equal(0, list!.TotalCount);
    }

    [Fact]
    public async Task CustomerCannotEditSomeoneElsesReview()
    {
        var author = await NewCustomer("own-review@example.com");
        var productId = await ProductIdBySlug("classic-cotton-tee");
        await BuyProduct(author, "TEE-WHT-M");
        var review = await PostReview(author, productId, 4, null, "Mine.");

        var other = await NewCustomer("not-owner@example.com");
        var update = await other.PutAsJsonAsync($"/api/reviews/{review.Id}",
            new CreateReviewRequest(1, null, "Hijacked."));
        var delete = await other.DeleteAsync($"/api/reviews/{review.Id}");

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    private async Task<HttpClient> NewCustomer(string email, string? fullName = null)
    {
        var client = _factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAsync(client, email, fullName: fullName));
        return client;
    }

    private async Task<Guid> ProductIdBySlug(string slug)
    {
        var product = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/by-slug/{slug}");
        return product!.Id;
    }

    private async Task BuyProduct(HttpClient client, string sku)
    {
        var cartResponse = await client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var inventory = await client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        (await client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, 1))).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "buyer@example.com", Address, null, "tok_visa")))
            .EnsureSuccessStatusCode();
    }

    private static async Task<ReviewResponse> PostReview(
        HttpClient client, Guid productId, int rating, string? title, string body)
    {
        var response = await client.PostAsJsonAsync($"/api/products/{productId}/reviews",
            new CreateReviewRequest(rating, title, body));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReviewResponse>())!;
    }
}
