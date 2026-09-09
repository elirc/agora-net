using Agora.Domain.Common;
using Agora.Domain.Services;

namespace Agora.Tests.Unit;

public class CategoryOptionSchemaRulesTests
{
    [Fact]
    public void Normalize_canonicalizes_keys_values_and_order_without_changing_value_case()
    {
        var rules = CategoryOptionSchemaRules.Normalize([
            new(" Color ", false, [" blue ", "Red"]),
            new("SIZE", true, ["S", "L", "M"]),
        ]);

        Assert.Equal(["color", "size"], rules.Select(r => r.Key));
        Assert.Equal(["Red", "blue"], rules[0].AllowedValues);
        Assert.Equal(["L", "M", "S"], rules[1].AllowedValues);
        Assert.True(rules[1].Required);
    }

    [Fact]
    public void Validate_reports_required_unknown_bad_value_and_non_ascii_keys_in_stable_order()
    {
        var rules = CategoryOptionSchemaRules.Normalize([
            new("size", true, ["S", "M", "L"]),
            new("color", false, ["Red", "Blue"]),
        ]);
        var violations = CategoryOptionSchemaRules.Validate(rules, new Dictionary<string, string>
        {
            ["material"] = "cotton",
            ["color"] = "red",
            ["\u212Aey"] = "secret",
        });

        Assert.Equal(
            [("color", "ValueNotAllowed"), ("material", "UnknownKey"), ("size", "RequiredKeyMissing"), ("\u212Aey", "InvalidKey")],
            violations.Select(v => (v.Key, v.Reason)));
        Assert.DoesNotContain(violations, v => v.ActualValue?.Length > 80 || v.Key.Length > 40);
    }

    [Theory]
    [MemberData(nameof(InvalidRules))]
    public void Normalize_rejects_ambiguous_or_unbounded_rules(IReadOnlyList<CategoryOptionRule> rules)
        => Assert.Throws<DomainException>(() => CategoryOptionSchemaRules.Normalize(rules));

    public static TheoryData<IReadOnlyList<CategoryOptionRule>> InvalidRules() => new()
    {
        { [new("size", true, ["M"]), new(" SIZE ", false, ["L"])] },
        { [new("siz\u212A", true, ["M"])] },
        { [new("size", true, [" M ", "M"])] },
        { [new("size", true, [])] },
        { [new(new string('k', 41), true, ["M"])] },
        { [new("size", true, [new string('v', 81)])] },
        { Enumerable.Range(0, 11).Select(i => new CategoryOptionRule("key" + i, false, ["v"])).ToArray() },
        { [new("size", true, Enumerable.Range(0, 51).Select(i => "v" + i).ToArray())] },
    };

    [Fact]
    public void Validate_is_ordinal_for_values_and_accepts_a_complete_valid_dictionary()
    {
        var rules = CategoryOptionSchemaRules.Normalize([new("size", true, ["M", "m"])]);
        Assert.Empty(CategoryOptionSchemaRules.Validate(rules, new Dictionary<string, string> { [" SIZE "] = " M " }));
        Assert.Empty(CategoryOptionSchemaRules.Validate(rules, new Dictionary<string, string> { ["size"] = "m" }));
        Assert.Single(CategoryOptionSchemaRules.Validate(rules, new Dictionary<string, string> { ["size"] = "medium" }),
            v => v.Reason == "ValueNotAllowed");
    }
}
