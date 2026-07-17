using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class DiscountsApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // Admin-only mutations are exercised throughout; authenticate up front.
    public Task InitializeAsync() => _client.AuthenticateAsAdminAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_IncludesSeededCodes()
    {
        var discounts = await _client.GetFromJsonAsync<List<DiscountResponse>>("/api/discounts");

        Assert.NotNull(discounts);
        Assert.Contains(discounts, d => d.Code == "WELCOME10" && d.Type == "Percentage");
        Assert.Contains(discounts, d => d.Code == "SAVE5" && d.Type == "FixedAmount");
    }

    [Fact]
    public async Task Create_UppercasesCode_AndIsRetrievable()
    {
        var response = await _client.PostAsJsonAsync("/api/discounts",
            new CreateDiscountRequest("spring20", "Percentage", 20m, null, null, 50, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<DiscountResponse>();
        Assert.Equal("SPRING20", created!.Code);
        Assert.True(created.IsActive);

        var fetched = await _client.GetFromJsonAsync<DiscountResponse>("/api/discounts/SPRING20");
        Assert.Equal(20m, fetched!.Value);
    }

    [Fact]
    public async Task Create_DuplicateCode_Returns409()
    {
        var response = await _client.PostAsJsonAsync("/api/discounts",
            new CreateDiscountRequest("WELCOME10", "Percentage", 15m, null, null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidType_Returns422()
    {
        var response = await _client.PostAsJsonAsync("/api/discounts",
            new CreateDiscountRequest("BOGUS", "BuyOneGetOne", 1m, null, null, null, null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_PercentageOver100_Returns422()
    {
        var response = await _client.PostAsJsonAsync("/api/discounts",
            new CreateDiscountRequest("TOOMUCH", "Percentage", 150m, null, null, null, null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Update_DeactivatesCode()
    {
        await _client.PostAsJsonAsync("/api/discounts",
            new CreateDiscountRequest("PAUSABLE", "FixedAmount", 2m, null, null, null, null));

        var response = await _client.PutAsJsonAsync("/api/discounts/PAUSABLE",
            new UpdateDiscountRequest(null, null, false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<DiscountResponse>();
        Assert.False(updated!.IsActive);
    }

    [Fact]
    public async Task Delete_RemovesCode()
    {
        await _client.PostAsJsonAsync("/api/discounts",
            new CreateDiscountRequest("SHORTLIVED", "FixedAmount", 1m, null, null, null, null));

        var deleteResponse = await _client.DeleteAsync("/api/discounts/SHORTLIVED");
        var getResponse = await _client.GetAsync("/api/discounts/SHORTLIVED");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }
}
