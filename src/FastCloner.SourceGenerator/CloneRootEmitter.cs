using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace FastCloner.SourceGenerator;

/// <summary>
/// Emits the generated clone file for a single root model. Shared by the <c>[FastClonerClonable]</c>
/// pipeline and the <c>[FastClonerDiscoverGenericArguments]</c> pipeline so both produce identical
/// files, diagnostics and naming for the same model.
/// </summary>
internal static class CloneRootEmitter
{
    public static void Emit(
        SourceProductionContext context,
        TypeModel model,
        EquatableArray<GenericUsage> usages,
        EquatableArray<ClosedSubtypeUsage> subtypeUsages,
        BridgeContract contract)
    {
        try
        {
            // Check for silent failure cases where FastCloner runtime is missing
            // We only warn here because generating broken code is sometimes better than nothing (e.g. partial clone),
            // but ideally the user should install FastCloner or fix the type.
            if (!model.IsFastClonerAvailable)
            {
                bool hasInitOnlyWithCycles = model.NeedsStateTracking && model.Members.Any(m => m.IsInitOnly);
                bool structWithReadonlyRefs = model.IsStruct && model.Members.Any(m => m is { IsValueType: false, IsReadOnly: true });

                if (hasInitOnlyWithCycles)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "FCG005",
                            "Init-only properties skipped",
                            "Type '{0}' has init-only properties and requires circular reference tracking, but FastCloner runtime is not available. Init-only properties will not be cloned.",
                            "FastCloner",
                            DiagnosticSeverity.Warning,
                            isEnabledByDefault: true),
                        Location.None,
                        model.Name));
                }

                if (structWithReadonlyRefs)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "FCG006",
                            "Readonly reference fields in struct skipped",
                            "Struct '{0}' has readonly reference fields, but FastCloner runtime is not available. These fields will be shallow-copied.",
                            "FastCloner",
                            DiagnosticSeverity.Warning,
                            isEnabledByDefault: true),
                        Location.None,
                        model.Name));
                }
            }

            CloneCodeGenerator generator = new CloneCodeGenerator(model, usages, subtypeUsages, contract);
            string generatedSource = generator.Generate();

            if (generator.SkippedNonPublicMembers.Count > 0)
            {
                string skippedList = string.Join(", ", generator.SkippedNonPublicMembers);
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "FCG010",
                        "Non-public members skipped by source generator",
                        "Type '{0}' has non-public members ({1}) that the source generator cannot clone on this target framework. " +
                        "Either upgrade the consumer to .NET 8+, or install the FastCloner runtime package, " +
                        "or apply [FastClonerVisibility] / [FastClonerIgnore] to opt out explicitly.",
                        "FastCloner",
                        DiagnosticSeverity.Warning,
                        isEnabledByDefault: true),
                    Location.None,
                    model.Name,
                    skippedList));
            }

            context.AddSource(GetHintName(model), SourceText.From(generatedSource, Encoding.UTF8));
        }
        catch (System.Exception ex)
        {
            Location location = Location.None;
            context.ReportDiagnostic(
                Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "FCG001",
                        "Generator Error",
                        "Error generating clone code: {0}",
                        "FastCloner",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    location,
                    ex.ToString()));
        }
    }

    /// <summary>
    /// Uses FullyQualifiedName to avoid collisions when the same class name exists in
    /// different namespaces.
    /// </summary>
    private static string GetHintName(TypeModel model)
    {
        string safeName = model.FullyQualifiedName
            .Replace("global::", "")
            .Replace(".", "_")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(" ", "")
            .Replace(",", "_")
            .Replace(":", "_")
            .Replace("?", "_");

        return $"{safeName}_FastDeepClone.g.cs";
    }
}
