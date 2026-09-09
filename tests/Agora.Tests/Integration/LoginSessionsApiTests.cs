using System.Net;
using System.Net.Http.Json;
using System.Text;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Agora.Tests.Integration;

public sealed class LoginSessionsApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    [Fact]
    public async Task Two_logins_can_be_listed_and_one_revoked_without_revoking_the_other()
    {
        var first = factory.CreateClient();
        var registered = await Register(first, "sessions-a@example.com", "  Phone  ");
        var second = factory.CreateClient();
        var laptop = await Login(second, "sessions-a@example.com", "Laptop");
        second.UseBearer(laptop.Token);

        var list = await second.GetFromJsonAsync<PagedResult<LoginSessionResponse>>("/api/me/sessions?page=1&pageSize=10");
        Assert.Equal(2, list!.TotalCount);
        Assert.Single(list.Items, s => s.IsCurrent && s.Id == laptop.SessionId && s.DeviceLabel == "Laptop");
        Assert.Single(list.Items, s => !s.IsCurrent && s.Id == registered.SessionId && s.DeviceLabel == "Phone");

        Assert.Equal(HttpStatusCode.NoContent,
            (await second.DeleteAsync($"/api/me/sessions/{registered.SessionId}")).StatusCode);
        first.UseBearer(registered.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await second.DeleteAsync($"/api/me/sessions/{registered.SessionId}")).StatusCode);
    }

    [Fact]
    public async Task Revoke_all_includes_current_session_and_applies_to_its_next_request()
    {
        var client = factory.CreateClient();
        var first = await Register(client, "sessions-all@example.com", "First");
        var second = await Login(client, "sessions-all@example.com", "Second");
        client.UseBearer(second.Token);

        var response = await client.PostAsync("/api/me/sessions/revoke-all", null);
        var receipt = await response.Content.ReadFromJsonAsync<RevokeAllSessionsResponse>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, receipt!.RevokedCount);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        var firstClient = factory.CreateClient();
        firstClient.UseBearer(first.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await firstClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Another_customers_session_is_hidden_as_not_found()
    {
        var a = factory.CreateClient();
        var authA = await Register(a, "sessions-owner-a@example.com", null);
        var b = factory.CreateClient();
        var authB = await Register(b, "sessions-owner-b@example.com", null);
        a.UseBearer(authA.Token);

        Assert.Equal(HttpStatusCode.NotFound,
            (await a.DeleteAsync($"/api/me/sessions/{authB.SessionId}")).StatusCode);
        b.UseBearer(authB.Token);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Session_expiry_mismatch_role_change_and_customer_removal_each_reject_token()
    {
        var expiryClient = factory.CreateClient();
        var expiry = await Register(expiryClient, "sessions-expiry@example.com", null);
        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE LoginSession SET ExpiresAt = ExpiresAt - 10000000 WHERE Id = {expiry.SessionId}"));
        expiryClient.UseBearer(expiry.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await expiryClient.GetAsync("/api/auth/me")).StatusCode);

        var roleClient = factory.CreateClient();
        var role = await Register(roleClient, "sessions-role@example.com", null);
        await factory.WithDbAsync(async db =>
        {
            var customer = await db.Customers.SingleAsync(c => c.Id == role.Customer.Id);
            customer.Role = CustomerRole.Admin;
            await db.SaveChangesAsync();
        });
        roleClient.UseBearer(role.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await roleClient.GetAsync("/api/auth/me")).StatusCode);

        var removedClient = factory.CreateClient();
        var removed = await Register(removedClient, "sessions-removed@example.com", null);
        await factory.WithDbAsync(async db =>
        {
            db.Customers.Remove(await db.Customers.SingleAsync(c => c.Id == removed.Customer.Id));
            await db.SaveChangesAsync();
        });
        removedClient.UseBearer(removed.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await removedClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_and_invalid_page_requests_are_rejected_without_exposing_rows()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me/sessions")).StatusCode);

        var client = factory.CreateClient();
        var auth = await Register(client, "sessions-bounds@example.com", null);
        client.UseBearer(auth.Token);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/me/sessions?page=0&pageSize=101")).StatusCode);
    }

    [Fact]
    public async Task Correctly_signed_unknown_session_and_cutover_token_without_sid_are_rejected()
    {
        var setup = factory.CreateClient();
        var auth = await Register(setup, "sessions-forged@example.com", null);

        var unknownSession = SignedToken(auth.Customer, Guid.NewGuid());
        var unknownClient = factory.CreateClient();
        unknownClient.UseBearer(unknownSession);
        Assert.Equal(HttpStatusCode.Unauthorized, (await unknownClient.GetAsync("/api/auth/me")).StatusCode);

        var legacyToken = SignedToken(auth.Customer, null);
        var legacyClient = factory.CreateClient();
        legacyClient.UseBearer(legacyToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await legacyClient.GetAsync("/api/auth/me")).StatusCode);
    }

    private static async Task<AuthResponse> Register(HttpClient client, string email, string? device)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, TestAuth.CustomerPassword, "Session Tester", device));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static async Task<AuthResponse> Login(HttpClient client, string email, string? device)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, TestAuth.CustomerPassword, device));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static string SignedToken(CustomerResponse customer, Guid? sessionId)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = customer.Id.ToString(),
            [JwtRegisteredClaimNames.Email] = customer.Email,
            ["role"] = customer.Role,
        };
        if (sessionId.HasValue) claims["sid"] = sessionId.Value.ToString();
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "agora-api",
            Audience = "agora-clients",
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(30).UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                    "agora-dev-signing-key-change-me-in-production-0123456789abcdef")),
                SecurityAlgorithms.HmacSha256),
        });
    }
}
