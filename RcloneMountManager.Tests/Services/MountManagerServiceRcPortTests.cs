using RcloneMountManager.Core.Services;

namespace RcloneMountManager.Tests.Services;

public class MountManagerServiceRcPortTests
{
  [Fact]
  public void AssignRcPort_ReturnsPortInRange()
  {
    int port = MountManagerService.AssignRcPort("abc123def456");
    Assert.InRange(port, 50000, 59999);
  }

  [Fact]
  public void AssignRcPort_SameIdReturnsSamePort()
  {
    int port1 = MountManagerService.AssignRcPort("abc123def456");
    int port2 = MountManagerService.AssignRcPort("abc123def456");
    Assert.Equal(port1, port2);
  }

  [Fact]
  public void AssignRcPort_DifferentIdsReturnDifferentPorts()
  {
    int port1 = MountManagerService.AssignRcPort("profile1");
    int port2 = MountManagerService.AssignRcPort("profile2");
    Assert.NotEqual(port1, port2);
  }
}