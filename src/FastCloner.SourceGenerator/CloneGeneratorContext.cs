using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FastCloner.SourceGenerator;

internal sealed class CloneGeneratorContext
{
    public TypeModel Model { get; }
    public StringBuilder Source { get; } = new StringBuilder();
    
    private readonly Dictionary<string, string> _typeNameToMethodName;
    private readonly HashSet<string> _neededHelperMethods;
    private readonly Queue<string> _pendingHelperMethods = new Queue<string>();
    private readonly Dictionary<string, MemberModel> _typeNameToMemberModel = new Dictionary<string, MemberModel>();
    private readonly Dictionary<string, TypeModel> _implicitTypeModels = new Dictionary<string, TypeModel>();
    private readonly Dictionary<string, TypeModel> _derivedTypeHelpers = new Dictionary<string, TypeModel>();
    private readonly HashSet<string> _usedDerivedHelperMethodNames = new HashSet<string>();
    private readonly Dictionary<string, int> _helperUsageCounts = new Dictionary<string, int>();

    public bool NeedsStateClass { get; set; }
    public bool NeedsClonerClass { get; set; }
    public bool UseStaticMethods { get; set; } = true;
    
    public bool CanHaveCircularReferences { get; set; }
    public bool NeedsStateTracking { get; set; }

    /// <summary>
    /// Whether a tracking state supplied to <c>InternalFastDeepClone(source, state)</c> is honored
    /// throughout this file's generated graph (helpers included). This is the capability behind an
    /// explicit <c>FastDeepClone(FastCloneOptions.PreserveIdentity)</c> operation and is a superset of
    /// <see cref="NeedsStateTracking"/>: it never changes what the public default entry point does.
    /// </summary>
    public bool StateCapable { get; }

    /// <summary>
    /// True when a <c>[FastClonerDiscoverGenericArguments(PreserveIdentity = true)]</c> surface
    /// requires this root to serve an explicit identity-preserving operation for its whole graph.
    /// Unlike <see cref="StateCapable"/> this is a hard requirement: if the graph necessarily
    /// delegates part of itself to the runtime cloner, the operation is withheld and the requirement
    /// is reported.
    /// </summary>
    public bool IdentityPreservationRequired => Model.IdentityPreservationRequired;

    /// <summary>
    /// True when the type asked for identity preservation itself, so it exposes the operation-level
    /// entry point even without a discovery requirement.
    /// </summary>
    public bool ConfiguresIdentity => Model.PreserveIdentity.HasValue ||
                                      Model.Members.Any(static member => member.PreserveIdentity.HasValue);

    /// <summary>
    /// Reasons why this generated graph cannot carry one tracking state across everything it deep
    /// clones. Empty means the graph can honor an explicit identity-preserving operation. Populated
    /// while the body is generated, so the decision uses the same analysis that emits the code.
    /// </summary>
    public List<string> RuntimeBoundaryReasons { get; } = [];

    public void RecordRuntimeBoundary(string reason)
    {
        if (!RuntimeBoundaryReasons.Contains(reason))
        {
            RuntimeBoundaryReasons.Add(reason);
        }
    }

    public bool IsFastClonerAvailable { get; }
    public TargetFramework TargetFramework { get; }
    public BridgeContract BridgeContract { get; }
    public List<NonPublicAccessor> NonPublicAccessors { get; } = [];
    public List<string> SkippedNonPublicMembers { get; } = [];

    private readonly Dictionary<string, bool> _circularReferenceOverrides = new Dictionary<string, bool>();

