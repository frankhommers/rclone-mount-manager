using RcloneMountManager.Core.Models;

namespace RcloneMountManager.Tests.Models;

public class RcloneBackendOptionTests
{
    [Theory]
    [InlineData("CommaSepList")]
    [InlineData("SpaceSepList")]
    [InlineData("stringArray")]
    public void GetControlType_ForListTypes_ReturnsStringList(string type)
    {
        var option = new RcloneBackendOption { Type = type };

        Assert.Equal(OptionControlType.StringList, option.GetControlType());
    }

    [Fact]
    public void IsKeyValue_ForHeaders_ReturnsTrue()
    {
        var option = new RcloneBackendOption { Name = "headers" };

        Assert.True(option.IsKeyValue);
    }

    [Fact]
    public void IsKeyValue_ForNonHeaders_ReturnsFalse()
    {
        var option = new RcloneBackendOption { Name = "user_agent" };

        Assert.False(option.IsKeyValue);
    }

    [Fact]
    public void GetControlType_ForBoolWithExamples_ReturnsToggle()
    {
        var option = new RcloneBackendOption
        {
            Type = "bool",
            Examples = ["true", "false"],
        };

        Assert.Equal(OptionControlType.Toggle, option.GetControlType());
    }

    [Fact]
    public void GetEnumValues_ForBoolWithExamples_ReturnsNull()
    {
        var option = new RcloneBackendOption
        {
            Type = "bool",
            Examples = ["true", "false"],
        };

        Assert.Null(option.GetEnumValues());
    }
}
