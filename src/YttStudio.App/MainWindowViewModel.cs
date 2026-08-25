using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using SkiaSharp;
using YttStudio.Core;
using YttStudio.Core.Format;
using YttStudio.Render;

namespace YttStudio.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IFileDialogService dialogs;
    private readonly SubtitleFileService fileService = new();
    private readonly SkiaSubtitleRenderer renderer;
    private readonly AsyncCommand saveCommand;
    private SubtitleProject? project;
    private Bitmap? previewImage;
    private string? sourcePath;
    private string status = "자막 파일을 열어 주세요.";
    private double maximumMilliseconds = 1;
    private double positionMilliseconds;
    private bool useCheckerboard;
    private bool disposed;

    public MainWindowViewModel(IFileDialogService dialogs)
    {
        this.dialogs = dialogs;
        renderer = new SkiaSubtitleRenderer(new BundledFontResolver(
            message => Serilog.Log.Information("{FontResolution}", message)));
        OpenCommand = new AsyncCommand(OpenAsync);
        saveCommand = new AsyncCommand(SaveAsync, () => project is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncCommand OpenCommand { get; }
    public AsyncCommand SaveCommand => saveCommand;
    public bool HasProject => project is not null;

    public Bitmap? PreviewImage
    {
        get => previewImage;
        private set => SetField(ref previewImage, value);
    }

    public string Status
    {
        get => status;
        private set => SetField(ref status, value);
    }

    public double MaximumMilliseconds
    {
        get => maximumMilliseconds;
        private set => SetField(ref maximumMilliseconds, value);
    }

    public double PositionMilliseconds
    {
        get => positionMilliseconds;
        set
        {
            if (SetField(ref positionMilliseconds, value))
            {
                OnPropertyChanged(nameof(PositionDisplay));
                RenderPreview();
            }
        }
    }

    public string PositionDisplay => TimeSpan.FromMilliseconds(PositionMilliseconds).ToString(@"mm\:ss\.fff");

    public bool UseCheckerboard
    {
        get => useCheckerboard;
        set
        {
            if (SetField(ref useCheckerboard, value))
            {
                RenderPreview();
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        PreviewImage?.Dispose();
        renderer.Dispose();
        disposed = true;
    }

    private async Task OpenAsync()
    {
        string? path = await dialogs.OpenSubtitleAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            ImportResult result = fileService.Import(path);
            project = result.Project;
            sourcePath = path;
            MaximumMilliseconds = Math.Max(1, project.Cues.Select(cue => cue.End.TotalMilliseconds).DefaultIfEmpty(1).Max());
            PositionMilliseconds = Math.Min(
                project.Cues.Select(cue => cue.Start.TotalMilliseconds).DefaultIfEmpty(0).Min() + 1,
                MaximumMilliseconds);
            Status = result.Warnings.Count == 0
                ? $"{Path.GetFileName(path)} — 큐 {project.Cues.Count}개"
                : $"{Path.GetFileName(path)} — {string.Join(" · ", result.Warnings.Select(warning => warning.Message))}";
            OnPropertyChanged(nameof(HasProject));
            saveCommand.NotifyCanExecuteChanged();
            RenderPreview();
        }
        catch (Exception exception)
        {
            Status = $"열기 실패: {exception.Message}";
        }
    }

    private async Task SaveAsync()
    {
        if (project is null)
        {
            return;
        }

        string suggestedName = Path.GetFileNameWithoutExtension(sourcePath) + ".ytt";
        string? path = await dialogs.SaveYttAsync(suggestedName);
        if (path is null)
        {
            return;
        }

        try
        {
            fileService.Export(project, path);
            Status = $"저장 완료: {path}";
        }
        catch (Exception exception)
        {
            Status = $"저장 실패: {exception.Message}";
        }
    }

    private void RenderPreview()
    {
        if (project is null || disposed)
        {
            return;
        }

        using SKBitmap bitmap = new(new SKImageInfo(YttConstants.ReferenceWidth, YttConstants.ReferenceHeight,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        using SKCanvas canvas = new(bitmap);
        if (UseCheckerboard)
        {
            DrawCheckerboard(canvas, bitmap.Width, bitmap.Height);
        }
        else
        {
            canvas.Clear(new SKColor(32, 32, 32));
        }

        renderer.Render(canvas, new PlayerViewport(bitmap.Width, bitmap.Height), project,
            TimeSpan.FromMilliseconds(PositionMilliseconds), new RenderOptions());
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(data.ToArray());
        Bitmap next = new(stream);
        Bitmap? previous = PreviewImage;
        PreviewImage = next;
        previous?.Dispose();

        FontResolution[] approximations = renderer.FontResolutions.Where(item => item.IsApproximation).ToArray();
        if (approximations.Length > 0)
        {
            Status = $"{Status.Split(" · 근사 표시:", StringSplitOptions.None)[0]} · 근사 표시: " +
                string.Join(", ", approximations.Select(item => item.Requested));
        }
    }

    private static void DrawCheckerboard(SKCanvas canvas, int width, int height)
    {
        const int cellSize = 32;
        using SKPaint light = new() { Color = new SKColor(64, 64, 64) };
        using SKPaint dark = new() { Color = new SKColor(40, 40, 40) };
        for (int y = 0; y < height; y += cellSize)
        {
            for (int x = 0; x < width; x += cellSize)
            {
                canvas.DrawRect(x, y, cellSize, cellSize,
                    ((x / cellSize) + (y / cellSize)) % 2 == 0 ? light : dark);
            }
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
