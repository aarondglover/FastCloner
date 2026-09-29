using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace FastCloner.SourceGenerator;

/// <summary>
/// The outcome of expanding identity requirements over the generated graph.
/// <br/><br/>
/// <paramref name="Capability"/> names every type that must be able to honor a supplied tracking
/// state, <paramref name="Required"/> every type for which that state is a <em>hard</em> requirement
/// (including the ones reached transitively, because a parent guarantee depends on them), and
/// <paramref name="Direct"/> only the types a
/// <c>[FastClonerDiscoverGenericArguments(PreserveIdentity = true)]</c> surface named itself.
/// <br/><br/>
/// The three are deliberately separate: capability is internal, <paramref name="Required"/> is what
/// must be reported when it cannot be supplied, and <paramref name="Direct"/> - together with the
/// type's own identity configuration - is what decides whether the type gains the public
/// <c>FastDeepClone(FastCloneOptions)</c> overload. A type that is only capable, or only required
/// because another root's graph reaches it, keeps its internal state machinery and gains no new
/// public API.
/// </summary>
internal readonly record struct IdentityRequirementSet(
    EquatableArray<string> Capability,
    EquatableArray<string> Required,
    EquatableArray<string> Direct);

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
    public static IdentityRequirementSet Expand(
        ImmutableArray<Result<TypeModel>> models,
        GenericArgumentDiscoveryResult discovery,
        EquatableArray<GenericUsage> usages)
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

        HashSet<string> capability = new(StringComparer.Ordinal);
        HashSet<string> required = new(StringComparer.Ordinal);
        HashSet<string> direct = new(StringComparer.Ordinal);
        Queue<string> pendingCapability = new();
        Queue<string> pendingRequired = new();

        void RequireCapability(string typeName)
        {
            if (capability.Add(typeName))
                pendingCapability.Enqueue(typeName);
        }

        void RequireOperation(string typeName)
        {
            RequireCapability(typeName);

            if (required.Add(typeName))
                pendingRequired.Enqueue(typeName);
        }

        // A type that configures identity (or was discovered with the requirement) is a root of the
        // requirement: its own file honors a supplied state, so everything it clones through another
        // generated file has to honor it too. Only the discovery surfaces make the operation itself
        // a hard requirement.
        foreach (TypeModel model in byFqn.Values)
        {
            if (model.SupportsStateTracking || model.IdentityPreservationRequired)
                RequireCapability(model.FullyQualifiedName);
        }

        // Direct requirements: what the surface named itself. These are never propagated, so the
        // public operation-level API is only offered where someone actually asked for it.
        foreach (string typeName in discovery.IdentityPreservationRequirements)
        {
            direct.Add(typeName);
            RequireOperation(typeName);
        }

        foreach (DiscoveredGenericRoot root in discovery.Roots)
        {
            if (root.Model is not { } model)
                continue;

            if (model.ExplicitIdentityOperationRequested)
            {
                direct.Add(model.FullyQualifiedName);
                RequireOperation(model.FullyQualifiedName);
            }
            else if (model.IdentityPreservationRequired)
            {
                RequireOperation(model.FullyQualifiedName);
            }
        }

        while (pendingCapability.Count > 0 || pendingRequired.Count > 0)
        {
            bool requiredPass = pendingRequired.Count > 0;
            string typeName = requiredPass ? pendingRequired.Dequeue() : pendingCapability.Dequeue();

            // A required root keeps requiring the operation across the whole graph it clones; a
            // merely capable one keeps only the capability.
            Action<string> require = requiredPass ? RequireOperation : RequireCapability;

            if (!byFqn.TryGetValue(typeName, out TypeModel? model))
                continue;

            HashSet<string> visited = new(StringComparer.Ordinal);
            foreach (MemberModel member in EnumerateMembers(model, visited))
                CollectCrossFileRelations(member, require);

            foreach (TypeModel derived in model.DerivedTypes)
                require(derived.FullyQualifiedName);

            // A clonable closed argument dispatched by the generated Cloner<T> helper has its own
            // root in this compilation, and the helper hands the state straight to it. That root has
            // to be able to use it, exactly like a clonable member's file.
            foreach (GenericUsage usage in usages)
            {
                if (usage.IsClonable &&
                    usage.IsDeclaredInCompilation &&
                    string.Equals(usage.GenericTypeMetadataName, typeName, StringComparison.Ordinal))
                {
                    require(usage.ArgumentTypeMetadataName);
                }
            }
        }

        return new IdentityRequirementSet(
            new EquatableArray<string>(capability.OrderBy(static name => name, StringComparer.Ordinal).ToArray()),
            new EquatableArray<string>(required.OrderBy(static name => name, StringComparer.Ordinal).ToArray()),
            new EquatableArray<string>(direct.OrderBy(static name => name, StringComparer.Ordinal).ToArray()));
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
