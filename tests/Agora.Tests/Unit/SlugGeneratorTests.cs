using Agora.Domain.Common;

namespace Agora.Tests.Unit;

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("Classic Cotton Tee", "classic-cotton-tee")]
    [InlineData("  Volt 65W GaN Charger  ", "volt-65w-gan-charger")]
    [InlineData("Home & Kitchen", "home-kitchen")]
    [InlineData("--Weird---Name--", "weird-name")]
    public void FromName_ProducesCleanSlug(string name, string expected)
    {
        Assert.Equal(expected, SlugGenerator.FromName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void FromName_Unsluggable_Throws(string name)
    {
        Assert.Throws<DomainException>(() => SlugGenerator.FromName(name));
    }
}
