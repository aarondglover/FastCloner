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
/// Outcome of discovery for one compilation.
/// <br/><br/>
/// <see cref="Roots"/> are types that need a new generated root.
/// <see cref="IdentityPreservationRequirements"/> name types whose root is generated elsewhere
/// (they already carry <c>[FastClonerClonable]</c>) and that must gain the capability to honor a
/// supplied tracking state. A requirement never changes such a type's default behavior and never
/// produces a second implementation.
/// </summary>
internal readonly record struct GenericArgumentDiscoveryResult(
    EquatableArray<DiscoveredGenericRoot> Roots,
    EquatableArray<string> IdentityPreservationRequirements);

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

    private static readonly GenericArgumentDiscoveryResult Empty =
        new(EquatableArray<DiscoveredGenericRoot>.Empty, EquatableArray<string>.Empty);

    /// <summary>
    /// Syntax gate for the usage pipeline. Generic methods resolve through the same node type
    /// as generic types, so a single predicate covers types, interfaces, delegates and methods.
    /// </summary>
    public static bool IsCandidate(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is GenericNameSyntax;
    }

    public static GenericArgumentDiscoveryResult Collect(
        GeneratorSyntaxContext context,
        TargetFramework targetFramework,
        ExternalIgnoreRegistry externalIgnores,
        CancellationToken cancellationToken)
    {
        GenericNameSyntax node = (GenericNameSyntax)context.Node;

        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(node, cancellationToken).Symbol;
        DiscoverySurface? surface = symbol == null ? null : GetDiscoverySurface(symbol);
        if (surface is not { } discovery)
            return Empty;

        List<ITypeSymbol> arguments = [];
        switch (symbol)
        {
            case INamedTypeSymbol { IsGenericType: true } named when discovery.MarkedType:
                arguments.AddRange(named.TypeArguments);
                break;
            case IMethodSymbol method:
                // Only the method's own arguments when the method itself is marked. An unmarked
                // generic method on a marked containing type must not contribute its arguments.
                if (discovery.MarkedMethod)
                    arguments.AddRange(method.TypeArguments);

                // A marked method on a generic containing type contributes both sources of closed
                // generic information, and a marked containing type is itself a discovery point.
                if (discovery.MarkedType && method.ContainingType is { IsGenericType: true } containingType)
                    arguments.AddRange(containingType.TypeArguments);

                break;
        }

        if (arguments.Count == 0)
            return Empty;

        Compilation compilation = context.SemanticModel.Compilation;
        bool nullability = context.SemanticModel.GetNullableContext(node.SpanStart).HasFlag(NullableContext.Enabled);

        List<DiscoveredGenericRoot> roots = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        HashSet<string> capabilityRequirements = new(StringComparer.Ordinal);

        foreach (ITypeSymbol argument in arguments)
        {
            CollectRoots(
                argument,
                compilation,
                nullability,
                targetFramework,
                externalIgnores,
                discovery.RequiresIdentityPreservation,
                roots,
                seen,
                capabilityRequirements);
        }

        return new GenericArgumentDiscoveryResult(
            new EquatableArray<DiscoveredGenericRoot>(roots.ToArray()),
            new EquatableArray<string>(capabilityRequirements.OrderBy(static name => name, StringComparer.Ordinal).ToArray()));
    }

    /// <summary>
    /// Merges the per-usage results into one deduplicated, deterministically ordered set.
    /// <br/><br/>
    /// Capability is a union, not a contest: when the same type is discovered through several
    /// surfaces, any surface requiring identity preservation only adds the capability to honor a
    /// supplied tracking state. The type's default behavior is identical either way, so there is no
    /// "winning" surface.
    /// </summary>
    public static GenericArgumentDiscoveryResult Merge(ImmutableArray<GenericArgumentDiscoveryResult> results)
    {
        Dictionary<string, DiscoveredGenericRoot> roots = new(StringComparer.Ordinal);
        HashSet<string> capabilityRequirements = new(StringComparer.Ordinal);

        foreach (GenericArgumentDiscoveryResult result in results)
        {
            foreach (string requirement in result.IdentityPreservationRequirements)
                capabilityRequirements.Add(requirement);

            foreach (DiscoveredGenericRoot root in result.Roots)
            {
                string key = root.Model != null
                    ? root.Model.FullyQualifiedName
                    : $"{root.Failure?.Id}:{root.Failure?.GetMessage()}";

                if (!roots.TryGetValue(key, out DiscoveredGenericRoot existing))
                {
                    roots[key] = root;
                    continue;
                }

                if (root.Model is { ExplicitIdentityOperationRequested: true } && existing.Model is { ExplicitIdentityOperationRequested: false })
                    roots[key] = root;
                else if (root.Model is { SupportsStateTracking: true } && existing.Model is { SupportsStateTracking: false })
                    roots[key] = root;
            }
        }

        return new GenericArgumentDiscoveryResult(
            new EquatableArray<DiscoveredGenericRoot>(roots.Values
                .OrderBy(static root => root.Model?.FullyQualifiedName ?? root.Failure?.Id, StringComparer.Ordinal)
                .ToArray()),
            new EquatableArray<string>(capabilityRequirements
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray()));
    }

    /// <summary>
    /// Which declaration of a closed usage is a discovery point, and whether that surface requires
    /// the discovered types to have identity-preservation capability.
    /// </summary>
    private readonly record struct DiscoverySurface(
        bool MarkedMethod,
        bool MarkedType,
        bool RequiresIdentityPreservation);

    private static DiscoverySurface? GetDiscoverySurface(ISymbol symbol)
    {
        switch (symbol)
        {
            case INamedTypeSymbol { IsGenericType: true } type:
            {
                bool typeRequires = false;
                bool markedType = TryGetTypeSurface(type.OriginalDefinition, ref typeRequires);
                return markedType ? new DiscoverySurface(MarkedMethod: false, MarkedType: true, typeRequires) : null;
            }
            case IMethodSymbol method:
            {
                bool methodRequires = false;
                bool markedMethod = TryGetDiscoveryAttribute(method.OriginalDefinition, ref methodRequires);
                bool markedType = false;
                bool typeRequires = false;

                if (method.ContainingType is { IsGenericType: true } containingType)
                    markedType = TryGetTypeSurface(containingType.OriginalDefinition, ref typeRequires);

                if (!markedMethod && !markedType)
                    return null;

                return new DiscoverySurface(markedMethod, markedType, methodRequires || typeRequires);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// A generic type is a discovery point when it carries the attribute itself, or when it
    /// declares a marked method: <c>IStage&lt;T&gt;</c> with <c>[FastClonerDiscoverGenericArguments] void Execute()</c>
    /// makes a closed <c>IStage&lt;Form&gt;</c> usage expose <c>Form</c>.
    /// </summary>
    private static bool TryGetTypeSurface(INamedTypeSymbol definition, ref bool requiresIdentityPreservation)
    {        requiresIdentityPreservation = false;
        bool marked = TryGetDiscoveryAttribute(definition, ref requiresIdentityPreservation);

        foreach (ISymbol member in definition.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } &&
                TryGetDiscoveryAttribute(member, ref requiresIdentityPreservation))
            {
                marked = true;
            }
        }

        return marked;
    }

    private static bool TryGetDiscoveryAttribute(ISymbol symbol, ref bool requiresIdentityPreservation)
    {
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != DiscoveryAttributeName)
                continue;

            foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments)
            {
                if (namedArgument is { Key: "PreserveIdentity", Value.Value: bool required })
                    requiresIdentityPreservation |= required;
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
        bool requiresIdentityPreservation,
        List<DiscoveredGenericRoot> roots,
        HashSet<string> seen,
        HashSet<string> capabilityRequirements)
    {
        if (argument is IArrayTypeSymbol array)
        {
            CollectRoots(array.ElementType, compilation, nullability, targetFramework, externalIgnores, requiresIdentityPreservation, roots, seen, capabilityRequirements);
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
                CollectRoots(typeArgument, compilation, nullability, targetFramework, externalIgnores, requiresIdentityPreservation, roots, seen, capabilityRequirements);

            return;
        }

        // Non-generic collections (ArrayList, Hashtable) carry no discoverable element type.
        if (TypeAnalyzer.IsCollectionType(named) || TypeAnalyzer.IsDictionaryType(named))
            return;

        string key = TypeAnalyzer.GetTypeNameForSignature(named);

        // Already a root through its own attribute: a second entry point would be a duplicate member
        // of the same extension class. The requirement converges into that existing root instead, by
        // asking it for the capability to honor a supplied tracking state.
        if (TypeAnalyzer.HasClonableAttribute(named))
        {
            if (requiresIdentityPreservation)
                capabilityRequirements.Add(key);

            return;
        }

        // Types from other assemblies are only rooted when the generator would already clone
        // them implicitly (public parameterless constructor). Member-wise cloners for arbitrary
        // foreign types would depend on state the generator cannot access.
        if (!IsDeclaredInCompilation(named, compilation) && !TypeAnalyzer.IsImplicitCandidate(named))
            return;

        if (!seen.Add(key))
            return;

        if (TypeModelFactory.TryCreate(
                named,
                nullability,
                compilation,
                targetFramework,
                externalIgnores,
                out TypeModel? model,
                out Diagnostic? error,
                requiresIdentityPreservation))
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
