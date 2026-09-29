using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FastCloner.SourceGenerator;

internal sealed class CloneCodeGenerator
{
    private readonly CloneGeneratorContext _context;
    private readonly EquatableArray<GenericUsage> _usages;
    private readonly EquatableArray<ClosedSubtypeUsage> _subtypeUsages;

    public CloneCodeGenerator(TypeModel model, EquatableArray<GenericUsage> usages, EquatableArray<ClosedSubtypeUsage> subtypeUsages, BridgeContract bridgeContract)
    {
        _context = new CloneGeneratorContext(model, bridgeContract);
        _usages = usages;
        _subtypeUsages = subtypeUsages;
    }

    public string Generate()
    {
        PreAnalyzeHelperUsages();
        WriteFileHeader();
        WriteUsings();
        WriteNamespace();
        WriteExtensionClass();
        WriteFileFooter();

        return _context.Source.ToString();
    }
    
    public IReadOnlyList<string> SkippedNonPublicMembers => _context.SkippedNonPublicMembers;

    /// <summary>
    /// Reasons why an explicit identity-preserving operation cannot be guaranteed for this root
    /// (empty when the generated graph can carry one tracking state).
    /// </summary>
    public IReadOnlyList<string> RuntimeBoundaryReasons => _context.RuntimeBoundaryReasons;

    public bool IdentityPreservationRequired => _context.IdentityPreservationRequired;

    /// <summary>
    /// True when a preserving discovery surface names this root directly (as opposed to being
    /// required only because another root's graph reaches it).
    /// </summary>
    public bool ExplicitIdentityOperationRequested => _context.ExposesIdentityOperation;

    private void PreAnalyzeHelperUsages()
    {
        AnalyzeMembers(_context.Model.Members);
        
        if (_context.Model.RelatedTypes != null)
        {
            foreach (TypeModel? related in _context.Model.RelatedTypes)
            {
                AnalyzeMembers(related.Members);
            }
        }

        // A collection of the root's own type parameter is cloned through the generated Cloner<T>
        // helper. That class is written before the collection helpers exist, so the decision has to
        // be taken here. Only the decision is taken here: the helper usage counts above stay exactly
        // as they were, so inlining behavior is unaffected.
        DetectSoleTypeParameterClonerUsages(_context.Model.Members);
        DetectSoleTypeParameterClonerUsages(_context.Model.NestedTypes);

        if (_context.Model.RelatedTypes != null)
        {
            foreach (TypeModel? related in _context.Model.RelatedTypes)
            {
                DetectSoleTypeParameterClonerUsages(related.Members);
                DetectSoleTypeParameterClonerUsages(related.NestedTypes);
            }
        }
    }

    private void DetectSoleTypeParameterClonerUsages(IEnumerable<MemberModel> members)
    {
        foreach (MemberModel member in members)
        {
            if (_context.Model.UsesSoleTypeParameterCloner(member))
            {
                _context.NeedsClonerClass = true;
            }
        }
    }

    private void AnalyzeMembers(IEnumerable<MemberModel> members)
    {
        foreach (MemberModel member in members)
        {
            switch (member.TypeKind)
            {
                case MemberTypeKind.Implicit:
                    _context.IncrementHelperUsage(member.TypeFullName);
                    break;
                case MemberTypeKind.Collection:
                case MemberTypeKind.Array:
                case MemberTypeKind.MultiDimArray:
                {
                    if (member.ElementTypeName != null)
                    {
                        if (_context.TryGetImplicitTypeModel(member.ElementTypeName, out _) ||
                            _context.TryGetMemberModel(member.ElementTypeName, out _))
                        {
                            _context.IncrementHelperUsage(member.ElementTypeName);
                        }
                    }

                    break;
                }
                case MemberTypeKind.Dictionary:
                {
                    if (member.KeyTypeName != null)
                    {
                        if (_context.TryGetImplicitTypeModel(member.KeyTypeName, out _) ||
                            _context.TryGetMemberModel(member.KeyTypeName, out _))
                        {
                            _context.IncrementHelperUsage(member.KeyTypeName);
                        }
                    }
                
                    if (member.ValueTypeName != null)
                    {
                        if (_context.TryGetImplicitTypeModel(member.ValueTypeName, out _) ||
                            _context.TryGetMemberModel(member.ValueTypeName, out _))
                        {
                            _context.IncrementHelperUsage(member.ValueTypeName);
                        }
                    }

                    break;
                }
            }
        }
    }

