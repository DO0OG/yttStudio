using System.Reflection;
using YttStudio.Core;

namespace YttStudio.Core.Tests;

public sealed class CoreSmokeTests
{
    [Fact]
    public void YttConstantsMatchReferenceValues()
    {
        Assert.Equal(0.96, YttConstants.CoordinateScale);
        Assert.Equal(2.0, YttConstants.CoordinateOffset);
        Assert.Equal(1280, YttConstants.ReferenceWidth);
        Assert.Equal(720, YttConstants.ReferenceHeight);
    }

    [Fact]
    public void SystemDrawingTypesDoNotLeakOutsideFormatBoundary()
    {
        IEnumerable<string> leaks = typeof(YttConstants).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace is null || !type.Namespace.StartsWith("YttStudio.Core.Format", StringComparison.Ordinal))
            .SelectMany(GetPublicApiTypes)
            .Where(type => type.Namespace?.StartsWith("System.Drawing", StringComparison.Ordinal) == true)
            .Select(type => type.FullName ?? type.Name)
            .Distinct(StringComparer.Ordinal);

        Assert.Empty(leaks);
    }

    private static IEnumerable<Type> GetPublicApiTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        yield return type;

        foreach (FieldInfo field in type.GetFields(flags))
        {
            yield return field.FieldType;
        }

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            yield return property.PropertyType;
        }

        foreach (MethodInfo method in type.GetMethods(flags))
        {
            yield return method.ReturnType;
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (ConstructorInfo constructor in type.GetConstructors(flags))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }
}
