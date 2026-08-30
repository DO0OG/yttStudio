using Avalonia.Headless.XUnit;
using YttStudio.App;

namespace YttStudio.App.Tests;

public sealed class MpvAutoInstallerContractTests
{
    [Fact]
    public void WindowsPackageUsesPinnedLgplBuild()
    {
        Assert.Equal("2026-08-29-e8673660ab", MpvAutoInstaller.PinnedReleaseTag);
        Assert.Equal(
            "mpv-dev-lgpl-x86_64-20260829-git-e8673660ab.7z",
            MpvAutoInstaller.PinnedAssetName);
        Assert.Equal(
            "78260166265fbc09b3bee75ee3464eb0f6bbaa8ecd172786e33c22bbf8a3cb47",
            MpvAutoInstaller.PinnedAssetSha256);
        Assert.Equal(27_984_604, MpvAutoInstaller.PinnedAssetLength);
    }

    [Theory]
    [InlineData(MpvPackagePlatform.Windows)]
    [InlineData(MpvPackagePlatform.MacOS)]
    [InlineData(MpvPackagePlatform.Linux)]
    public void SupportedDesktopPlatformsOfferAutomaticInstallation(MpvPackagePlatform platform)
    {
        MpvPackageInstallInstructions instructions = MpvAutoInstaller.GetPackageManagerInstructions(platform);

        Assert.True(instructions.SupportsAutomaticInstallation);
    }

    [Fact]
    public void UnknownPlatformDoesNotOfferAutomaticInstallation()
    {
        MpvPackageInstallInstructions instructions = MpvAutoInstaller.GetPackageManagerInstructions(MpvPackagePlatform.Other);

        Assert.False(instructions.SupportsAutomaticInstallation);
    }

    [AvaloniaFact]
    public void VideoCommandsRemainAvailableBeforeRuntimeInstallation()
    {
        using MainWindowViewModel viewModel = new(
            new StubFileDialogService(),
            null,
            () => null,
            static (_, _) => Task.CompletedTask);

        Assert.True(viewModel.OpenVideoCommand.CanExecute(null));
        Assert.True(viewModel.OpenVideoUrlCommand.CanExecute(null));
    }

    private sealed class StubFileDialogService : IFileDialogService
    {
        public Task<string?> OpenSubtitleAsync() => Task.FromResult<string?>(null);
        public Task<string?> OpenVideoAsync() => Task.FromResult<string?>(null);
        public Task<string?> OpenVideoUrlAsync(VideoUrlDialogOptions? options = null)
            => Task.FromResult<string?>(null);
        public Task<string?> SaveYttAsync(string? suggestedName) => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "삭제")
            => Task.FromResult(false);
        public Task<string?> OpenProjectAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveProjectAsync(string? suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> OpenMpvLibraryAsync() => Task.FromResult<string?>(null);
        public Task<string?> RelinkVideoAsync(string missingPath) => Task.FromResult<string?>(null);
    }
}
