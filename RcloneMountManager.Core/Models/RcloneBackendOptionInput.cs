using RcloneMountManager.Core.ViewModels;

namespace RcloneMountManager.Core.Models;

public partial class RcloneBackendOptionInput : TypedOptionViewModel
{
    private readonly RcloneBackendOption _option;

    public RcloneBackendOptionInput() : this(new RcloneBackendOption()) { }

    public RcloneBackendOptionInput(RcloneBackendOption option)
    {
        _option = option;
        InitializeTypedValues(null);
    }

    protected override IRcloneOptionDefinition Option => _option;

    public bool Required => _option.Required;

    public override string Label
    {
        get
        {
            var required = Required ? "required" : "optional";
            var secret = IsPassword ? ", secret" : string.Empty;
            return $"{Name} ({required}{secret})";
        }
    }
}
