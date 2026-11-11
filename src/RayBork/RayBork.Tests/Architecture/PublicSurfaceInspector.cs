using System.Reflection;

namespace RayBork.Tests;

internal static class PublicSurfaceInspector {
    private const BindingFlags PUBLIC_DECLARED_FLAGS =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string[] _forbiddenNamespacePrefixes = [
        "SDL3",
        "Silk.NET",
        "Vulkan",
        "RayBork.Internal",
        "RayBork.Bake",
    ];

    private static readonly string[] _forbiddenAssemblyNames = [
        "SDL3-CS",
        "Silk.NET.Vulkan",
        "Silk.NET.Core",
        "RayBork.Bake",
    ];

    public static IReadOnlyList<string> FindViolations(Assembly assembly) {
        ArgumentNullException.ThrowIfNull(assembly);

        HashSet<string> violations = new(StringComparer.Ordinal);
        HashSet<Type> auditedTypes = [];
        foreach (Type exportedType in assembly.GetExportedTypes()) {
            AuditType(exportedType, violations, auditedTypes);
        }

        return SortViolations(violations);
    }

    public static IReadOnlyList<string> FindViolations(Type type) {
        ArgumentNullException.ThrowIfNull(type);

        HashSet<string> violations = new(StringComparer.Ordinal);
        AuditType(type, violations, []);
        return SortViolations(violations);
    }

    private static void AuditType(Type type, HashSet<string> violations, HashSet<Type> auditedTypes) {
        if (!auditedTypes.Add(type)) {
            return;
        }

        string typeContext = $"type '{FormatTypeName(type)}'";
        InspectExposedType(type, typeContext, violations);

        if (type.BaseType is not null) {
            InspectExposedType(type.BaseType, $"base type of {typeContext}", violations);
        }

        foreach (Type implementedInterface in type.GetInterfaces()) {
            InspectExposedType(implementedInterface, $"interface of {typeContext}", violations);
        }

        foreach (FieldInfo field in type.GetFields(PUBLIC_DECLARED_FLAGS)) {
            InspectExposedType(field.FieldType, $"public field '{FormatTypeName(type)}.{field.Name}'", violations);
        }

        foreach (PropertyInfo property in type.GetProperties(PUBLIC_DECLARED_FLAGS)) {
            string propertyContext = $"public property '{FormatTypeName(type)}.{property.Name}'";
            InspectExposedType(property.PropertyType, propertyContext, violations);
            if (property.GetMethod is not null) {
                InspectExposedType(property.GetMethod.ReturnType, propertyContext, violations);
            }
            if (property.SetMethod is not null) {
                ParameterInfo[] setterParameters = property.SetMethod.GetParameters();
                if (setterParameters.Length > 0) {
                    InspectExposedType(setterParameters[^1].ParameterType, propertyContext, violations);
                }
            }
            foreach (ParameterInfo indexParameter in property.GetIndexParameters()) {
                InspectExposedType(indexParameter.ParameterType, propertyContext, violations);
            }
        }

        foreach (MethodInfo method in type.GetMethods(PUBLIC_DECLARED_FLAGS)) {
            if (method.IsSpecialName) {
                continue;
            }

            string methodContext = $"public method '{FormatTypeName(type)}.{method.Name}'";
            InspectExposedType(method.ReturnType, methodContext, violations);
            foreach (ParameterInfo parameter in method.GetParameters()) {
                InspectExposedType(parameter.ParameterType, methodContext, violations);
            }
        }

        foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
            string constructorContext = $"public constructor of {typeContext}";
            foreach (ParameterInfo parameter in constructor.GetParameters()) {
                InspectExposedType(parameter.ParameterType, constructorContext, violations);
            }
        }

        foreach (EventInfo eventInfo in type.GetEvents(PUBLIC_DECLARED_FLAGS)) {
            if (eventInfo.EventHandlerType is not null) {
                InspectExposedType(eventInfo.EventHandlerType, $"public event '{FormatTypeName(type)}.{eventInfo.Name}'", violations);
            }
        }

        foreach (Type nestedType in type.GetNestedTypes(BindingFlags.Public)) {
            AuditType(nestedType, violations, auditedTypes);
        }
    }

    private static void InspectExposedType(Type type, string context, HashSet<string> violations) {
        if (type.IsGenericParameter) {
            return;
        }

        string typeName = FormatTypeName(type);
        string? typeNamespace = type.Namespace;
        if (IsForbiddenNamespace(typeNamespace)) {
            violations.Add($"Backend type '{typeName}' is exposed by {context} (namespace '{typeNamespace}').");
        }

        string? assemblyName = type.Assembly.GetName().Name;
        if (assemblyName is not null && IsForbiddenAssembly(assemblyName)) {
            violations.Add($"Backend type '{typeName}' is exposed by {context} (assembly '{assemblyName}').");
        }

        if (type.HasElementType) {
            Type? elementType = type.GetElementType();
            if (elementType is not null) {
                InspectExposedType(elementType, context, violations);
            }
        }

        if (type.IsGenericType) {
            foreach (Type genericArgument in type.GetGenericArguments()) {
                InspectExposedType(genericArgument, context, violations);
            }
        }
    }

    private static bool IsForbiddenNamespace(string? typeNamespace) {
        if (typeNamespace is null) {
            return false;
        }

        foreach (string forbiddenNamespace in _forbiddenNamespacePrefixes) {
            if (string.Equals(typeNamespace, forbiddenNamespace, StringComparison.Ordinal)
                || typeNamespace.StartsWith(forbiddenNamespace + ".", StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private static bool IsForbiddenAssembly(string assemblyName) {
        foreach (string forbiddenAssemblyName in _forbiddenAssemblyNames) {
            if (string.Equals(assemblyName, forbiddenAssemblyName, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private static string FormatTypeName(Type type) => type.FullName ?? type.Name;

    private static IReadOnlyList<string> SortViolations(HashSet<string> violations) {
        List<string> sortedViolations = [.. violations];
        sortedViolations.Sort(StringComparer.Ordinal);
        return sortedViolations;
    }
}