    private void WriteFileHeader()
    {
        StringBuilder sb = _context.Source;
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine($"// Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
    }

    private void WriteUsings()
    {
        StringBuilder sb = _context.Source;
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Reflection;");
        sb.AppendLine("using FastCloner.SourceGenerator.Shared;");
        
        if (_context.IsFastClonerAvailable)
        {
            sb.AppendLine("using FastCloner;");
        }

        sb.AppendLine();
    }

    private void WriteNamespace()
    {
        string ns = _context.Model.Namespace;
        if (!string.IsNullOrEmpty(ns))
        {
            _context.Source.AppendLine($"namespace {ns}");
            _context.Source.AppendLine("{");
        }
    }

    private void WriteExtensionClass()
    {
        string typeName = _context.Model.FullyQualifiedName;
        string fullTypeName = _context.Model.FullyQualifiedName;
        StringBuilder sb = _context.Source;
        
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// Extension methods for cloning {_context.Model.Name}.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public static partial class {_context.Model.Name}FastDeepCloneExtensions");
        sb.AppendLine("    {");
        
        WritePublicFastDeepCloneMethod(typeName, fullTypeName);
        WritePrivateFastDeepCloneMethod(typeName, fullTypeName);
        WriteDerivedTypeHelpers();
        WriteClonerClass();
        
        CollectionHelperGenerator.GenerateHelpers(_context);
        
        EmitNonPublicAccessorBlock(sb);

        // Emitted last: whether the operation-level entry point may be offered at all depends on
        // whether generating the rest of the file found a part of the graph that necessarily
        // delegates to the runtime cloner (and therefore cannot carry the supplied state).
        WritePublicFastDeepCloneWithOptionsMethod(typeName);

        sb.AppendLine("    }");
    }

    private void EmitNonPublicAccessorBlock(StringBuilder sb)
    {
        if (_context.NonPublicAccessors.Count == 0)
            return;

        bool isGeneric = _context.Model.TypeParameters.Count > 0;

        if (!isGeneric)
        {
            NonPublicAccessorEmitter.WriteDeclarations(_context, sb, "        ", insideNestedShell: false);
            return;
        }

        string typeParams = $"<{string.Join(", ", _context.Model.TypeParameters)}>";
        string constraints = _context.Model.TypeConstraints.Count == 0
            ? string.Empty
            : " " + string.Join(" ", _context.Model.TypeConstraints);

        sb.AppendLine();
        sb.AppendLine($"        private static class __FcAccessors{typeParams}{constraints}");
        sb.AppendLine("        {");
        NonPublicAccessorEmitter.WriteDeclarations(_context, sb, "            ", insideNestedShell: true);
        sb.AppendLine("        }");
    }

    private void WritePublicFastDeepCloneMethod(string typeName, string fullTypeName)
    {
        StringBuilder sb = _context.Source;
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// Performs deep clone of {_context.Model.Name}. This is a source generated method.");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        /// <param name=\"source\">The object to clone.</param>");

        string typeParams = GetTypeParametersString();
        string constraints = GetTypeConstraintsString();
        bool isStruct = _context.Model.IsStruct;
        bool trustNullability = _context.Model.TrustNullability;
        string returnTypeSuffix = isStruct ? "" : "?";
        string paramTypeSuffix = (isStruct || trustNullability) ? "" : "?";
        string notNullAttr = CloneGeneratorContext.NotNullIfNotNullAttr(_context.Model.CodeAnalysisAvailable && !isStruct);
        if (!string.IsNullOrEmpty(notNullAttr))
            sb.AppendLine($"        {notNullAttr}");
        sb.AppendLine($"        public static {typeName}{returnTypeSuffix} FastDeepClone{typeParams}(this {typeName}{paramTypeSuffix} source){constraints}");
        sb.AppendLine("        {");
        
        bool hasInitOnlyWithCycles = _context.CanHaveCircularReferences && _context.Model.Members.Any(m => m.IsInitOnly);
        bool structWithReadonlyRefs = _context.Model.IsStruct && _context.Model.Members.Any(m => m is { IsValueType: false, IsReadOnly: true });

        if (!_context.Model.IsRefLikeType && _context.IsFastClonerAvailable && (hasInitOnlyWithCycles || structWithReadonlyRefs))
        {
             _context.RecordRuntimeBoundary(
                 "the whole type is cloned by the runtime cloner because of " +
                 (hasInitOnlyWithCycles
                     ? "init-only members combined with circular reference tracking"
                     : "readonly reference fields in a struct"));
             sb.AppendLine($"            return {CloneGeneratorContext.FastClonerDeepCloneCall("source")};");
             sb.AppendLine("        }");
             sb.AppendLine();
             return;
        }
        
        if (_context.Model.IsPolymorphicRoot)
        {
            WritePolymorphicRootPublicBody(typeName, fullTypeName);
        }
        else if (_context.Model.IsAbstract)
        {
            sb.AppendLine("            return InternalFastDeepClone(source, null);");
        }
        else if (!_context.NeedsStateTracking)
        {
            if (isStruct)
            {
                WriteStructCloneBodyDirect(typeName);
            }
            else
            {
                if (!trustNullability)
                {
                    sb.AppendLine("            if (source == null) return null;");
                }
                WriteCloneBody(typeName, fullTypeName, false);
            }
        }
        else
        {
            sb.AppendLine("            return InternalFastDeepClone(source, null);");
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    /// <summary>
    /// Emits the operation-level entry point that can positively require identity preservation for a
    /// single call. The default entry point above keeps its existing behavior and fast path: a
    /// tracking state is only allocated when the caller asks for it, so ordinary calls pay nothing.
    /// <br/><br/>
    /// Two conditions decide whether it is offered at all:
    /// <br/><br/>
    /// - this root has a reason of its own to expose the operation
    ///   (<see cref="CloneGeneratorContext.ExposesIdentityOperation"/>): it is directly required by a
    ///   <c>PreserveIdentity = true</c> discovery surface, or it configures identity itself. Merely
    ///   being state capable — for cycles, or because another preserving root's graph reaches it —
    ///   is not a reason; and
    /// - generating the rest of the file proved the graph can carry one tracking state across
    ///   everything it deep clones. If some part necessarily delegates to the runtime cloner (which
    ///   runs its own state), the call is not offered: a caller cannot silently receive a clone that
    ///   ignores the requirement.
    /// </summary>
    private void WritePublicFastDeepCloneWithOptionsMethod(string typeName)
    {
        if (!_context.ExposesIdentityOperation || !_context.StateCapable || _context.RuntimeBoundaryReasons.Count > 0)
            return;

        StringBuilder sb = _context.Source;
        string typeParams = GetTypeParametersString();
        string constraints = GetTypeConstraintsString();
        bool isStruct = _context.Model.IsStruct;
        bool trustNullability = _context.Model.TrustNullability;
        string returnTypeSuffix = isStruct ? "" : "?";
        string paramTypeSuffix = (isStruct || trustNullability) ? "" : "?";

        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// Performs deep clone of {_context.Model.Name} honoring the requested operation options.");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        /// <param name=\"source\">The object to clone.</param>");
        sb.AppendLine($"        /// <param name=\"options\">Requirements for this operation.</param>");
        sb.AppendLine($"        /// <remarks>");
        sb.AppendLine($"        /// {GeneratedTypeNames.FastCloneOptions}.PreserveIdentity requires reference topology");
        sb.AppendLine($"        /// preservation for this call regardless of the type's default. Without it the call is");
        sb.AppendLine($"        /// identical to FastDeepClone(source), so the type's default behavior is never changed.");
        sb.AppendLine($"        /// This overload is only generated when the whole graph this implementation clones can");
        sb.AppendLine($"        /// be carried by one tracking state, so the guarantee covers everything actually");
        sb.AppendLine($"        /// deep cloned by the operation.");
        sb.AppendLine($"        /// </remarks>");

        string notNullAttr = CloneGeneratorContext.NotNullIfNotNullAttr(_context.Model.CodeAnalysisAvailable && !isStruct);
        if (!string.IsNullOrEmpty(notNullAttr))
            sb.AppendLine($"        {notNullAttr}");

        sb.AppendLine($"        public static {typeName}{returnTypeSuffix} FastDeepClone{typeParams}(this {typeName}{paramTypeSuffix} source, {GeneratedTypeNames.FastCloneOptions} options){constraints}");
        sb.AppendLine("        {");
        sb.AppendLine($"            if ((options & {GeneratedTypeNames.FastCloneOptions}.PreserveIdentity) == 0)");
        sb.AppendLine("            {");
        sb.AppendLine($"                return FastDeepClone{typeParams}(source);");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine($"            return InternalFastDeepClone{typeParams}(source, new {GeneratedTypeNames.CloneState}(preservingOperation: true));");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void WritePrivateFastDeepCloneMethod(string typeName, string fullTypeName)
    {
        StringBuilder sb = _context.Source;
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// Performs deep clone of {_context.Model.Name} with circular reference tracking.");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        /// <param name=\"source\">The object to clone.</param>");
        sb.AppendLine($"        /// <param name=\"state\">State for circular reference tracking. If null, a new state is created.</param>");

        string typeParams = GetTypeParametersString();
        string constraints = GetTypeConstraintsString();
        
        bool isStruct = _context.Model.IsStruct;
        bool trustNullability = _context.Model.TrustNullability;
        string returnTypeSuffix = isStruct ? "" : "?";
        string paramTypeSuffix = (isStruct || trustNullability) ? "" : "?";

        sb.AppendLine($"        internal static {typeName}{returnTypeSuffix} InternalFastDeepClone{typeParams}(this {typeName}{paramTypeSuffix} source, {GeneratedTypeNames.CloneState}? state){constraints}");
        sb.AppendLine("        {");
        
        bool hasInitOnlyWithCycles = _context.CanHaveCircularReferences && _context.Model.Members.Any(m => m.IsInitOnly);
        bool structWithReadonlyRefs = _context.Model.IsStruct && _context.Model.Members.Any(m => m is { IsValueType: false, IsReadOnly: true });
        
        if (!_context.Model.IsRefLikeType && _context.IsFastClonerAvailable && (hasInitOnlyWithCycles || structWithReadonlyRefs))
        {
             _context.RecordRuntimeBoundary(
                 "the whole type is cloned by the runtime cloner because of " +
                 (hasInitOnlyWithCycles
                     ? "init-only members combined with circular reference tracking"
                     : "readonly reference fields in a struct"));
             sb.AppendLine("            // Fallback to runtime cloning due to complex language features.");
             sb.AppendLine("            // Note: State is ignored here as the runtime handles its own circular reference tracking.");
             sb.AppendLine($"            return {CloneGeneratorContext.FastClonerDeepCloneCall("source")};");
             sb.AppendLine("        }");
             sb.AppendLine();
             return;
        }
        
        if (!isStruct && !trustNullability)
        {
            sb.AppendLine("            if (source == null) return null;");
        }
        
        if (_context.Model.IsPolymorphicRoot)
        {
            WritePolymorphicRootDispatcher(typeName, fullTypeName);
        }
        else if (_context.Model.IsAbstract)
        {
            WriteAbstractTypeDispatcher(typeName);
        }
        else if (_context.NeedsStateTracking)
        {
            _context.NeedsStateClass = true;
            sb.AppendLine($"            var localState = state ?? new {GeneratedTypeNames.CloneState}();");

            if (!_context.Model.IsStruct)
            {
                sb.AppendLine("            var known = localState.GetKnownRef(source);");
                sb.AppendLine($"            if (known != null) return ({typeName})known;");
            }
            
            WriteCloneBody(typeName, fullTypeName, true, "localState");
        }
        else
        {
            _context.NeedsStateClass = true;
            sb.AppendLine("            if (state != null)");
            sb.AppendLine("            {");
            if (!_context.Model.IsStruct)
            {
                sb.AppendLine("                var known = state.GetKnownRef(source);");
                sb.AppendLine($"                if (known != null) return ({typeName})known;");
            }
            sb.AppendLine("            }");
            WriteCloneBody(typeName, fullTypeName, true, "state");
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    /// <summary>
    /// Derived types for dispatch: declaration-scanned subtypes from the model plus
    /// closed constructions of generic subtypes discovered from usages (TypedRepo&lt;int&gt;),
    /// deduplicated by fully qualified name and appended in a deterministic order.
    /// </summary>
    private List<TypeModel> GetEffectiveDerivedTypes()
    {
        List<TypeModel> derivedTypes = [.. _context.Model.DerivedTypes];

        if (_subtypeUsages.Count > 0)
        {
            HashSet<string> known = [.. derivedTypes.Select(t => t.FullyQualifiedName)];

            foreach (ClosedSubtypeUsage usage in _subtypeUsages
                         .Where(u => u.RootFqn == _context.Model.FullyQualifiedName)
                         .OrderBy(u => u.Model.FullyQualifiedName, StringComparer.Ordinal))
            {
                if (known.Add(usage.Model.FullyQualifiedName))
                {
                    derivedTypes.Add(usage.Model);
                }
            }
        }

        return derivedTypes;
    }

    private void WriteAbstractTypeDispatcher(string typeName)
    {
        StringBuilder sb = _context.Source;
        IReadOnlyList<TypeModel> derivedTypes = GetEffectiveDerivedTypes();

        sb.AppendLine();
        sb.AppendLine("            // Dispatch to concrete type cloner based on runtime type");
        sb.AppendLine("            var runtimeType = source.GetType();");
        sb.AppendLine();

        WriteDerivedTypeDispatchBranches(typeName, derivedTypes);

        if (_context.IsFastClonerAvailable)
        {
            _context.RecordRuntimeBoundary(
                "an unknown derived type of '" + _context.Model.Name + "' is cloned by the runtime cloner, which cannot be given the generated state");
            sb.AppendLine($"            return ({typeName}){CloneGeneratorContext.FastClonerDeepCloneCall("source")}!;");
        }
        else
        {
            sb.AppendLine($"            throw new InvalidOperationException($\"Cannot clone unknown derived type {{runtimeType.FullName}} of {_context.Model.Name}. \" +");
            sb.AppendLine("                \"Either add the derived type to this assembly, use [FastClonerInclude] to register it, \" +");
            sb.AppendLine("                \"or install the FastCloner NuGet package for runtime fallback.\");");
        }
    }

    /// <summary>
    /// Emits the public FastDeepClone body for a non-abstract polymorphic root.
    /// The exact root type is checked first and its body is inlined, so cloning the root
    /// itself costs a single type check over the plain clonable path; only actual
    /// subtypes pay for dispatch by falling through to the internal dispatcher.
    /// </summary>
    private void WritePolymorphicRootPublicBody(string typeName, string fullTypeName)
    {
        StringBuilder sb = _context.Source;

        if (!_context.Model.TrustNullability)
        {
            sb.AppendLine("            if (source == null) return null;");
        }

        sb.AppendLine($"            if (source.GetType() != typeof({fullTypeName}))");
        sb.AppendLine("            {");
        sb.AppendLine("                return InternalFastDeepClone(source, null)!;");
        sb.AppendLine("            }");
        sb.AppendLine();

        if (_context.NeedsStateTracking)
        {
            _context.NeedsStateClass = true;
            sb.AppendLine($"            var localState = new {GeneratedTypeNames.CloneState}();");
            sb.AppendLine("            var known = localState.GetKnownRef(source);");
            sb.AppendLine($"            if (known != null) return ({typeName})known;");
            sb.AppendLine();
            WriteCloneBody(typeName, fullTypeName, true, "localState");
        }
        else
        {
            WriteCloneBody(typeName, fullTypeName, false);
        }
    }

    /// <summary>
    /// Emits the InternalFastDeepClone body for a non-abstract polymorphic root:
    /// subtype dispatch branches, then an unknown-subtype guard, then the state-aware
    /// root body. The guard structure keeps the root body at standard indentation.
    /// </summary>
    private void WritePolymorphicRootDispatcher(string typeName, string fullTypeName)
    {
        StringBuilder sb = _context.Source;
        IReadOnlyList<TypeModel> derivedTypes = GetEffectiveDerivedTypes();

        sb.AppendLine();
        sb.AppendLine("            // Dispatch to the cloner matching the runtime type (polymorphic root)");
        sb.AppendLine("            var runtimeType = source.GetType();");
        sb.AppendLine();

        WriteDerivedTypeDispatchBranches(typeName, derivedTypes);

        // Neither a known subtype nor the exact root type: unknown subtype fallback,
        // identical to the abstract dispatcher tail.
        sb.AppendLine($"            if (runtimeType != typeof({fullTypeName}))");
        sb.AppendLine("            {");
        if (_context.IsFastClonerAvailable)
        {
            _context.RecordRuntimeBoundary(
                "an unknown derived type of '" + _context.Model.Name + "' is cloned by the runtime cloner, which cannot be given the generated state");
            sb.AppendLine($"                return ({typeName}){CloneGeneratorContext.FastClonerDeepCloneCall("source")}!;");
        }
        else
        {
            sb.AppendLine($"                throw new InvalidOperationException($\"Cannot clone unknown derived type {{runtimeType.FullName}} of {_context.Model.Name}. \" +");
            sb.AppendLine("                    \"Either add the derived type to this assembly, use [FastClonerInclude] to register it, \" +");
            sb.AppendLine("                    \"or install the FastCloner NuGet package for runtime fallback.\");");
        }
        sb.AppendLine("            }");
        sb.AppendLine();

        // Exact root type: the same body a plain clonable generates.
        if (_context.NeedsStateTracking)
        {
            _context.NeedsStateClass = true;
            sb.AppendLine($"            var localState = state ?? new {GeneratedTypeNames.CloneState}();");
            sb.AppendLine("            var known = localState.GetKnownRef(source);");
            sb.AppendLine($"            if (known != null) return ({typeName})known;");
            sb.AppendLine();
            WriteCloneBody(typeName, fullTypeName, true, "localState");
        }
        else
        {
            _context.NeedsStateClass = true;
            sb.AppendLine("            if (state != null)");
            sb.AppendLine("            {");
            sb.AppendLine("                var known = state.GetKnownRef(source);");
            sb.AppendLine($"                if (known != null) return ({typeName})known;");
            sb.AppendLine("            }");
            WriteCloneBody(typeName, fullTypeName, true, "state");
        }
    }

    private void WriteDerivedTypeDispatchBranches(string typeName, IReadOnlyList<TypeModel> derivedTypes)
    {
        StringBuilder sb = _context.Source;

        // Inside a generic extension method (Repo<T>) no direct cast to a concrete subtype
        // exists, so casts to and from subtypes must roundtrip through object.
        bool isGenericRoot = _context.Model.TypeParameters.Count > 0;

        foreach (TypeModel? derivedType in derivedTypes)
        {
            string derivedTypeName = derivedType.FullyQualifiedName;
            string sourceCast = isGenericRoot ? $"({derivedTypeName})(object)source" : $"({derivedTypeName})source";
            string resultCast = isGenericRoot ? $"({typeName})(object)" : $"({typeName})";

            if (derivedType.Members.Count == 0)
            {
                // global:: prefix is required: without it a consumer namespace like Foo.Bar,
                // where a type named Foo exists in namespace Foo, hijacks the first segment
                // and the generated reference fails to compile (CS0117).
                string extensionClassName = string.IsNullOrEmpty(derivedType.Namespace)
                    ? $"global::{derivedType.Name}FastDeepCloneExtensions"
                    : $"global::{derivedType.Namespace}.{derivedType.Name}FastDeepCloneExtensions";

                sb.AppendLine($"            if (runtimeType == typeof({derivedTypeName}))");
                sb.AppendLine($"                return {resultCast}{extensionClassName}.InternalFastDeepClone({sourceCast}, state)!;");
            }
            else
            {
                string helperName = _context.RegisterDerivedTypeHelper(derivedType, $"Clone{GetSafeTypeName(derivedType.Name)}");
                // Helper methods on generic roots carry the root's type parameters so that
                // collection helpers invoked inside them (Helper<T>(...)) resolve.
                string helperTypeParams = GetTypeParametersString();

                sb.AppendLine($"            if (runtimeType == typeof({derivedTypeName}))");
                sb.AppendLine($"                return {resultCast}{helperName}{helperTypeParams}({sourceCast}, state);");
            }
            sb.AppendLine();
        }
    }

    private static string GetSafeTypeName(string typeName)
    {
        return typeName
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_')
            .Replace(' ', '_')
            .Replace('.', '_')
            .Replace('[', '_')
            .Replace(']', '_')
            .Replace('?', '_');
    }

    private void WriteDerivedTypeHelpers()
    {
        if (!_context.HasDerivedTypeHelpers)
            return;

        StringBuilder sb = _context.Source;

        foreach ((TypeModel derivedModel, string methodName) in _context.GetDerivedTypeHelpers())
        {
            // Generic roots: the helper shares the root's type parameters (unused in its
            // signature) so member cloning inside it can call generic collection helpers.
            string helperTypeParams = GetTypeParametersString();

            sb.AppendLine();
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// Clones a {derivedModel.Name} instance (auto-generated for abstract base class).");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        private static {derivedModel.FullyQualifiedName} {methodName}{helperTypeParams}({derivedModel.FullyQualifiedName} source, {GeneratedTypeNames.CloneState}? state)");
            sb.AppendLine("        {");
            
            if (derivedModel.NeedsStateTracking || _context.StateCapable)
            {
                _context.NeedsStateClass = true;
                sb.AppendLine($"            var localState = state ?? new {GeneratedTypeNames.CloneState}();");
                sb.AppendLine("            var known = localState.GetKnownRef(source);");
                sb.AppendLine($"            if (known != null) return ({derivedModel.FullyQualifiedName})known;");
                sb.AppendLine();
                
                WriteDerivedTypeCloneBody(derivedModel, true, "localState");
            }
            else
            {
                WriteDerivedTypeCloneBody(derivedModel, false, "state");
            }

            sb.AppendLine("        }");
        }
    }

    private void WriteDerivedTypeCloneBody(TypeModel derivedModel, bool useState, string stateVarName)
    {
        StringBuilder sb = _context.Source;
        string typeName = derivedModel.FullyQualifiedName;

        if (derivedModel.IsStruct)
        {
            sb.AppendLine($"            var result = source;");
            sb.AppendLine();

            foreach (MemberModel member in derivedModel.Members)
            {
                MemberCloneGenerator.WriteMemberCloning(_context, member, "result", "source", stateVarName);
            }

            sb.AppendLine("            return result;");
        }
        else
        {
            if (derivedModel.HasParameterlessConstructor)
            {
                if (useState)
                {
                    ClassCloneBodyGenerator.WriteNewWithObjectInitializer(
                        sb,
                        typeName,
                        ClassCloneBodyGenerator.CollectObjectInitializerAssignments(_context, derivedModel.Members, "source", stateVarName));

                    sb.AppendLine($"            {stateVarName}?.AddKnownRef(source, result);");
                    sb.AppendLine();

                    foreach (MemberModel member in derivedModel.Members)
                    {
                        if (ClassCloneBodyGenerator.MustAssignInObjectInitializer(member))
                            continue;

                        MemberCloneGenerator.WriteMemberCloning(_context, member, "result", "source", stateVarName);
                    }

                    sb.AppendLine();
                    sb.AppendLine("            return result;");
                }
                else
                {
                    sb.AppendLine($"            var result = new {typeName}");
                    sb.AppendLine("            {");

                    List<string> memberAssignments = [];
                    foreach (MemberModel member in derivedModel.Members)
                    {
                        string assignment = MemberCloneGenerator.GetMemberAssignment(_context, member, "source", "null", "                ");
                        if (!string.IsNullOrEmpty(assignment))
                        {
                            memberAssignments.Add($"                {assignment}");
                        }
                    }

                    if (memberAssignments.Count > 0)
                    {
                        sb.AppendLine(string.Join(",\n", memberAssignments));
                    }

                    sb.AppendLine("            };");
                    sb.AppendLine();
                    sb.AppendLine("            return result;");
                }
            }
            else
            {
                ClassCloneBodyGenerator.WriteGetUninitializedObject(sb, typeName);

                if (useState)
                {
                    sb.AppendLine($"            {stateVarName}?.AddKnownRef(source, result);");
                }

                sb.AppendLine();

                // GetUninitializedObject: no constructor ran (issue #48 — populate without
                // invoking property setters and share weaver state).
                foreach (MemberModel member in derivedModel.Members)
                {
                    MemberCloneGenerator.WriteMemberCloning(_context, member, "result", "source", stateVarName, instanceCreatedWithoutConstructor: true);
                }

                sb.AppendLine();
                sb.AppendLine("            return result;");
            }
        }
    }

    private string GetTypeParametersString()
    {
        return _context.Model.TypeParameters.Count == 0 ? string.Empty : $"<{string.Join(", ", _context.Model.TypeParameters)}>";
    }

    private string GetTypeConstraintsString()
    {
        if (_context.Model.TypeConstraints.Count == 0)
            return string.Empty;

        return " " + string.Join(" ", _context.Model.TypeConstraints);
    }

    private void WriteCloneBody(string typeName, string fullTypeName, bool useState, string? stateVarName = null)
    {
        if (_context.Model is { IsStruct: true, IsRecord: false })
        {
            WriteStructCloneBody(typeName, stateVarName ?? "state");
        }
        else
        {
            WriteClassCloneBody(typeName, fullTypeName, useState, stateVarName);
        }
    }

    private void WriteStructCloneBody(string typeName, string stateVarName = "state")
    {
        StringBuilder sb = _context.Source;
        sb.AppendLine($"            var result = source;");
        sb.AppendLine();

        foreach (MemberModel member in _context.Model.Members)
        {
            MemberCloneGenerator.WriteMemberCloning(_context, member, "result", "source", stateVarName);
        }

        sb.AppendLine("            return result;");
    }
    
    private void WriteStructCloneBodyDirect(string typeName)
    {
        StringBuilder sb = _context.Source;
        
        if (_context.Model.IsRecord)
        {
            List<string> deepCloneAssignments = [];
            foreach (MemberModel member in _context.Model.Members)
            {
                if (member.TypeKind == MemberTypeKind.Safe)
                    continue;
                if (member.IsReadOnly)
                    continue;
                
                string assignment = MemberCloneGenerator.GetMemberAssignment(_context, member, "source", "null", "                ");
                if (!string.IsNullOrEmpty(assignment))
                {
                    deepCloneAssignments.Add($"                {assignment}");
                }
            }
            
            if (deepCloneAssignments.Count == 0)
            {
                sb.AppendLine($"            return source with {{ }};");
            }
            else
            {
                sb.AppendLine($"            return source with");
                sb.AppendLine("            {");
                sb.AppendLine(string.Join(",\n", deepCloneAssignments));
                sb.AppendLine("            };");
            }
        }
        else
        {
            sb.AppendLine($"            var result = source;");
            sb.AppendLine();

            foreach (MemberModel member in _context.Model.Members)
            {
                MemberCloneGenerator.WriteMemberCloning(_context, member, "result", "source", "null");
            }

            sb.AppendLine("            return result;");
        }
    }

    private void WriteClassCloneBody(string typeName, string fullTypeName, bool useState, string? stateVarName = null)
    {
        ClassCloneBodyGenerator.WriteClassCloneBody(_context, typeName, useState, stateVarName, useNullConditional: true, sourceVarName: "source");
    }
    
    private void WriteClonerClass()
    {
        if (!_context.NeedsClonerClass)
            return;

        StringBuilder sb = _context.Source;
        sb.AppendLine();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Helper class for cloning generic types.");
        sb.AppendLine("        /// </summary>");

        string typeParams = GetTypeParametersString();
        string constraints = GetTypeConstraintsString();
        
        string[]? typeParamsArray = _context.Model.TypeParameters.GetArray();
        if (typeParamsArray == null || typeParamsArray.Length == 0)
        {
            sb.AppendLine($"        private static class Cloner<T>");
            sb.AppendLine("        {");
            sb.AppendLine($"            public static T Clone(T source, {GeneratedTypeNames.CloneState}? state)");
        }
        else
        {
            sb.AppendLine($"        private static class Cloner{typeParams}{constraints}");
            sb.AppendLine("        {");

            string firstTypeParam = typeParamsArray[0];
            sb.AppendLine($"            public static {firstTypeParam} Clone({firstTypeParam} source, {GeneratedTypeNames.CloneState}? state)");
        }
        
        sb.AppendLine("            {");
        sb.AppendLine("                if (source == null) return default!;");
        
        foreach (GenericUsage usage in _usages)
        {
            if (usage.GenericTypeMetadataName != _context.Model.FullyQualifiedName)
                continue;
            
            foreach (MemberModel nested in usage.NestedHelpers)
            {
                _context.GetOrCreateHelperMethodName(nested);
            }
            foreach (TypeModel? implicitType in usage.ImplicitTypes)
            {
                _context.RegisterImplicitType(implicitType);
                _context.GetOrCreateHelperMethodName(implicitType.FullyQualifiedName);
            }
                
            string argType = usage.ArgumentTypeMetadataName;
            string castTypeParam = typeParamsArray == null || typeParamsArray.Length == 0
                ? "T" 
                : typeParamsArray[0];
            
            if (usage.IsSafe)
            {
                sb.AppendLine($"                if (typeof({castTypeParam}) == typeof({argType})) return ({castTypeParam})(object)source;");
            }
            else if (usage.IsClonable && !string.IsNullOrEmpty(usage.ExtensionClassFQN))
            {
                sb.AppendLine($"                if (typeof({castTypeParam}) == typeof({argType}))");

                if (usage.IsDeclaredInCompilation)
                {
                    sb.AppendLine($"                    return ({castTypeParam})(object){usage.ExtensionClassFQN}.InternalFastDeepClone(({argType})(object)source, state)!;");
                }
                else
                {
                    // The argument's own generated entry point lives in another assembly, where
                    // InternalFastDeepClone is internal. The public entry point starts a fresh
                    // tracking state, so the operation cannot be guaranteed across that boundary.
                    _context.RecordRuntimeBoundary(
                        $"'{argType}' is a clonable type from a referenced assembly, whose state-aware entry point is not accessible");
                    sb.AppendLine($"                    return ({castTypeParam})(object)({usage.ExtensionClassFQN}.FastDeepClone(({argType})(object)source)!);");
                }
            }
            else if (usage.CollectionModel != null)
            {
                MemberModel collectionModel = usage.CollectionModel.Value;
                string helperName = _context.GetOrCreateHelperMethodName(collectionModel);
                bool needsState = _context.MemberCanTrack(collectionModel);
                
                string callArgs = needsState 
                    ? $"(({argType})(object)source, state)" 
                    : $"(({argType})(object)source)";

                sb.AppendLine($"                if (typeof({castTypeParam}) == typeof({argType}))");
                sb.AppendLine($"                    return ({castTypeParam})(object){helperName}{typeParams}{callArgs}!;");
            }
            else
            {
                if (_context.TryGetImplicitTypeModel(argType, out TypeModel implicitModel))
                {
                     string helperName = _context.GetOrCreateHelperMethodName(argType);
                     bool needsState = _context.ImplicitHelperNeedsState(implicitModel);
                     string callArgs = needsState 
                        ? $"(({argType})(object)source, state)" 
                        : $"(({argType})(object)source)";

                     sb.AppendLine($"                if (typeof({castTypeParam}) == typeof({argType}))");
                     sb.AppendLine($"                    return ({castTypeParam})(object){helperName}{typeParams}{callArgs}!;");
                }
            }
        }
        
        string fallbackCastTypeParam = (typeParamsArray == null || typeParamsArray.Length == 0)
            ? "T" 
            : typeParamsArray[0];
        
        if (_context.IsFastClonerAvailable)
        {
            // Any closed argument that matches none of the branches above is cloned by the runtime
            // with its own tracking state. Only worth recording separately for generic roots: for a
            // closed type the member-level reason already names the offending member.
            if (_context.Model.TypeParameters.Count > 0)
            {
                _context.RecordRuntimeBoundary(
                    "a generic member is cloned by the runtime cloner ('Cloner<T>' fallback) because its closed type has no generated model");
            }

            sb.AppendLine($"                return ({fallbackCastTypeParam}){CloneGeneratorContext.FastClonerDeepCloneCall("source")}!;");
        }
        else
        {
            sb.AppendLine("                return source;");
        }
        sb.AppendLine("            }");

        sb.AppendLine("        }");
    }

    private void WriteFileFooter()
    {
        if (!string.IsNullOrEmpty(_context.Model.Namespace))
        {
            _context.Source.AppendLine("}");
        }
    }
}
