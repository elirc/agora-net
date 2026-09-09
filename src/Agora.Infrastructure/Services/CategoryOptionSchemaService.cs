using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Agora.Infrastructure.Services;

public sealed record VariantOptionCandidate(Guid? Id, string Sku, IReadOnlyDictionary<string, string> Options);

public class CategoryOptionSchemaService(AgoraDbContext db, ILogger<CategoryOptionSchemaService> logger)
{
    /// <summary>Caller must hold its local authoring write transaction through schema read and catalog save.</summary>
    public async Task ValidateAuthoringAsync(Guid categoryId, IReadOnlyList<VariantOptionCandidate> variants, CancellationToken ct = default)
    {
        var schema = await db.Set<CategoryOptionSchema>().AsNoTracking().SingleOrDefaultAsync(s => s.CategoryId == categoryId, ct);
        if (schema is null || schema.Mode == CategoryOptionSchemaMode.Off) return;
        var rules = schema.ReadRules();
        var violations = variants.Select(v => new VariantOptionViolation(v.Id, v.Sku,
            CategoryOptionSchemaRules.Validate(rules, v.Options))).Where(v => v.Violations.Count > 0).ToArray();
        if (violations.Length == 0) return;
        if (schema.Mode == CategoryOptionSchemaMode.Enforce) throw new InvalidCategoryOptionsException(violations);
        var reasons = violations.SelectMany(v => v.Violations).GroupBy(v => v.Reason).ToDictionary(g => g.Key, g => g.Count());
        logger.LogInformation("Category option observations for {CategoryId}: {ViolatingVariantCount} variants, reason counts {ReasonCounts}",
            categoryId, violations.Length, reasons);
    }
}
