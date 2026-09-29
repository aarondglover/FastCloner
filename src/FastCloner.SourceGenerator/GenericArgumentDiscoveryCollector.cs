using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FastCloner.SourceGenerator;

/// <summary>
/// A clone root discovered through a <c>[FastClonerDiscoverGenericArguments]</c> API surface.
/// Holds either the generated model or the diagnostic that prevented modelling the type.
/// <br/><br/>
/// Unlike <see cref="Result{T}"/> (a class, so never value-equal) this is a value type with
/// structural equality, so an unchanged discovered root keeps its incremental cache slot and
/// its generated file is not rewritten.
/// </summary>
internal readonly struct DiscoveredGenericRoot : IEquatable<DiscoveredGenericRoot>
{
    public DiscoveredGenericRoot(TypeModel model) : this(model, null)
    {
    }

    public DiscoveredGenericRoot(Diagnostic failure) : this(null, failure)
    {
    }

    private DiscoveredGenericRoot(TypeModel? model, Diagnostic? failure)
    {
        Model = model;
        Failure = failure;
    }

    public TypeModel? Model { get; }

    public Diagnostic? Failure { get; }

    public bool Equals(DiscoveredGenericRoot other)
    {
        return Equals(Model, other.Model) &&
               Failure?.Id == other.Failure?.Id &&
               Failure?.GetMessage() == other.Failure?.GetMessage();
    }

    public override bool Equals(object? obj)
    {
        return obj is DiscoveredGenericRoot other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Model?.GetHashCode() ?? Failure?.Id.GetHashCode() ?? 0;
    }
}

/// <summary>
/// Discovers clone roots from closed usages of generic API surfaces marked with
/// <c>[FastClonerDiscoverGenericArguments]</c>.
/// <br/><br/>
/// Discovery is driven from the usage site rather than from the attribute: a marked
/// declaration is only a discovery point once it is observed in a closed generic form, and
/// checking the referenced declaration's attributes at the usage site also covers marked
/// declarations that live in a referenced assembly.
/// </summary>
internal static class GenericArgumentDiscoveryCollector
{
    private const string DiscoveryAttributeName = "FastCloner.SourceGenerator.Shared.FastClonerDiscoverGenericArgumentsAttribute";

    /// <summary>
    /// Syntax gate for the usage pipeline. Generic methods resolve through the same node type
    /// as generic types, so a single predicate covers types, interfaces, delegates and methods.
    /// </summary>
    public static bool IsCandidate(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is GenericNameSyntax;
    }

    public static EquatableArray<DiscoveredGenericRoot> Collect(
        GeneratorSyntaxContext context,
        TargetFramework targetFramework,
        ExternalIgnoreRegistry externalIgnores,
        CancellationToken cancellationToken)
    {
        GenericNameSyntax node = (GenericNameSyntax)context.Node;

        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(node, cancellationToken).Symbol;
        if (symbol == null || !TryGetDiscoverySurface(symbol, out bool preserveIdentity))
            return EquatableArray<DiscoveredGenericRoot>.Empty;

        List<ITypeSymbol> arguments = [];
        switch (symbol)
        {
            case INamedTypeSymbol { IsGenericType: true } named:
                arguments.AddRange(named.TypeArguments);
                break;
            case IMethodSymbol method:
                arguments.AddRange(method.TypeArguments);

                // A marked method on a generic containing type contributes both sources of
                // closed generic information: the method's own arguments and the closed
                // arguments of the containing type.
                if (method.ContainingType is { IsGenericType: true } containingType)
                    arguments.AddRange(containingType.TypeArguments);

                break;
        }

        if (arguments.Count == 0)
            return EquatableArray<DiscoveredGenericRoot>.Empty;

        Compilation compilation = context.SemanticModel.Compilation;
        bool nullability = context.SemanticModel.GetNullableContext(node.SpanStart).HasFlag(NullableContext.Enabled);

        List<DiscoveredGenericRoot> roots = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (ITypeSymbol argument in arguments)
            CollectRoots(argument, compilation, nullability, targetFramework, externalIgnores, preserveIdentity, roots, seen);

        return new EquatableArray<DiscoveredGenericRoot>(roots.ToArray());
    }

