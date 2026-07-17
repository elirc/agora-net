using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class CategoriesApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // Admin-only mutations are exercised throughout; authenticate up front.
    public Task InitializeAsync() => _client.AuthenticateAsAdminAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_ReturnsSeededCategories()
    {
        var categories = await _client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");

        Assert.NotNull(categories);
        Assert.Contains(categories, c => c.Slug == "apparel");
        Assert.Contains(categories, c => c.Slug == "electronics");
        Assert.Contains(categories, c => c.Slug == "home-kitchen");
    }

    [Fact]
    public async Task Create_Returns201_AndIsRetrievable()
    {
        var response = await _client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest("Outdoor Gear", null, "Tents and packs", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CategoryResponse>();
        Assert.NotNull(created);
        Assert.Equal("outdoor-gear", created.Slug); // auto-generated from name

        var fetched = await _client.GetFromJsonAsync<CategoryResponse>($"/api/categories/{created.Id}");
        Assert.Equal("Outdoor Gear", fetched!.Name);
    }

    [Fact]
    public async Task Create_DuplicateSlug_Returns409()
    {
        var response = await _client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest("Apparel Two", "apparel", null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_MissingName_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/categories", new { slug = "no-name" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownParent_Returns422()
    {
        var response = await _client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest("Orphan", "orphan-cat", null, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesFields()
    {
        var created = await CreateCategory("Renameable");

        var response = await _client.PutAsJsonAsync($"/api/categories/{created.Id}",
            new UpdateCategoryRequest("Renamed", created.Slug, "now with description", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CategoryResponse>();
        Assert.Equal("Renamed", updated!.Name);
        Assert.Equal("now with description", updated.Description);
    }

    [Fact]
    public async Task Update_UnknownId_Returns404()
    {
        var response = await _client.PutAsJsonAsync($"/api/categories/{Guid.NewGuid()}",
            new UpdateCategoryRequest("X", "x-slug", null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_SelfParent_Returns422()
    {
        var created = await CreateCategory("Self Parent");

        var response = await _client.PutAsJsonAsync($"/api/categories/{created.Id}",
            new UpdateCategoryRequest(created.Name, created.Slug, null, created.Id));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Delete_EmptyCategory_Returns204_ThenGone()
    {
        var created = await CreateCategory("Ephemeral");

        var deleteResponse = await _client.DeleteAsync($"/api/categories/{created.Id}");
        var getResponse = await _client.GetAsync($"/api/categories/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_CategoryWithProducts_Returns409()
    {
        var categories = await _client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        var apparel = categories!.Single(c => c.Slug == "apparel");

        var response = await _client.DeleteAsync($"/api/categories/{apparel.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task<CategoryResponse> CreateCategory(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest(name, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }
}
