using Avalonia.Input;
using YttStudio.App;

namespace YttStudio.App.Tests;

public sealed class DeleteKeyRoutingTests
{
    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, false)]
    public void DeleteIsBlockedForTextBoxInlineEditOrOtherFocus(
        bool cueListFocused,
        bool textBoxFocused,
        bool inlineEditing,
        bool expected)
    {
        Assert.Equal(expected, MainWindow.ShouldDeleteCueFromList(
            Key.Delete,
            KeyModifiers.None,
            cueListFocused,
            textBoxFocused,
            inlineEditing));
    }

    [Fact]
    public void PlainDeleteWithCueListFocusIsRoutedToCueCommand()
    {
        Assert.True(MainWindow.ShouldDeleteCueFromList(
            Key.Delete,
            KeyModifiers.None,
            cueListFocused: true,
            textBoxFocused: false,
            inlineEditing: false));
    }

    [Fact]
    public void ModifiedDeleteIsNotRouted()
    {
        Assert.False(MainWindow.ShouldDeleteCueFromList(
            Key.Delete,
            KeyModifiers.Control,
            cueListFocused: true,
            textBoxFocused: false,
            inlineEditing: false));
    }
}
