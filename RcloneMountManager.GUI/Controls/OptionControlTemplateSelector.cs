using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using RcloneMountManager.Core.ViewModels;
using System.Collections.Generic;

namespace RcloneMountManager.Controls;

public class OptionControlTemplateSelector : IDataTemplate
{
    [Content]
    public Dictionary<string, IDataTemplate> Templates { get; } = new();

    public Control? Build(object? param)
    {
        if (param is not TypedOptionViewModel vm)
            return null;

        var key = vm.ControlType switch
        {
            Core.Models.OptionControlType.Toggle => "Toggle",
            Core.Models.OptionControlType.ComboBox => "ComboBox",
            Core.Models.OptionControlType.Numeric => "Numeric",
            Core.Models.OptionControlType.Duration => "Duration",
            Core.Models.OptionControlType.SizeSuffix => "SizeSuffix",
            _ => "Text",
        };

        return Templates.TryGetValue(key, out var template)
            ? template.Build(param)
            : null;
    }

    public bool Match(object? data) => data is TypedOptionViewModel;
}
