using RcloneMountManager.Core.Models;

namespace RcloneMountManager.Tests.Models;

public class RcloneBackendOptionTests
{
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
