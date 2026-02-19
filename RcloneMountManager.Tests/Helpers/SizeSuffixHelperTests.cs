using RcloneMountManager.Core.Helpers;

namespace RcloneMountManager.Tests.Helpers;

public class SizeSuffixHelperTests
{
    [Theory]
    [InlineData("128Mi", 128, "Mi")]
    [InlineData("1Gi", 1, "Gi")]
    [InlineData("256Ki", 256, "Ki")]
    [InlineData("2Ti", 2, "Ti")]
    [InlineData("1024", 1024, "B")]
    [InlineData("0", 0, "B")]
    [InlineData("", 0, "B")]
    [InlineData("off", 0, "B")]
    public void Parse_ValidSuffix_ReturnsComponents(string input, decimal expectedValue, string expectedUnit)
    {
        var (value, unit) = SizeSuffixHelper.Parse(input);
        Assert.Equal(expectedValue, value);
        Assert.Equal(expectedUnit, unit);
    }

    [Theory]
    [InlineData(128, "Mi", "128Mi")]
    [InlineData(1, "Gi", "1Gi")]
    [InlineData(256, "Ki", "256Ki")]
    [InlineData(0, "B", "0")]
    [InlineData(1024, "B", "1024")]
    public void Format_Components_ReturnsRcloneString(decimal value, string unit, string expected)
    {
        Assert.Equal(expected, SizeSuffixHelper.Format(value, unit));
    }

    [Fact]
    public void Parse_Null_ReturnsZeroBytes()
    {
        var (value, unit) = SizeSuffixHelper.Parse(null);
        Assert.Equal(0m, value);
        Assert.Equal("B", unit);
    }

    [Fact]
    public void Units_ContainsAllExpected()
    {
        var units = SizeSuffixHelper.Units;
        Assert.Equal(5, units.Count);
        Assert.Contains("B", units);
        Assert.Contains("Ki", units);
        Assert.Contains("Mi", units);
        Assert.Contains("Gi", units);
        Assert.Contains("Ti", units);
    }
}
