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
  [InlineData("stringArray", OptionControlType.StringList)]
  public void GetControlType_ReturnsCorrectType(string rcloneType, OptionControlType expected)
  {
    RcloneOption option = new() {Type = rcloneType};
    Assert.Equal(expected, option.GetControlType());
  }

  [Fact]
  public void GetEnumValues_ForBool_ReturnsNull()
  {
    RcloneOption option = new() {Type = "bool"};
    Assert.Null(option.GetEnumValues());
  }

  [Fact]
  public void GetEnumValues_ForCacheMode_ReturnsFourValues()
  {
    RcloneOption option = new() {Type = "CacheMode"};
    IReadOnlyList<string>? values = option.GetEnumValues();
    Assert.NotNull(values);
    Assert.Equal(4, values.Count);
    Assert.Contains("full", values);
  }

  [Fact]
  public void GetEnumValues_ForTristate_ReturnsThreeValues()
  {
    RcloneOption option = new() {Type = "Tristate"};
    IReadOnlyList<string>? values = option.GetEnumValues();
    Assert.NotNull(values);
    Assert.Equal(3, values.Count);
  }

  [Fact]
  public void GetEnumValues_ForString_ReturnsNull()
  {
    RcloneOption option = new() {Type = "string"};
    Assert.Null(option.GetEnumValues());
  }
}