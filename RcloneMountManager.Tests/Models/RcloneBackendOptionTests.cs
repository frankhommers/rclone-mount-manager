using RcloneMountManager.Core.Models;

namespace RcloneMountManager.Tests.Models;

public class RcloneBackendOptionTests
{
    [Fact]
    public void EditableComboBox_SelectedEnumValue_SyncsToValue()
    {
        var option = new RcloneBackendOption
        {
            Name = "url",
            Type = "string",
            Examples = ["https://example.com", "https://other.com"],
        };
        var input = new RcloneBackendOptionInput(option);

        input.SelectedEnumValue = "https://example.com";

        Assert.Equal("https://example.com", input.Value);
    }

    [Fact]
    public void EditableComboBox_FreeTextValue_SyncsToValue()
    {
        var option = new RcloneBackendOption
        {
            Name = "url",
            Type = "string",
            Examples = ["https://example.com", "https://other.com"],
        };
        var input = new RcloneBackendOptionInput(option);

        input.SelectedEnumValue = "https://custom.example.org";

        Assert.Equal("https://custom.example.org", input.Value);
    }

    [Fact]
    public void EditableComboBox_SetValue_SyncsToSelectedEnumValue()
    {
        var option = new RcloneBackendOption
        {
            Name = "url",
            Type = "string",
            Examples = ["https://example.com", "https://other.com"],
        };
        var input = new RcloneBackendOptionInput(option);

        input.Value = "https://example.com";

        Assert.Equal("https://example.com", input.SelectedEnumValue);
    }

    [Fact]
    public void EditableComboBox_WithDefaultStr_InitializesToDefault()
    {
        var option = new RcloneBackendOption
        {
            Name = "url",
            Type = "string",
            DefaultStr = "https://default.com",
            Examples = ["https://example.com", "https://other.com"],
        };
        var input = new RcloneBackendOptionInput(option);

        Assert.Equal("https://default.com", input.SelectedEnumValue);
    }

    [Fact]
    public void EditableComboBox_EnumValues_ReturnsExamples()
    {
        var option = new RcloneBackendOption
        {
            Name = "url",
            Type = "string",
            Examples = ["https://example.com", "https://other.com"],
        };
        var input = new RcloneBackendOptionInput(option);

        Assert.NotNull(input.EnumValues);
        Assert.Equal(2, input.EnumValues!.Count);
        Assert.Contains("https://example.com", input.EnumValues);
        Assert.Contains("https://other.com", input.EnumValues);
    }

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

    [Fact]
    public void GetControlType_ForStringWithExamples_ReturnsEditableComboBox()
    {
        var option = new RcloneBackendOption
        {
            Name = "url",
            Type = "string",
            Examples = ["https://example.com", "https://other.com"],
        };

        Assert.Equal(OptionControlType.EditableComboBox, option.GetControlType());
    }
}
