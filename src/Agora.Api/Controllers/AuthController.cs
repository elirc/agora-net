using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AgoraDbContext db,
    IPasswordHasher passwordHasher,
    JwtTokenService tokenService,
    AuthenticationSessionService sessions) : ControllerBase
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
        var issue = tokenService.PrepareIssue();
        var session = sessions.Start(customer, request.DeviceLabel, issue.IssuedAt, issue.ExpiresAt);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        var token = tokenService.IssueToken(customer, session.Id, issue);
        return CreatedAtAction(nameof(Me), null,
            new AuthResponse(token, issue.ExpiresAt, session.Id, CustomerResponse.From(customer)));
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

        var issue = tokenService.PrepareIssue();
        var session = sessions.Start(customer, request.DeviceLabel, issue.IssuedAt, issue.ExpiresAt);
        await db.SaveChangesAsync(ct);
        var token = tokenService.IssueToken(customer, session.Id, issue);
        return Ok(new AuthResponse(token, issue.ExpiresAt, session.Id, CustomerResponse.From(customer)));
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
