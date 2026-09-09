using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;
namespace Agora.Api.Contracts;

public sealed record CreateOrderHoldRequest(
    [Required, MaxLength(40)] string Reason,
    [MaxLength(500)] string? Note);

public sealed record ReleaseOrderHoldRequest(
    [Required, Range(0, long.MaxValue)] long? ExpectedRevision);

public sealed record OrderHoldResponse(
    Guid Id, Guid OrderId, string Reason, string? Note, Guid CreatedBy,
    DateTimeOffset CreatedAt, bool IsActive, long Revision,
    Guid? ReleasedBy, DateTimeOffset? ReleasedAt)
{
    public static OrderHoldResponse From(OrderHold hold) => new(
        hold.Id, hold.OrderId, hold.Reason.ToString(), hold.Note, hold.CreatedBy,
        hold.CreatedAt, hold.IsActive, hold.Revision, hold.ReleasedBy, hold.ReleasedAt);
}

public sealed record AssignmentCommandRequest(
    Guid AssignmentId,
    [Required, Range(0, long.MaxValue)] long? ExpectedRevision);

public sealed record WarehouseAssignmentResponse(
    Guid OrderId, Guid AssignmentId, Guid OwnerId, DateTimeOffset ClaimedAt,
    DateTimeOffset ExpiresAt, long Revision, bool IsExpired)
{
    public static WarehouseAssignmentResponse From(WarehouseAssignment assignment, DateTimeOffset now) => new(
        assignment.OrderId, assignment.AssignmentId, assignment.OwnerId, assignment.ClaimedAt,
        assignment.ExpiresAt, assignment.Revision, !assignment.IsLive(now));
}

public sealed record AssignmentReleaseResponse(
    Guid OrderId, Guid AssignmentId, Guid OwnerId, DateTimeOffset ReleasedAt);
