using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using YttStudio.Core;
using YttStudio.Core.Editing;
using YttStudio.Core.Format;
using YttStudio.Core.Project;
using YttStudio.Core.Validation;
using YttStudio.Render;
using YttStudio.Video;
using SubtitleRenderOptions = YttStudio.Render.RenderOptions;

namespace YttStudio.App;

public sealed partial class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{

    /// <summary>검색과 치환에 쓰는 패턴을 가져온다.</summary>
    public string SearchPattern
    {
        get => searchPattern;
        set
        {
            if (searchPattern == value)
            {
                return;
            }

            searchPattern = value;
            OnPropertyChanged();
            ReplaceAllCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary><see cref="ReplaceAllCommand"/> 가 적용할 치환 텍스트를 가져온다.</summary>
    public string ReplacementText
    {
        get => replacementText;
        set
        {
            if (replacementText == value)
            {
                return;
            }

            replacementText = value;
            OnPropertyChanged();
        }
    }

    /// <summary><see cref="SearchPattern"/> 을 정규식으로 다루는지 가져온다.</summary>
    public bool UseRegex
    {
        get => useRegex;
        set
        {
            if (useRegex == value)
            {
                return;
            }

            useRegex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>검색이 대소문자를 구분하는지 가져온다.</summary>
    public bool MatchCase
    {
        get => matchCase;
        set
        {
            if (matchCase == value)
            {
                return;
            }

            matchCase = value;
            OnPropertyChanged();
        }
    }

    /// <summary>편집기를 거쳐 큐 텍스트의 모든 일치를 치환해 되돌릴 수 있게 한다.</summary>
    private void ReplaceAll()
    {
        if (editor is null || string.IsNullOrEmpty(searchPattern))
        {
            return;
        }

        try
        {
            TextSearchOptions options = new()
            {
                UseRegex = useRegex,
                CaseSensitive = matchCase,
            };
            int replaced = editor.ReplaceText(searchPattern, replacementText, options);
            Status = $"{Loc["ReplaceAll"]}: {replaced}";
            AfterMutation(refreshRows: true);
        }
        catch (ArgumentException exception)
        {
            // 잘못된 정규식은 사용자 입력이지 크래시 사유가 아니다.
            Status = $"{Loc["UseRegex"]} — {exception.Message}";
        }
    }
}
