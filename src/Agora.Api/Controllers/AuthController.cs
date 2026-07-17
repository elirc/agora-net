using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AgoraDbContext db,
    IPasswordHasher passwordHasher,
    JwtTokenService tokenService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Customers.AnyAsync(c => c.Email == email, ct))
        {
            return Conflict(new ProblemDetails
            {
                Title = "An account with this email already exists.",
            });
        }

        var customer = new Customer
        {
            Email = email,
            PasswordHash = passwordHasher.Hash(request.Password),
            FullName = request.FullName?.Trim() ?? string.Empty,
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        var (token, expiresAt) = tokenService.IssueToken(customer);
        return CreatedAtAction(nameof(Me), null,
            new AuthResponse(token, expiresAt, CustomerResponse.From(customer)));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Email == email, ct);

        if (customer is null || !passwordHasher.Verify(request.Password, customer.PasswordHash))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid email or password.",
            });
        }

        var (token, expiresAt) = tokenService.IssueToken(customer);
        return Ok(new AuthResponse(token, expiresAt, CustomerResponse.From(customer)));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<CustomerResponse>> Me(CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);
        return customer is null ? NotFound() : Ok(CustomerResponse.From(customer));
    }
}
