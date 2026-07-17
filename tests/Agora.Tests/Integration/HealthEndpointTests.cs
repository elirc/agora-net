using System.Net;
using System.Net.Http.Json;

namespace Agora.Tests.Integration;

public class HealthEndpointTests : IClassFixture<AgoraApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(AgoraApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_Health_Returns200()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Health_ReportsHealthyServiceName()
    {
        var body = await _client.GetFromJsonAsync<HealthDto>("/health");

        Assert.NotNull(body);
        Assert.Equal("healthy", body.Status);
        Assert.Equal("agora-net", body.Service);
    }

    private sealed record HealthDto(string Status, string Service, DateTimeOffset UtcNow);
}
