using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace FastCloner.SourceGenerator;

/// <summary>
/// Expands identity-preservation capability requirements over the generated graph.
/// <br/><br/>
/// Capability is what lets one generated file honor a tracking state supplied by another. A type
/// that must serve an explicit identity-preserving operation therefore pulls in every type its
/// generated graph clones through a <em>different</em> generated file (clonable members, clonable
/// collection elements and dictionary keys/values, dispatched derived types). Without that, a
/// preserving operation would silently stop at the first file boundary.
/// </summary>
internal static class IdentityCapabilityRequirements
{
    public static EquatableArray<string> Expand(
        ImmutableArray<Result<TypeModel>> models,
        GenericArgumentDiscoveryResult discovery)
    {
        Dictionary<string, TypeModel> byFqn = new(StringComparer.Ordinal);

        foreach (Result<TypeModel> result in models)
        {
            if (result.IsSuccess && result.Value is TypeModel model)
                byFqn[model.FullyQualifiedName] = model;
        }

        foreach (DiscoveredGenericRoot root in discovery.Roots)
        {
            if (root.Model is { } model)
                byFqn[model.FullyQualifiedName] = model;
        }

        HashSet<string> required = new(StringComparer.Ordinal);
        Queue<string> pending = new();

        void Require(string typeName)
        {
            if (required.Add(typeName))
                pending.Enqueue(typeName);
        }

        // A type that configures identity (or was discovered with the requirement) is a root of the
        // requirement: its own file honors a supplied state, so everything it clones through another
        // generated file has to honor it too.
        foreach (TypeModel model in byFqn.Values)
        {
            if (model.SupportsStateTracking)
                Require(model.FullyQualifiedName);
        }

        foreach (string typeName in discovery.IdentityPreservationRequirements)
            Require(typeName);

        while (pending.Count > 0)
        {
            string typeName = pending.Dequeue();
            if (!byFqn.TryGetValue(typeName, out TypeModel? model))
                continue;

            HashSet<string> visited = new(StringComparer.Ordinal);
            foreach (MemberModel member in EnumerateMembers(model, visited))
                CollectCrossFileRelations(member, Require);

            foreach (TypeModel derived in model.DerivedTypes)
                Require(derived.FullyQualifiedName);
        }

        return new EquatableArray<string>(required.OrderBy(static name => name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Members of the type itself plus the members of the implicit models the same file generates
    /// helpers for, since those helpers also call into other generated files.
    /// </summary>
    private static IEnumerable<MemberModel> EnumerateMembers(TypeModel model, HashSet<string> visited)
    {
        if (!visited.Add(model.FullyQualifiedName))
            yield break;

        foreach (MemberModel member in model.Members)
            yield return member;

        foreach (MemberModel member in model.NestedTypes)
            yield return member;

        foreach (TypeModel related in model.RelatedTypes)
        {
            foreach (MemberModel member in EnumerateMembers(related, visited))
                yield return member;
        }
    }

    private static void CollectCrossFileRelations(MemberModel member, Action<string> require)
    {
        if (member.TypeKind == MemberTypeKind.Clonable)
            require(member.TypeFullName);

        if (member.ElementHasClonableAttr && member.ElementTypeName != null)
            require(member.ElementTypeName);

        if (member.KeyIsClonable && member.KeyTypeName != null)
            require(member.KeyTypeName);

        if (member.ValueIsClonable && member.ValueTypeName != null)
            require(member.ValueTypeName);
    }
}