    public CloneGeneratorContext(TypeModel model, BridgeContract? bridgeContract = null, Dictionary<string, string>? sharedMethodNames = null, HashSet<string>? sharedNeededHelpers = null)
    {
        Model = model;
        CanHaveCircularReferences = model.CanHaveCircularReferences;
        IsFastClonerAvailable = model.IsFastClonerAvailable;
        TargetFramework = model.TargetFramework;
        BridgeContract = bridgeContract ?? BridgeContract.Empty;
        
        bool anyMemberNeedsIdentity = false;
        foreach (MemberModel m in model.Members)
        {
            if (m.PreserveIdentity == true)
            {
                anyMemberNeedsIdentity = true;
                break;
            }
        }
        NeedsStateTracking = model.NeedsStateTracking || anyMemberNeedsIdentity;
        StateCapable = NeedsStateTracking || model.SupportsStateTracking;
        
        _typeNameToMethodName = sharedMethodNames ?? new Dictionary<string, string>();
        _neededHelperMethods = sharedNeededHelpers ?? [];

        foreach (TypeModel? related in model.RelatedTypes)
        {
            IndexTypeName(_implicitTypeModels, related.FullyQualifiedName, related, related.IsStruct);
        }
        
        foreach (MemberModel nested in model.NestedTypes)
        {
            IndexTypeName(_typeNameToMemberModel, nested.TypeFullName, nested, nested.IsValueType);
        }
    }

    public void SetCircularReferenceOverride(string typeName, bool needsState)
    {
        _circularReferenceOverrides[typeName] = needsState;
    }

    public bool NeedsCircularState(string typeName, bool defaultFromModel)
    {
        if (_circularReferenceOverrides.TryGetValue(typeName, out bool overrideValue))
        {
            return overrideValue;
        }
        return defaultFromModel;
    }

    public bool HasPendingHelperMethods => _pendingHelperMethods.Count > 0;

    /// <summary>
    /// Whether the implicit helper for <paramref name="implicitModel"/> in this file takes a
    /// tracking state. While the file is state capable, reference-typed implicit types always
    /// participate so repeated references stay shared; structs keep their existing behavior
    /// (a distinct copy per occurrence is correct for value semantics). Call sites and helper
    /// definitions both go through this method so they cannot drift apart.
    /// </summary>
    public bool ImplicitHelperNeedsState(TypeModel implicitModel)
    {
        if (!StateCapable)
            return false;

        if (implicitModel.IsStruct)
            return NeedsCircularState(implicitModel.FullyQualifiedName, implicitModel.NeedsStateTracking);

        return true;
    }

    /// <summary>
    /// Whether a member's helper takes a tracking state at all. This is the <em>shape</em> of the
    /// generated helper and deliberately ignores member-level configuration: helper definitions are
    /// shared, so shape decisions must not depend on which member happened to ask for them.
    /// Whether the state is actually passed is decided per call site by
    /// <see cref="GetMemberStateArgument"/>.
    /// </summary>
    public bool MemberCanTrack(MemberModel member)
    {
        // Members whose clone call always accepts a state - clonable members (their own generated
        // file) and the runtime-bridge Cloner<T> used for everything else - can be handed this file's
        // state even when this file itself is not state capable. Threading through such a file is
        // what lets an explicit preserving operation reach across it, and it preserves the existing
        // behavior. Nothing is threaded by default: the ordinary path starts from a null state.
        if (MemberKindAlwaysAcceptsState(member))
            return true;

        return StateCapable && MemberKindSupportsTracking(member);
    }

