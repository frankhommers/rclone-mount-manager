using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using RcloneMountManager.Core.Models;
using RcloneMountManager.ViewModels;
using System.Collections.Generic;

namespace RcloneMountManager.Controls;

public class OptionControlTemplateSelector : IDataTemplate
{
    [Content]
    public Dictionary<string, IDataTemplate> Templates { get; } = new();

    public Control? Build(object? param)
    {
        var key = param switch
        {
            MountOptionInputViewModel vm => GetKey(vm.ControlType),
            RcloneBackendOptionInput bi => GetKey(bi.ControlType),
            _ => null,
        };

        if (key is null) return null;

        return Templates.TryGetValue(key, out var template)
            ? template.Build(param)
            : null;
    }

    public bool Match(object? data) =>
        data is MountOptionInputViewModel or RcloneBackendOptionInput;

    private static string GetKey(OptionControlType controlType) => controlType switch
    {
        OptionControlType.Toggle => "Toggle",
        OptionControlType.ComboBox => "ComboBox",
        OptionControlType.Numeric => "Numeric",
        OptionControlType.Duration => "Duration",
        OptionControlType.SizeSuffix => "SizeSuffix",
        _ => "Text",
    };
}
