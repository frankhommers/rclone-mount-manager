using RcloneMountManager.Core.Services;
using System.Linq;

namespace RcloneMountManager.Tests.Services;

public class RcloneOptionsServiceTests
{
    private const string SampleJson = """
    {
      "mount": [
        {
          "Name": "debug_fuse",
          "Help": "Debug the FUSE internals",
          "Type": "bool",
          "Default": false,
          "DefaultStr": "false",
          "Advanced": false,
          "Required": false,
          "IsPassword": false
        },
        {
          "Name": "attr_timeout",
          "Help": "Time for which attributes are cached",
          "Type": "Duration",
          "Default": 1000000000,
          "DefaultStr": "1s",
          "Advanced": false,
          "Required": false,
          "IsPassword": false
        }
      ],
      "vfs": [
        {
          "Name": "vfs_cache_mode",
          "Help": "Cache mode off|minimal|writes|full",
          "Type": "CacheMode",
          "Default": 0,
          "DefaultStr": "off",
          "Advanced": false,
          "Required": false,
          "IsPassword": false
        }
      ],
      "nfs": [
        {
          "Name": "nfs_cache_type",
          "Help": "NFS cache type",
          "Type": "memory|disk|symlink",
          "Default": "memory",
          "DefaultStr": "memory",
          "Advanced": false,
          "Required": false,
          "IsPassword": false
        }
      ]
    }
    """;

    private const string SampleJsonWithRc = """
    {
      "mount": [
        { "Name": "debug_fuse", "Help": "Debug", "Type": "bool", "DefaultStr": "false" }
      ],
      "rc": [
        { "Name": "rc", "Help": "Enable RC", "Type": "bool", "DefaultStr": "false", "Advanced": false },
        { "Name": "rc_addr", "Help": "RC address", "Type": "stringArray", "DefaultStr": "localhost:5572", "Advanced": false },
        { "Name": "rc_user", "Help": "RC user", "Type": "string", "DefaultStr": "", "Advanced": false },
        { "Name": "rc_pass", "Help": "RC pass", "Type": "string", "DefaultStr": "", "Advanced": false, "IsPassword": true },
        { "Name": "rc_no_auth", "Help": "No auth", "Type": "bool", "DefaultStr": "false", "Advanced": false },
        { "Name": "rc_cert", "Help": "TLS cert", "Type": "string", "DefaultStr": "", "Advanced": false },
        { "Name": "rc_web_gui", "Help": "Web GUI", "Type": "bool", "DefaultStr": "false", "Advanced": false },
        { "Name": "metrics_addr", "Help": "Metrics addr", "Type": "stringArray", "DefaultStr": "", "Advanced": false }
      ]
    }
    """;

    [Fact]
    public void ParseOptionsJson_IncludesRcGroup()
    {
        var groups = RcloneOptionsService.ParseOptionsJson(SampleJsonWithRc);
        var rc = groups.FirstOrDefault(g => g.Name == "rc");
        Assert.NotNull(rc);
        Assert.Equal("Remote Control", rc.DisplayName);
    }

    [Fact]
    public void ParseOptionsJson_RcGroup_ExcludesMetricsOptions()
    {
        var groups = RcloneOptionsService.ParseOptionsJson(SampleJsonWithRc);
        var rc = groups.First(g => g.Name == "rc");
        Assert.DoesNotContain(rc.Options, o => o.Name.StartsWith("metrics_"));
    }

    [Fact]
    public void ParseOptionsJson_RcGroup_BasicOptionsNotAdvanced()
    {
        var groups = RcloneOptionsService.ParseOptionsJson(SampleJsonWithRc);
        var rc = groups.First(g => g.Name == "rc");
        var basicNames = new[] { "rc", "rc_addr", "rc_user", "rc_pass", "rc_no_auth" };
        foreach (var name in basicNames)
        {
            var opt = rc.Options.FirstOrDefault(o => o.Name == name);
            Assert.NotNull(opt);
            Assert.False(opt.Advanced, $"{name} should be basic");
        }
    }

    [Fact]
    public void ParseOptionsJson_RcGroup_NonBasicOptionsAreAdvanced()
    {
        var groups = RcloneOptionsService.ParseOptionsJson(SampleJsonWithRc);
        var rc = groups.First(g => g.Name == "rc");
        var cert = rc.Options.First(o => o.Name == "rc_cert");
        Assert.True(cert.Advanced);
        var webGui = rc.Options.First(o => o.Name == "rc_web_gui");
        Assert.True(webGui.Advanced);
    }

    [Fact]
    public void ParseOptionsJson_ReturnsCorrectGroups()
    {
        var groups = RcloneOptionsService.ParseOptionsJson(SampleJson);
        Assert.Equal(3, groups.Count);
        Assert.Equal("mount", groups[0].Name);
        Assert.Equal("Mount", groups[0].DisplayName);
        Assert.Equal("vfs", groups[1].Name);
        Assert.Equal("nfs", groups[2].Name);
    }

    [Fact]
    public void ParseOptionsJson_MountGroup_HasCorrectOptions()
    {
        var groups = RcloneOptionsService.ParseOptionsJson(SampleJson);
        var mount = groups[0];
        Assert.Equal(2, mount.Options.Count);
        Assert.Equal("debug_fuse", mount.Options[0].Name);
        Assert.Equal("bool", mount.Options[0].Type);
        Assert.Equal("false", mount.Options[0].DefaultStr);
        Assert.Equal("attr_timeout", mount.Options[1].Name);
        Assert.Equal("Duration", mount.Options[1].Type);
    }

    [Fact]
    public void ParseOptionsJson_CacheMode_IsEnum()
    {
        var groups = RcloneOptionsService.ParseOptionsJson(SampleJson);
        var vfs = groups[1];
        var cacheMode = vfs.Options[0];
        var enumValues = cacheMode.GetEnumValues();
        Assert.NotNull(enumValues);
        Assert.Equal(["off", "minimal", "writes", "full"], enumValues);
    }

    [Fact]
    public void ParseOptionsJson_PipeSeparatedEnum_IsParsed()
    {
        var groups = RcloneOptionsService.ParseOptionsJson(SampleJson);
        var nfs = groups[2];
        var cacheType = nfs.Options[0];
        var enumValues = cacheType.GetEnumValues();
        Assert.NotNull(enumValues);
        Assert.Equal(["memory", "disk", "symlink"], enumValues);
    }

    [Fact]
    public void ParseOptionsJson_SkipsMissingGroups()
    {
        var json = """{ "mount": [] }""";
        var groups = RcloneOptionsService.ParseOptionsJson(json);
        Assert.Single(groups);
        Assert.Equal("mount", groups[0].Name);
    }

    [Fact]
    public void ParseOptionsJson_SkipsEmptyNames()
    {
        var json = """
        {
          "mount": [
            { "Name": "", "Help": "test", "Type": "bool", "DefaultStr": "false" },
            { "Name": "valid", "Help": "test", "Type": "bool", "DefaultStr": "false" }
          ]
        }
        """;
        var groups = RcloneOptionsService.ParseOptionsJson(json);
        Assert.Single(groups[0].Options);
        Assert.Equal("valid", groups[0].Options[0].Name);
    }
}
