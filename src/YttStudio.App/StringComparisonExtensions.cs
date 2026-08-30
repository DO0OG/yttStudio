namespace YttStudio.App;

/// <summary>문자 하나를 비교할 때도 명시적인 문자열 비교 규칙을 사용할 수 있게 한다.</summary>
internal static class StringComparisonExtensions
{
    public static bool StartsWith(this string value, char prefix, StringComparison comparison)
        => value.StartsWith(prefix.ToString(), comparison);
}
