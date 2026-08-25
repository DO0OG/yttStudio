using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace YttStudio.App;

public interface IFileDialogService
{
    Task<string?> OpenSubtitleAsync();
    Task<string?> SaveYttAsync(string? suggestedName);
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
}
