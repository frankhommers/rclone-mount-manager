using RcloneMountManager.Core.Helpers;

namespace RcloneMountManager.Tests.Helpers;

public class DurationHelperTests
{
  [Theory]
  [InlineData("5m", 0, 5, 0)]
  [InlineData("1h", 1, 0, 0)]
  [InlineData("1h30m", 1, 30, 0)]
  [InlineData("10s", 0, 0, 10)]
  [InlineData("2h5m30s", 2, 5, 30)]
  [InlineData("90s", 0, 1, 30)]
  [InlineData("0", 0, 0, 0)]
  [InlineData("0s", 0, 0, 0)]
  [InlineData("", 0, 0, 0)]
  public void Parse_ValidDuration_ReturnsTimeSpan(string input, int hours, int minutes, int seconds)
  {
    TimeSpan result = DurationHelper.Parse(input);
    Assert.Equal(new TimeSpan(hours, minutes, seconds), result);
  }

  [Theory]
  [InlineData(0, 5, 0, "5m")]
  [InlineData(1, 0, 0, "1h")]
  [InlineData(1, 30, 0, "1h30m")]
  [InlineData(0, 0, 10, "10s")]
  [InlineData(2, 5, 30, "2h5m30s")]
  [InlineData(0, 0, 0, "0s")]
  public void Format_TimeSpan_ReturnsRcloneString(int hours, int minutes, int seconds, string expected)
  {
    TimeSpan ts = new(hours, minutes, seconds);
    Assert.Equal(expected, DurationHelper.Format(ts));
  }

  [Fact]
  public void Parse_Null_ReturnsZero()
  {
    Assert.Equal(TimeSpan.Zero, DurationHelper.Parse(null));
  }
}