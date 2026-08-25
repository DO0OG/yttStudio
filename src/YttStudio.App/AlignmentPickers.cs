using Avalonia.Controls;
using Avalonia.Layout;
using YttStudio.Core;

namespace YttStudio.App;

public sealed class AnchorPicker : Grid
{
    public AnchorPicker()
    {
        RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
        ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto");
        for (int index = 0; index < 9; index++)
        {
            AnchorPoint anchor = (AnchorPoint)index;
            Button button = new()
            {
                Content = new[] { "↖", "↑", "↗", "←", "✛", "→", "↙", "↓", "↘" }[index],
                Width = 34,
                Height = 30,
                Margin = new Avalonia.Thickness(1),
            };
            button.Click += (_, _) => (DataContext as MainWindowViewModel)?.ApplySelectedAnchor(anchor);
            SetRow(button, index / 3);
            SetColumn(button, index % 3);
            Children.Add(button);
        }
    }
}

public sealed class JustificationPicker : StackPanel
{
    public JustificationPicker()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 4;
        Add("왼쪽", Justification.Left);
        Add("가운데", Justification.Center);
        Add("오른쪽", Justification.Right);
    }

    private void Add(string label, Justification justification)
    {
        Button button = new() { Content = label, MinWidth = 58 };
        button.Click += (_, _) => (DataContext as MainWindowViewModel)?.ApplySelectedJustification(justification);
        Children.Add(button);
    }
}
