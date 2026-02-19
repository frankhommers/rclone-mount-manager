using RcloneMountManager.Core.Models;

namespace RcloneMountManager.Tests.Models;

public class RcloneOptionTests
{
    [Theory]
    [InlineData("bool", OptionControlType.Toggle)]
    [InlineData("int", OptionControlType.Numeric)]
    [InlineData("int64", OptionControlType.Numeric)]
    [InlineData("uint32", OptionControlType.Numeric)]
    [InlineData("float64", OptionControlType.Numeric)]
    [InlineData("string", OptionControlType.Text)]
    [InlineData("Duration", OptionControlType.Duration)]
    [InlineData("SizeSuffix", OptionControlType.SizeSuffix)]
    [InlineData("CacheMode", OptionControlType.ComboBox)]
    [InlineData("memory|disk|symlink", OptionControlType.ComboBox)]
    [InlineData("stringArray", OptionControlType.Text)]
    public void GetControlType_ReturnsCorrectType(string rcloneType, OptionControlType expected)
    {
        var option = new RcloneOption { Type = rcloneType };
        Assert.Equal(expected, option.GetControlType());
    }

    [Fact]
    public void GetEnumValues_ForBool_ReturnsNull()
    {
        var option = new RcloneOption { Type = "bool" };
        Assert.Null(option.GetEnumValues());
    }

    [Fact]
    public void GetEnumValues_ForCacheMode_ReturnsFourValues()
    {
        var option = new RcloneOption { Type = "CacheMode" };
        var values = option.GetEnumValues();
        Assert.NotNull(values);
        Assert.Equal(4, values.Count);
        Assert.Contains("full", values);
    }

    [Fact]
    public void GetEnumValues_ForTristate_ReturnsThreeValues()
    {
        var option = new RcloneOption { Type = "Tristate" };
        var values = option.GetEnumValues();
        Assert.NotNull(values);
        Assert.Equal(3, values.Count);
    }

    [Fact]
    public void GetEnumValues_ForString_ReturnsNull()
    {
        var option = new RcloneOption { Type = "string" };
        Assert.Null(option.GetEnumValues());
    }
}