    /// <summary>
    /// Merges the per-usage results into one deduplicated, deterministically ordered set.
    /// When the same type is discovered through several surfaces, the identity-preserving
    /// model wins: root identity is a property of the discovered type, not of the surface
    /// that happened to discover it first.
    /// </summary>
    public static IEnumerable<DiscoveredGenericRoot> Merge(ImmutableArray<EquatableArray<DiscoveredGenericRoot>> lists)
    {
        Dictionary<string, DiscoveredGenericRoot> merged = new(StringComparer.Ordinal);

        foreach (EquatableArray<DiscoveredGenericRoot> list in lists)
        {
            foreach (DiscoveredGenericRoot root in list)
            {
                string key = root.Model != null
                    ? root.Model.FullyQualifiedName
                    : $"{root.Failure?.Id}:{root.Failure?.GetMessage()}";

                if (!merged.TryGetValue(key, out DiscoveredGenericRoot existing))
                {
                    merged[key] = root;
                    continue;
                }

                if (PreservesIdentity(root.Model) && !PreservesIdentity(existing.Model))
                    merged[key] = root;
            }
        }

        return merged.Values
            .OrderBy(static root => root.Model?.FullyQualifiedName ?? root.Failure?.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool PreservesIdentity(TypeModel? model)
    {
        return model is { PreserveIdentity: true };
    }

    /// <summary>
    /// Decides whether a closed usage of <paramref name="symbol"/> is a discovery point and
    /// whether roots discovered through it should preserve identity.
    /// </summary>
    private static bool TryGetDiscoverySurface(ISymbol symbol, out bool preserveIdentity)
    {
        preserveIdentity = false;

        switch (symbol)
        {
            case INamedTypeSymbol { IsGenericType: true } type:
                return TryGetTypeSurface(type.OriginalDefinition, ref preserveIdentity);
            case IMethodSymbol method:
            {
                bool marked = TryGetDiscoveryAttribute(method.OriginalDefinition, ref preserveIdentity);

                if (method.ContainingType is { IsGenericType: true } containingType)
                    marked |= TryGetTypeSurface(containingType.OriginalDefinition, ref preserveIdentity);

                return marked;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// A generic type is a discovery surface when it carries the attribute itself, or when it
    /// declares a marked method: <c>IStage&lt;T&gt;</c> with <c>[FastClonerDiscoverGenericArguments] void Execute()</c>
    /// makes a closed <c>IStage&lt;Form&gt;</c> usage expose <c>Form</c>.
    /// </summary>
    private static bool TryGetTypeSurface(INamedTypeSymbol definition, ref bool preserveIdentity)
    {
        bool marked = TryGetDiscoveryAttribute(definition, ref preserveIdentity);

        foreach (ISymbol member in definition.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } &&
                TryGetDiscoveryAttribute(member, ref preserveIdentity))
            {
                marked = true;
            }
        }

        return marked;
    }

    private static bool TryGetDiscoveryAttribute(ISymbol symbol, ref bool preserveIdentity)
    {
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != DiscoveryAttributeName)
                continue;

            foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments)
            {
                if (namedArgument is { Key: "PreserveIdentity", Value.Value: bool enabled })
                    preserveIdentity |= enabled;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Converts one closed generic argument into clone roots.
    /// <br/><br/>
    /// A root must be a closed, non-generic type: the generated entry point is a concrete
    /// <c>FastDeepClone(this T)</c> extension, so constructed generics (which keep the
    /// definition's type parameters on the symbol) cannot be roots themselves. They are
    /// descended into instead, which is what makes nested arguments such as
    /// <c>IContainer&lt;List&lt;Order&gt;&gt;</c> and <c>IContainer&lt;Wrapper&lt;Order&gt;&gt;</c>
    /// expose <c>Order</c>.
    /// </summary>
    private static void CollectRoots(
        ITypeSymbol argument,
        Compilation compilation,
        bool nullability,
        TargetFramework targetFramework,
        ExternalIgnoreRegistry externalIgnores,
        bool preserveIdentity,
        List<DiscoveredGenericRoot> roots,
        HashSet<string> seen)
    {
        if (argument is IArrayTypeSymbol array)
        {
            CollectRoots(array.ElementType, compilation, nullability, targetFramework, externalIgnores, preserveIdentity, roots, seen);
            return;
        }

        // Type parameters, pointers and function pointers carry no closed information.
        if (argument is not INamedTypeSymbol named)
            return;

        // Open or unbound arguments (IContainer<T> inside a generic declaration) produce no roots.
        if (GenericTypeAnalyzer.ContainsUnboundTypeParameter(named))
            return;

        if (TypeAnalyzer.IsSafeType(named, compilation) || TypeAnalyzer.IsDoNotCloneType(named))
            return;

        // An extension on object would be applicable to every receiver and would silently
        // shallow-copy anything FastCloner has no cloner for.
        if (named.SpecialType == SpecialType.System_Object)
            return;

        if (named.IsGenericType)
        {
            foreach (ITypeSymbol typeArgument in named.TypeArguments)
                CollectRoots(typeArgument, compilation, nullability, targetFramework, externalIgnores, preserveIdentity, roots, seen);

            return;
        }

        // Non-generic collections (ArrayList, Hashtable) carry no discoverable element type.
        if (TypeAnalyzer.IsCollectionType(named) || TypeAnalyzer.IsDictionaryType(named))
            return;

        // Already a root through its own attribute; a second entry point would be a duplicate
        // member of the generated extension class.
        if (TypeAnalyzer.HasClonableAttribute(named))
            return;

        // Types from other assemblies are only rooted when the generator would already clone
        // them implicitly (public parameterless constructor). Member-wise cloners for arbitrary
        // foreign types would depend on state the generator cannot access.
        if (!IsDeclaredInCompilation(named, compilation) && !TypeAnalyzer.IsImplicitCandidate(named))
            return;

        string key = TypeAnalyzer.GetTypeNameForSignature(named);
        if (!seen.Add(key))
            return;

        if (TypeModelFactory.TryCreate(named, nullability, compilation, targetFramework, externalIgnores, out TypeModel? model, out Diagnostic? error, preserveIdentity ? true : null))
        {
            roots.Add(new DiscoveredGenericRoot(model!));
        }
        else if (error != null)
        {
            roots.Add(new DiscoveredGenericRoot(error));
        }
    }

    private static bool IsDeclaredInCompilation(INamedTypeSymbol type, Compilation compilation)
    {
        return SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly);
    }
}