    private static bool MemberKindAlwaysAcceptsState(MemberModel member)
    {
        switch (member.TypeKind)
        {
            case MemberTypeKind.Clonable:
            case MemberTypeKind.Object:
            case MemberTypeKind.Other:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Whether tracking applies to a member by default, i.e. without an explicit operation. This is
    /// where member-level configuration lives, including a negative override.
    /// </summary>
    public bool MemberTracksByDefault(MemberModel member)
    {
        if (member.PreserveIdentity is not null)
            return member.PreserveIdentity.Value;

        return MemberCanTrack(member);
    }

    /// <summary>
    /// The state expression to pass for a member. Member-level <c>[FastClonerPreserveIdentity(false)]</c>
    /// suppresses tracking for ordinary calls, but an explicit
    /// <c>FastCloneOptions.PreserveIdentity</c> operation is the strongest requirement for that
    /// invocation: the state is threaded whenever it belongs to such an operation, whatever the
    /// member configuration says.
    /// </summary>
    public string GetMemberStateArgument(MemberModel member, string stateVar)
    {
        if (MemberTracksByDefault(member))
            return stateVar;

        // Nothing to force when the enclosing body has no state in scope.
        if (!MemberCanTrack(member) || stateVar == "null")
            return "null";

        return PreservingOperationState(stateVar);
    }

    /// <summary>
    /// Expression that yields the state only when it belongs to an explicit preserving operation.
    /// </summary>
    public static string PreservingOperationState(string stateVar)
    {
        return $"{stateVar} is {{ IsPreservingOperation: true }} ? {stateVar} : null";
    }

    /// <summary>
    /// Whether an implicit clone can be inlined. Inlining copies members without registering the
    /// clone in the tracking state, so a state capable file must not inline reference-typed
    /// implicit clones: an explicit preserving operation has to see them.
    /// </summary>
    public bool CanInline(string typeFullName, bool isValueType, string stateVar)
    {
        // Inlining copies members without registering the clone in the tracking state, so it is only
        // valid when no state is in scope (the ordinary fast path) or when the file cannot track at
        // all. Structs have no identity to register.
        return ShouldInline(typeFullName) && (isValueType || !StateCapable || stateVar == "null");
    }

    private static bool MemberKindSupportsTracking(MemberModel member)
    {
        switch (member.TypeKind)
        {
            case MemberTypeKind.Safe:
                return false;
            case MemberTypeKind.Clonable:
                return true;
            case MemberTypeKind.Collection:
            case MemberTypeKind.Array:
            case MemberTypeKind.MultiDimArray:
            {
                // Elements that are safe values carry no identity to preserve.
                if (member.ElementIsSafe)
                    return false;
                if (member.ElementHasClonableAttr)
                    return true;
                break;
            }
        }

        return true;
    }

    public string DequeuePendingHelperMethod() => _pendingHelperMethods.Dequeue();

    public bool TryGetImplicitTypeModel(string typeName, out TypeModel model)
    {
        return _implicitTypeModels.TryGetValue(typeName, out model);
    }

    public bool TryGetMemberModel(string typeName, out MemberModel model)
    {
        return _typeNameToMemberModel.TryGetValue(typeName, out model);
    }

    public string GetMethodName(string typeName)
    {
        return _typeNameToMethodName[typeName];
    }

    public void RegisterImplicitType(TypeModel model)
    {
        IndexTypeName(_implicitTypeModels, model.FullyQualifiedName, model, model.IsStruct);
    }
    
    public void RegisterExternalMethod(string typeFullName, string methodName)
    {
        _typeNameToMethodName[typeFullName] = methodName;
    }
    
    public string GetOrCreateHelperMethodName(string typeFullName)
    {
        if (_typeNameToMethodName.TryGetValue(typeFullName, out string? existingMethod))
        {
            return existingMethod;
        }
        
        string methodName = $"FastClonerSgClone{GetCleanTypeName(typeFullName)}";
        bool isValueType = _implicitTypeModels.TryGetValue(typeFullName, out TypeModel implicitModel) && implicitModel.IsStruct;
        IndexTypeName(_typeNameToMethodName, typeFullName, methodName, isValueType);

        if (_neededHelperMethods.Add(typeFullName))
        {
            _pendingHelperMethods.Enqueue(typeFullName);
        }

        return methodName;
    }
    
    public string GetOrCreateHelperMethodName(MemberModel member)
    {
        string typeKey = member.TypeFullName;

        if (_typeNameToMethodName.TryGetValue(typeKey, out string? existingMethod))
        {
            return existingMethod;
        }
        
        string methodName = $"FastClonerSgClone{GetCleanTypeName(member.TypeFullName)}";
        IndexTypeName(_typeNameToMethodName, typeKey, methodName, member.IsValueType);

        if (_neededHelperMethods.Add(typeKey))
        {
            _pendingHelperMethods.Enqueue(typeKey);
        }
        
        IndexTypeName(_typeNameToMemberModel, typeKey, member, member.IsValueType);

        return methodName;
    }

    /// <summary>
    /// Element/key/value type names include the usage-site NRT suffix (<c>Payload?</c>),
    /// while helper keys are the underlying type (<c>Payload</c>). Index both so lookups match.
    /// </summary>
    private static void IndexTypeName<T>(Dictionary<string, T> map, string typeFullName, T value, bool isValueType)
    {
        if (!map.ContainsKey(typeFullName))
            map[typeFullName] = value;

        if (!isValueType && typeFullName.Length > 0 && typeFullName[typeFullName.Length - 1] != '?')
        {
            string annotated = typeFullName + "?";
            if (!map.ContainsKey(annotated))
                map[annotated] = value;
        }
    }
    
    private static string GetCleanTypeName(string typeName)
    {
        return typeName
            .Replace("global::", "")
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_')
            .Replace(' ', '_')
            .Replace('.', '_')
            .Replace('[', '_')
            .Replace(']', '_')
            .Replace('?', '_')
            .Replace(':', '_');
    }
    
    /// <summary>
    /// Registers a private clone helper for a dispatched derived type and returns its method name.
    /// The name is uniquified when needed: two closed constructions of the same generic subtype
    /// (e.g. TypedRepo&lt;int&gt; and TypedRepo&lt;string&gt;) share the same simple name.
    /// </summary>
    public string RegisterDerivedTypeHelper(TypeModel derivedType, string baseMethodName)
    {
        if (!_derivedTypeHelpers.ContainsKey(derivedType.FullyQualifiedName))
        {
            string methodName = baseMethodName;
            int suffix = 2;
            while (!_usedDerivedHelperMethodNames.Add(methodName))
                methodName = $"{baseMethodName}_{suffix++}";

            _derivedTypeHelpers[derivedType.FullyQualifiedName] = derivedType;
            _typeNameToMethodName[derivedType.FullyQualifiedName] = methodName;
        }

        return _typeNameToMethodName[derivedType.FullyQualifiedName];
    }
    
    public IEnumerable<(TypeModel Model, string MethodName)> GetDerivedTypeHelpers()
    {
        foreach (KeyValuePair<string, TypeModel> kvp in _derivedTypeHelpers)
        {
            yield return (kvp.Value, _typeNameToMethodName[kvp.Key]);
        }
    }
    
    public bool HasDerivedTypeHelpers => _derivedTypeHelpers.Count > 0;

    public void IncrementHelperUsage(string typeFullName)
    {
        if (_helperUsageCounts.TryGetValue(typeFullName, out int count))
        {
            _helperUsageCounts[typeFullName] = count + 1;
        }
        else
        {
            _helperUsageCounts[typeFullName] = 1;
        }
    }

    public int GetHelperUsageCount(string typeFullName)
    {
        return _helperUsageCounts.TryGetValue(typeFullName, out int count) ? count : 0;
    }

    public bool ShouldInline(string typeFullName)
    {
        return GetHelperUsageCount(typeFullName) == 1;
    }

    private int variableCounter;
    public int GetNextVariableId() => System.Threading.Interlocked.Increment(ref variableCounter);
    
    public string GetNonPublicAccessorPrefix()
    {
        return Model.TypeParameters.Count == 0 ? string.Empty : $"__FcAccessors<{string.Join(", ", Model.TypeParameters)}>.";
    }

    public NonPublicAccessor RegisterNonPublicAccessor(NonPublicAccessor accessor)
    {
        foreach (NonPublicAccessor existing in NonPublicAccessors)
        {
            if (existing.AccessorMethodName == accessor.AccessorMethodName)
                return existing;
        }
        NonPublicAccessors.Add(accessor);
        return accessor;
    }

    public static string FastClonerDeepCloneCall(string expression) => $"global::FastCloner.FastCloner.DeepClone({expression})";
    
    public static string NotNullIfNotNullAttr(bool isAvailable, string paramName = "source") 
        => isAvailable 
            ? $"[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(\"{paramName}\")]" 
            : "";
}
