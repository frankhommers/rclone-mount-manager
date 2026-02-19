using CommunityToolkit.Mvvm.ComponentModel;

namespace RcloneMountManager.Core.Models;

public partial class RcloneBackendOptionInput : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string Help { get; init; } = string.Empty;
    public bool Required { get; init; }
    public bool IsPassword { get; init; }

    public string Label
    {
        get
        {
            var required = Required ? "required" : "optional";
            var secret = IsPassword ? ", secret" : string.Empty;
            return $"{Name} ({required}{secret})";
        }
    }

    [ObservableProperty]
    private string _value = string.Empty;
}
