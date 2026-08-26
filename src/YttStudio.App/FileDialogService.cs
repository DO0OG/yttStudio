using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Layout;

namespace YttStudio.App;

public interface IFileDialogService
{
    Task<string?> OpenSubtitleAsync();
    Task<string?> OpenVideoAsync();
    Task<string?> SaveYttAsync(string? suggestedName);
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "삭제");

    /// <summary>열 <c>.yttproj</c> 패키지를 고른다.</summary>
    Task<string?> OpenProjectAsync();

    /// <summary><c>.yttproj</c> 패키지를 저장할 위치를 고른다.</summary>
    Task<string?> SaveProjectAsync(string? suggestedName);

    /// <summary>사용자가 libmpv 네이티브 라이브러리 파일 또는 포함 폴더를 선택하게 한다.</summary>
    Task<string?> OpenMpvLibraryAsync();

    /// <summary>
    /// 기록된 영상 경로를 더 이상 찾을 수 없는 프로젝트를 다시 연결한다.
    /// 패키지는 경로만 저장하므로 끊어진 연결을 복구할 수 있어야 한다.
    /// </summary>
    Task<string?> RelinkVideoAsync(string missingPath);
}

public sealed class FileDialogService : IFileDialogService
{
    private readonly Window owner;

    public FileDialogService(Window owner)
    {
        this.owner = owner;
    }

    public async Task<string?> OpenSubtitleAsync()
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "자막 열기",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("YTT / ASS 자막") { Patterns = ["*.ytt", "*.srv3", "*.ass"] },
            ],
        });
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    public async Task<string?> OpenVideoAsync()
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "영상 열기",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("영상")
                {
                    Patterns = ["*.mp4", "*.mkv", "*.webm", "*.mov", "*.avi", "*.m4v"],
                },
            ],
        });
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    public async Task<string?> SaveYttAsync(string? suggestedName)
    {
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "YTT 저장",
            SuggestedFileName = suggestedName,
            DefaultExtension = "ytt",
            FileTypeChoices = [new FilePickerFileType("YouTube timed text") { Patterns = ["*.ytt"] }],
        });
        return file?.Path.LocalPath;
    }

    public async Task<string?> OpenProjectAsync()
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "프로젝트 열기",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("YttStudio 프로젝트") { Patterns = ["*.yttproj"] }],
        });
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    public async Task<string?> SaveProjectAsync(string? suggestedName)
    {
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "프로젝트 저장",
            SuggestedFileName = suggestedName,
            DefaultExtension = "yttproj",
            FileTypeChoices = [new FilePickerFileType("YttStudio 프로젝트") { Patterns = ["*.yttproj"] }],
        });
        return file?.Path.LocalPath;
    }

    public async Task<string?> OpenMpvLibraryAsync()
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "libmpv library",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("libmpv native library")
                {
                    Patterns = ["*.dll", "*.so", "*.so.*", "*.dylib"],
                },
            ],
        });
        if (files.Count > 0)
        {
            return files[0].Path.LocalPath;
        }

        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "libmpv folder",
                AllowMultiple = false,
            });
        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    public async Task<string?> RelinkVideoAsync(string missingPath)
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"영상을 찾을 수 없습니다 — 다시 찾기: {Path.GetFileName(missingPath)}",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("영상")
                {
                    Patterns = ["*.mp4", "*.mkv", "*.webm", "*.mov", "*.avi", "*.m4v"],
                },
            ],
        });
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "삭제")
    {
        Window dialog = new()
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        Button cancel = new() { Content = "취소", MinWidth = 80 };
        Button confirm = new() { Content = confirmLabel, MinWidth = 80 };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, confirm },
                },
            },
        };
        return dialog.ShowDialog<bool>(owner);
    }
}
