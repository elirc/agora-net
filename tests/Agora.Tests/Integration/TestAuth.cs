using System.Net.Http.Headers;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Infrastructure.Persistence;

namespace Agora.Tests.Integration;

/// <summary>Login/registration helpers for integration tests.</summary>
public static class TestAuth
{
    public const string CustomerPassword = "CustomerPass123!";

    /// <summary>Logs in and returns the bearer token.</summary>
    public static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
    }

    /// <summary>Registers a fresh customer account and returns the bearer token.</summary>
    public static async Task<string> RegisterAsync(
        HttpClient client, string email, string password = CustomerPassword, string? fullName = null)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, password, fullName));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
    }

    /// <summary>Authenticates the client as the seeded admin for all subsequent requests.</summary>
    public static async Task AuthenticateAsAdminAsync(this HttpClient client)
    {
        var token = await LoginAsync(client, AgoraDbSeeder.AdminEmail, AgoraDbSeeder.AdminPassword);
        client.UseBearer(token);
    }

    public static void UseBearer(this HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
