using YttStudio.App;

namespace YttStudio.App.Tests;

public sealed class AppVersionTests
{
    [Fact]
    public void BuildMetadataIsNotShownToTheUser()
    {
        Assert.Equal("0.1.0", AppVersion.Resolve("0.1.0+9a3f21c", new Version(1, 0, 0)));
    }

    [Fact]
    public void InformationalVersionWinsOverAssemblyVersion()
    {
        Assert.Equal("2.3.4", AppVersion.Resolve("2.3.4", new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+onlymetadata")]
    public void AssemblyVersionIsTheFallback(string? informationalVersion)
    {
        Assert.Equal("1.2.3", AppVersion.Resolve(informationalVersion, new Version(1, 2, 3, 4)));
    }

    [Fact]
    public void MissingVersionsDegradeToZero()
    {
        Assert.Equal("0.0.0", AppVersion.Resolve(null, null));
    }

    [Fact]
    public void TheRunningAssemblyReportsAVersion()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", AppVersion.Current);
    }
}
