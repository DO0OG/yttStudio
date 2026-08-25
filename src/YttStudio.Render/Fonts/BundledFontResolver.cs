using System.Reflection;
using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

/// <summary>Loads redistributable fonts from assembly resources and reports explicit fallbacks.</summary>
public sealed class BundledFontResolver : IFontResolver, IDisposable
{
    private static readonly IReadOnlyDictionary<YtFont, BundledFont> BundledFonts =
        new Dictionary<YtFont, BundledFont>
        {
            [YtFont.Default] = new("Roboto-Regular.ttf", FontResolutionStatus.BundledExact),
            [YtFont.Sans] = new("Roboto-Regular.ttf", FontResolutionStatus.BundledExact),
            [YtFont.MonoSerif] = new("LiberationMono-Regular.ttf", FontResolutionStatus.BundledMetricCompatible),
            [YtFont.Serif] = new("LiberationSerif-Regular.ttf", FontResolutionStatus.BundledMetricCompatible),
            [YtFont.SmallCaps] = new("LiberationSans-Regular.ttf", FontResolutionStatus.BundledMetricCompatible),
        };

    private static readonly IReadOnlyDictionary<YtFont, string> SystemFamilies =
        new Dictionary<YtFont, string>
        {
            [YtFont.MonoSans] = "Lucida Console",
            [YtFont.Casual] = "Comic Sans MS",
            [YtFont.Cursive] = "Monotype Corsiva",
        };

    private readonly Dictionary<YtFont, FontResolution> cache = [];
    private readonly Action<string>? log;
    private bool disposed;

    public BundledFontResolver(Action<string>? log = null)
    {
        this.log = log;
    }

    public FontResolution Resolve(YtFont requested)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (cache.TryGetValue(requested, out FontResolution? cached))
        {
            return cached;
        }

        FontResolution resolution = BundledFonts.TryGetValue(requested, out BundledFont? bundled)
            ? LoadBundled(requested, bundled)
            : LoadSystemOrFallback(requested, SystemFamilies.GetValueOrDefault(requested) ?? "Roboto");
        cache.Add(requested, resolution);
        log?.Invoke($"font: {requested} -> {resolution.ActualFamilyName} ({resolution.Status})");
        return resolution;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        foreach (SKTypeface typeface in cache.Values.Select(item => item.Typeface).Distinct())
        {
            typeface.Dispose();
        }

        cache.Clear();
        disposed = true;
    }

    private static FontResolution LoadBundled(YtFont requested, BundledFont bundled)
    {
        Assembly assembly = typeof(BundledFontResolver).Assembly;
        string resourceName = $"YttStudio.Render.Assets.Fonts.{bundled.FileName}";
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Bundled font resource '{resourceName}' is missing.");
        SKTypeface typeface = SKTypeface.FromStream(stream)
            ?? throw new InvalidOperationException($"Bundled font '{bundled.FileName}' could not be loaded.");
        return new FontResolution(requested, typeface, typeface.FamilyName, bundled.Status);
    }

    private static FontResolution LoadSystemOrFallback(YtFont requested, string familyName)
    {
        SKTypeface candidate = SKTypeface.FromFamilyName(familyName);
        if (string.Equals(candidate.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
        {
            return new FontResolution(requested, candidate, candidate.FamilyName, FontResolutionStatus.SystemExact);
        }

        candidate.Dispose();
        using Stream stream = typeof(BundledFontResolver).Assembly.GetManifestResourceStream(
            "YttStudio.Render.Assets.Fonts.Roboto-Regular.ttf")
            ?? throw new InvalidOperationException("The Roboto fallback resource is missing.");
        SKTypeface fallback = SKTypeface.FromStream(stream)
            ?? throw new InvalidOperationException("The Roboto fallback could not be loaded.");
        return new FontResolution(requested, fallback, fallback.FamilyName, FontResolutionStatus.ApproximateFallback);
    }

    private sealed record BundledFont(string FileName, FontResolutionStatus Status);
}
