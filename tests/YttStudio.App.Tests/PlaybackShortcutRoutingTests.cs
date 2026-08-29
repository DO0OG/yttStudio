using Avalonia.Input;

namespace YttStudio.App.Tests;

public sealed class PlaybackShortcutRoutingTests
{
    [Fact]
    public void PlainSpaceTogglesPlaybackWhenNothingClaimsTheKey()
    {
        Assert.True(MainWindow.ShouldTogglePlayback(
            Key.Space,
            KeyModifiers.None,
            shortcutBlocked: false,
            canTogglePlayback: true));
    }

    [Fact]
    public void SpaceIsLeftAloneWhereItAlreadyHasAJob()
    {
        Assert.False(MainWindow.ShouldTogglePlayback(
            Key.Space,
            KeyModifiers.None,
            shortcutBlocked: true,
            canTogglePlayback: true));
    }

    [Fact]
    public void SpaceIsNotSwallowedWhileThereIsNothingToPlay()
    {
        Assert.False(MainWindow.ShouldTogglePlayback(
            Key.Space,
            KeyModifiers.None,
            shortcutBlocked: false,
            canTogglePlayback: false));
    }

    [Theory]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Alt)]
    public void ModifiedSpaceIsNotRouted(KeyModifiers modifiers)
    {
        Assert.False(MainWindow.ShouldTogglePlayback(
            Key.Space,
            modifiers,
            shortcutBlocked: false,
            canTogglePlayback: true));
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.K)]
    [InlineData(Key.Delete)]
    public void OtherKeysAreNotRouted(Key key)
    {
        Assert.False(MainWindow.ShouldTogglePlayback(
            key,
            KeyModifiers.None,
            shortcutBlocked: false,
            canTogglePlayback: true));
    }
}
