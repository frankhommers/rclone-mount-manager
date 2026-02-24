using RcloneMountManager.Core.Services;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.Tests.Services;

public sealed class RcloneRcClientTests
{
    [Fact]
    public async Task GetPidAsync_ReturnsNull_WhenConnectionRefused()
    {
        RcloneRcClient client = new(new HttpClient());
        int? result = await client.GetPidAsync(59999, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task IsAliveAsync_ReturnsFalse_WhenConnectionRefused()
    {
        RcloneRcClient client = new(new HttpClient());
        bool alive = await client.IsAliveAsync(59999, CancellationToken.None);
        Assert.False(alive);
    }

    [Fact]
    public async Task QuitAsync_ReturnsFalse_WhenConnectionRefused()
    {
        RcloneRcClient client = new(new HttpClient());
        bool result = await client.QuitAsync(59999, CancellationToken.None);
        Assert.False(result);
    }
}
