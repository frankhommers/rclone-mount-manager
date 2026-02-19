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
        if (param is not MountOptionInputViewModel vm)
            return null;

        var key = vm.ControlType switch
        {
            OptionControlType.Toggle => "Toggle",
            OptionControlType.ComboBox => "ComboBox",
            OptionControlType.Numeric => "Numeric",
            OptionControlType.Duration => "Duration",
            OptionControlType.SizeSuffix => "SizeSuffix",
            _ => "Text",
        };

        return Templates.TryGetValue(key, out var template)
            ? template.Build(param)
            : null;
    }

    public bool Match(object? data) => data is MountOptionInputViewModel;
}
