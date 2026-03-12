using RcloneMountManager.Core.Models;

namespace RcloneMountManager.Tests.Models;

public class RcloneBackendOptionTests
{
  [Fact]
  public void EditableComboBox_SelectedEnumValue_SyncsToValue()
  {
    RcloneBackendOption option = new()
    {
      Name = "url",
      Type = "string",
      Examples = ["https://example.com", "https://other.com"],
    };
    RcloneBackendOptionInput input = new(option);

    input.SelectedEnumValue = "https://example.com";

    Assert.Equal("https://example.com", input.Value);
  }

  [Fact]
  public void EditableComboBox_FreeTextValue_SyncsToValue()
  {
    RcloneBackendOption option = new()
    {
      Name = "url",
      Type = "string",
      Examples = ["https://example.com", "https://other.com"],
    };
    RcloneBackendOptionInput input = new(option);

    input.SelectedEnumValue = "https://custom.example.org";

    Assert.Equal("https://custom.example.org", input.Value);
  }

  [Fact]
  public void EditableComboBox_SetValue_SyncsToSelectedEnumValue()
  {
    RcloneBackendOption option = new()
    {
      Name = "url",
      Type = "string",
      Examples = ["https://example.com", "https://other.com"],
    };
    RcloneBackendOptionInput input = new(option);

    input.Value = "https://example.com";

    Assert.Equal("https://example.com", input.SelectedEnumValue);
  }

  [Fact]
  public void EditableComboBox_WithDefaultStr_InitializesToDefault()
  {
    RcloneBackendOption option = new()
    {
      Name = "url",
      Type = "string",
      DefaultStr = "https://default.com",
      Examples = ["https://example.com", "https://other.com"],
    };
    RcloneBackendOptionInput input = new(option);

    Assert.Equal("https://default.com", input.SelectedEnumValue);
  }

  [Fact]
  public void EditableComboBox_EnumValues_ReturnsExamples()
  {
    RcloneBackendOption option = new()
    {
      Name = "url",
      Type = "string",
      Examples = ["https://example.com", "https://other.com"],
    };
    RcloneBackendOptionInput input = new(option);

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
    RcloneBackendOption option = new() {Type = type};

    Assert.Equal(OptionControlType.StringList, option.GetControlType());
  }

  [Fact]
  public void IsKeyValue_ForHeaders_ReturnsTrue()
  {
    RcloneBackendOption option = new() {Name = "headers"};

    Assert.True(option.IsKeyValue);
  }

  [Fact]
  public void IsKeyValue_ForNonHeaders_ReturnsFalse()
  {
    RcloneBackendOption option = new() {Name = "user_agent"};

    Assert.False(option.IsKeyValue);
  }

  [Fact]
  public void GetControlType_ForBoolWithExamples_ReturnsToggle()
  {
    RcloneBackendOption option = new()
    {
      Type = "bool",
      Examples = ["true", "false"],
    };

    Assert.Equal(OptionControlType.Toggle, option.GetControlType());
  }

  [Fact]
  public void GetEnumValues_ForBoolWithExamples_ReturnsNull()
  {
    RcloneBackendOption option = new()
    {
      Type = "bool",
      Examples = ["true", "false"],
    };

    Assert.Null(option.GetEnumValues());
  }

  [Fact]
  public void GetControlType_ForStringWithExamples_ReturnsEditableComboBox()
  {
    RcloneBackendOption option = new()
    {
      Name = "url",
      Type = "string",
      Examples = ["https://example.com", "https://other.com"],
    };

    Assert.Equal(OptionControlType.EditableComboBox, option.GetControlType());
  }
}