using Avalonia.Controls;

namespace YttStudio.App;

internal sealed class CueTableGrid : Grid
{
    public CueTableGrid()
    {
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(40)));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(104)));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(104)));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(88)));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(64)));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(120)));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
    }
}
