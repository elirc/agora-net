using System.ComponentModel.DataAnnotations;

namespace Agora.Api.Contracts;

public sealed record MergeCartsRequest([Required, MaxLength(64)] string SourceToken,
    [Required, MaxLength(64)] string TargetToken,
    [Required, Range(0, int.MaxValue)] int? ExpectedSourceVersion,
    [Required, Range(0, int.MaxValue)] int? ExpectedTargetVersion);
public sealed record CartMergeResponse(CartResponse Target, int SourceVersion, int TargetVersion);
