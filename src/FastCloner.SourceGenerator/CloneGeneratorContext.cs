using System;
using System.Collections.Generic;
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
    private readonly Dictionary<string, bool> _helperAcceptsState = new Dictionary<string, bool>(StringComparer.Ordinal);

    public bool NeedsStateClass { get; set; }
    public bool NeedsClonerClass { get; set; }
    public bool UseStaticMethods { get; set; } = true;
    
    public bool CanHaveCircularReferences { get; set; }
    public bool NeedsStateTracking { get; set; }
    public bool IsFastClonerAvailable { get; }
    public TargetFramework TargetFramework { get; }
    public BridgeContract BridgeContract { get; }
    public List<NonPublicAccessor> NonPublicAccessors { get; } = [];
    public List<string> SkippedNonPublicMembers { get; } = [];

    private readonly Dictionary<string, bool> _circularReferenceOverrides = new Dictionary<string, bool>();

    /// <param name="additionalModels">
    /// Models this context will clone that are not reachable through <see cref="TypeModel.RelatedTypes"/>
    /// or <see cref="TypeModel.DerivedTypes"/> — currently the implicit models discovered from generic
    /// usages. They take part in the identity opt-out scan so that capability is complete before the
    /// first helper decision is taken.
    /// </param>
    public CloneGeneratorContext(
        TypeModel model,
        BridgeContract? bridgeContract = null,
        Dictionary<string, string>? sharedMethodNames = null,
        HashSet<string>? sharedNeededHelpers = null,
        IEnumerable<TypeModel>? additionalModels = null)
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
        
        _typeNameToMethodName = sharedMethodNames ?? new Dictionary<string, string>();
        _neededHelperMethods = sharedNeededHelpers ?? [];

        foreach (TypeModel? related in model.RelatedTypes)
        {
            IndexTypeName(_implicitTypeModels, related.FullyQualifiedName, related, related.IsStruct);
        }

        RequiresIdentityOptOutPropagation = ScanForIdentityOptOut(model, additionalModels);
        
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

    public string DequeuePendingHelperMethod() => _pendingHelperMethods.Dequeue();

    public bool TryGetImplicitTypeModel(string typeName, out TypeModel model)
    {
        return _implicitTypeModels.TryGetValue(typeName, out model);
    }

    public bool TryGetMemberModel(string typeName, out MemberModel model)
    {
        return _typeNameToMemberModel.TryGetValue(typeName, out model);
    }

    /// <summary>
    /// Whether any member cloned through this context opts out of identity preservation with
    /// <c>[FastClonerPreserveIdentity(false)]</c>.
    /// <br/><br/>
    /// Such an opt-out has to travel through the whole member subgraph, including helpers generated
    /// for nested collection/array/dictionary/implicit levels, so every helper in the context must
    /// be <i>able</i> to receive it. This is a capability, not a behaviour change: a helper whose
    /// state argument is <c>null</c> behaves exactly as a helper generated without state ever did.
    /// <br/><br/>
    /// Finalized in the constructor over every model this context can reach — including the models
    /// that only generic-usage analysis discovers — so capability never depends on discovery order.
    /// </summary>
    public bool RequiresIdentityOptOutPropagation { get; }

    /// <summary>
    /// Whether the shared helper for <paramref name="typeFullName"/> declares a state parameter.
    /// <br/><br/>
    /// This is the helper's <i>capability</i>, not its default behaviour: a helper that accepts state
    /// still tracks nothing when it is handed <c>null</c>, exactly like a helper generated without
    /// the parameter — so widening the capability does not change how an ordinary call clones.
    /// <br/><br/>
    /// Capability is derived from the model the helper is generated from, never from the member being
    /// emitted, so signature and call arguments always agree. It is widened (never narrowed) by
    /// <see cref="RequiresIdentityOptOutPropagation"/> because an opt-out has to reach helpers nested
    /// inside the opted-out subgraph. Both inputs are immutable, so the cache is safe and the result
    /// does not depend on the order in which usages are discovered.
    /// </summary>
    public bool HelperAcceptsState(string typeFullName)
    {
        if (_helperAcceptsState.TryGetValue(typeFullName, out bool recorded))
        {
            return recorded;
        }

        bool accepts = RequiresIdentityOptOutPropagation || DefaultStateRequirement(typeFullName);
        _helperAcceptsState[typeFullName] = accepts;
        return accepts;
    }

    /// <summary>
    /// Whether a nested cloner call passes a state argument.
    /// <br/><br/>
    /// Registered types are cloned through the context's <c>Clone</c> methods, which always offer both
    /// a state-taking and a stateless overload, so those calls keep the argument decision they had
    /// before, widened by the opt-out capability. A generated helper has exactly one signature shared
    /// by every call site, so its argument count must be precisely <see cref="HelperAcceptsState"/>.
    /// </summary>
    public bool HelperTakesState(string helperMethodName, string typeFullName, bool previousDecision)
    {
        return HelperAcceptsState(typeFullName) || (helperMethodName == "Clone" && previousDecision);
    }

    /// <summary>
    /// State requirement of the type's own cloning, mirroring how <c>GenerateHelpers</c> picks the
    /// writer: implicit models become implicit clone methods, everything else uses the member model
    /// registered for the type.
    /// </summary>
    private bool DefaultStateRequirement(string typeFullName)
    {
        if (_implicitTypeModels.TryGetValue(typeFullName, out TypeModel implicitModel))
        {
            return implicitModel.NeedsStateTracking && NeedsStateTracking;
        }

        if (_typeNameToMemberModel.TryGetValue(typeFullName, out MemberModel member))
        {
            return MemberCloneGenerator.MemberNeedsCircularRefTracking(this, member);
        }

        return false;
    }

    public string GetMethodName(string typeName)
    {
        return _typeNameToMethodName[typeName];
    }

    public void RegisterImplicitType(TypeModel model)
    {
        // Registration only indexes the model: capability was finalized in the constructor, so a
        // late discovery can never invalidate a helper decision that has already been taken.
        IndexTypeName(_implicitTypeModels, model.FullyQualifiedName, model, model.IsStruct);
    }

    private static bool ScanForIdentityOptOut(TypeModel model, IEnumerable<TypeModel>? additionalModels)
    {
        if (HasOptOutMember(model.Members))
        {
            return true;
        }

        foreach (TypeModel related in model.RelatedTypes)
        {
            if (HasOptOutMember(related.Members))
            {
                return true;
            }
        }

        foreach (TypeModel derived in model.DerivedTypes)
        {
            if (HasOptOutMember(derived.Members))
            {
                return true;
            }
        }

        if (additionalModels != null)
        {
            foreach (TypeModel additional in additionalModels)
            {
                if (HasOptOutMember(additional.Members))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasOptOutMember(IEnumerable<MemberModel> members)
    {
        foreach (MemberModel member in members)
        {
            if (member.PreserveIdentity == false)
            {
                return true;
            }
        }

        return false;
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
