using System.ComponentModel;

namespace RcloneMountManager.Core.Models;

public enum MountType
{
    [Description("Rclone (auto)")]
    RcloneAuto,

    [Description("Rclone FUSE")]
    RcloneFuse,

    [Description("Rclone NFS")]
    RcloneNfs,

    [Description("macOS NFS")]
    MacOsNfs,
}
