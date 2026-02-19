namespace RcloneMountManager.Models;

public sealed class RcloneBackendOption
{
    public string Name { get; set; } = string.Empty;
    public string Help { get; set; } = string.Empty;
    public bool Required { get; set; }
    public bool IsPassword { get; set; }
    public bool Advanced { get; set; }
}
