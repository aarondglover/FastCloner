# Generic argument discovery points

## Summary

Allow generic declarations and API surfaces to opt into FastCloner source-generation discovery.

A marked API surface becomes a discovery point: when FastCloner observes it being used in a closed generic form, the closed generic arguments are treated as clone roots and passed into the existing source-generation analysis pipeline.

Proposed API:

```csharp
[FastClonerDiscoverGenericArguments]
```

with optional identity preservation:

```csharp
[FastClonerDiscoverGenericArguments(PreserveIdentity = true)]
```

## Motivation

FastCloner already discovers closed generic usages for types it knows are cloning roots. The missing capability is a reusable way to declare that a generic API surface itself should act as the discovery point.

For example:

```csharp
[FastClonerDiscoverGenericArguments]
public interface IContainer<T>
{
}
```

A consumer usage such as:

```csharp
IContainer<Order>
```

would cause `Order` to become a FastCloner source-generation root.

The same concept should apply consistently across generic API shapes:

```csharp
[FastClonerDiscoverGenericArguments]
public sealed class Pipeline<TInput, TOutput>
{
}
```

```csharp
[FastClonerDiscoverGenericArguments]
public delegate TResult Processor<TSource, TResult>(TSource source);
```

```csharp
public interface IStage<T>
{
    [FastClonerDiscoverGenericArguments]
    void Execute();
}
```

```csharp
public static class Operations
{
    [FastClonerDiscoverGenericArguments]
    public static void Execute<T1, T2, T3>()
    {
    }
}
```

Closed usages such as:

```csharp
Pipeline<Order, Result>
Processor<Customer, CustomerSnapshot>
IStage<Form>
Operations.Execute<Request, Response, Context>()
```

would expose the relevant closed generic arguments as clone roots.

## Discovery semantics

The attribute is a source-generation discovery declaration, not a new cloning mechanism.

When a marked API surface is observed in a closed generic context:

1. Resolve the closed generic arguments represented by that usage.
2. Treat all relevant closed generic arguments as clone roots.
3. Feed those roots into FastCloner's existing type analysis and code-generation pipeline.
4. Reuse existing recursive discovery for nested types, collections, dictionaries and supported polymorphic paths.

For a marked method declared on a generic containing type, both sources of closed generic information should be considered:

- generic arguments belonging to the method itself;
- generic arguments belonging to the closed containing type.

Example:

```csharp
public interface IStage<TContext>
{
    [FastClonerDiscoverGenericArguments]
    void Execute<TInput, TOutput>();
}
```

A closed use of:

```csharp
IStage<Context>.Execute<Request, Response>()
```

would expose `Context`, `Request`, and `Response` as roots.

Open or unbound generic arguments should not produce roots, consistent with existing FastCloner generic discovery.

## Identity preservation

Some discovery points require graph identity to be preserved during cloning.

The proposed API allows this to be declared alongside discovery:

```csharp
[FastClonerDiscoverGenericArguments(PreserveIdentity = true)]
```

This should reuse FastCloner's existing identity-preservation machinery rather than introduce separate semantics.

For example:

```text
source:                 clone:

A ──► B ◄── C           A' ──► B' ◄── C'
```

The clone is detached from the source graph, while repeated references to the same source object remain repeated references to the same cloned object.

`PreserveIdentity = false` is the default and retains FastCloner's current default behavior.

`PreserveIdentity = true` is a *capability* requirement, not a behavior change: types discovered through the
surface must have generated cloning capability compatible with an operation requiring identity preservation. It
does not make the discovered type preserve identity by default.

The declaration is satisfied by an explicit operation-level request for identity preservation, so the discovered
type's own default cloning behavior is untouched:

```csharp
value.FastDeepClone();                                   // unchanged default behavior
value.FastDeepClone(FastCloneOptions.PreserveIdentity);  // A' ──► B' ◄── C'
```

The explicit request is the strongest identity requirement for that invocation: member and type level
`PreserveIdentity(false)` are defaults for the ordinary call and do not weaken it.

See the implementation notes below for the capability model, for the boundary the generator repairs and the
boundary it reports instead of advertising (a preserving operation is only offered where the generated graph can
actually carry it), and for the pre-existing member-level negative-override limitation against a child type whose
own default is preserving, which stays a separate issue.

## Proposed attribute surface

Initial target set:

```csharp
[AttributeUsage(
    AttributeTargets.Class |
    AttributeTargets.Struct |
    AttributeTargets.Interface |
    AttributeTargets.Delegate |
    AttributeTargets.Method)]
public sealed class FastClonerDiscoverGenericArgumentsAttribute : Attribute
{
    public bool PreserveIdentity { get; init; }
}
```

This is the initial proposed public shape for implementation.

## Design constraints

- Do not require consumer domain types to carry FastCloner attributes solely to become roots discovered through another generic API.
- Do not introduce a second cloning pipeline.
- Do not select only a particular generic parameter by index; all relevant closed generic arguments should be considered.
- Reuse existing FastCloner type modelling, recursive graph analysis and generated cloning paths.
- Keep identity preservation orthogonal to normal clone behavior and implement it using the existing state-tracking machinery.
- Avoid runtime reflection for discovery; this is intended to be a compile-time source-generator capability.

## Likely generator integration

The existing generic usage discovery already inspects closed generic syntax and resolves symbols. This feature should extend that discovery so that a closed usage can also qualify because the referenced declaration or API surface carries `FastClonerDiscoverGenericArgumentsAttribute`.

The implementation should preferably feed discovered types into the same model-building path used by existing generic discovery rather than duplicate type analysis or code generation.

The exact Roslyn pipeline shape should be chosen to preserve FastCloner's current incremental-generation characteristics.

## Repository conventions

Implementation should follow the repository's existing conventions rather than introduce a parallel style or toolchain.

In particular:

- use the existing TUnit test infrastructure and assertion style in `src/FastCloner.Tests`;
- do not add or replace test frameworks or supporting test packages unless the feature genuinely requires something not already available;
- reuse the existing source-generator project structure and shared attribute assembly rather than creating new projects for this feature;
- preserve the current target-framework strategy, nullable settings, language-version choices, signing and packaging conventions;
- follow the repository's existing naming, file placement, namespace, generated-code and incremental-generator patterns;
- prefer extending existing collectors, models and code-generation paths over introducing duplicate discovery or modelling pipelines;
- avoid unrelated package upgrades, formatting churn or refactors;
- keep the implementation narrowly scoped to the discovery feature and its required tests/documentation.

If an implementation choice conflicts with this document but is clearly required by an established repository convention, follow the repository convention and document the reason.

## Validation

Tests should cover at least:

- generic class discovery;
- generic struct discovery;
- generic interface discovery;
- generic delegate discovery;
- generic method discovery;
- a marked method on a generic containing type;
- multiple generic arguments;
- nested generic arguments;
- open generic arguments being ignored;
- `PreserveIdentity = true` preserving shared-reference topology;
- default discovery retaining existing identity behavior;
- referenced-assembly declarations acting as discovery points from a consuming compilation;
- the generated-to-generated state transitions (non-public members, `Cloner<T>` for an included clonable argument,
  a collection of the root's own type parameter) keeping one tracking state;
- a requirement that cannot be served being reported (`FCG013`) with the operation-level entry point withheld, and
  a graph that can be served exposing it with no runtime-cloner escape path.

## Implementation notes

Recorded while implementing the feature; they clarify (without changing) the agreed design.

### Attribute shape

`PreserveIdentity` is declared as `{ get; set; }` rather than `{ get; init; }`. The shared attribute assembly
targets `netstandard2.0` and ships no `IsExternalInit` polyfill, and the sibling
`FastClonerPreserveIdentityAttribute.Enabled` uses `{ get; set; }`; named-argument usage
(`[FastClonerDiscoverGenericArguments(PreserveIdentity = true)]`) is identical either way.

### Discovery is driven from the usage site

The generator resolves discovery points from closed generic *usage* syntax (the same
`CreateSyntaxProvider` + `GenericNameSyntax` shape `GenericUsageCollector` and `SubtypeUsageCollector` use) and
inspects the referenced declaration's attributes there. An `ForAttributeWithMetadataName` pipeline over the
attribute would only ever see declarations in the current compilation, so it could not satisfy the
referenced-assembly requirement. A marked declaration is only a discovery point once observed in a closed form,
which is exactly the requested behavior.

A generic *type* is also a discovery surface when it declares a marked method, which is what makes a closed
`IStage<Form>` usage expose `Form` in the design's `IStage<T>` example. For a marked method, both the method's
own closed arguments and the closed arguments of its containing type are collected.

### Which arguments become roots

A generated entry point is a closed, non-generic `FastDeepClone(this T)` extension, so a root must be a closed,
non-generic type. Consequences, in the order the collector applies them:

- open/unbound arguments (`IContainer<T>`, `typeof(IContainer<>)`) are ignored;
- safe types and "do not clone" types (delegates, `Lazy`/`Task`-style types) report no cloneable state and are
  ignored;
- `object` is ignored explicitly: an extension method on `object` would apply to every receiver and silently
  shallow-copy anything FastCloner has no cloner for;
- constructed generic arguments and collection/dictionary/array arguments are descended instead of rooted, which
  is what exposes `Order` for `IContainer<List<Order>>` and `IContainer<Wrapper<Order>>`. Constructed generic
  ARGUMENTS cannot be roots themselves because a closed construction keeps the definition's type parameters
  (`Wrapper<Order>` still reports `T`), so an entry point generated for it would declare an unusable type
  parameter at every call site;
- a type that already carries `[FastClonerClonable]` is not rooted again: its own pipeline already emitted the
  entry point, and a second one would be a duplicate member of the same extension class. If the discovering
  surface requires identity preservation, the requirement is carried into that existing root instead;
- types declared in another assembly are rooted only when the generator would already clone them implicitly
  (public parameterless constructor). Emitting member-wise cloners for arbitrary foreign types (e.g. `HttpClient`)
  would depend on state the generator cannot access, and would turn an innocuous usage into a build break.

Roots are deduplicated by fully qualified name and the capability requirement is OR-ed, so no surface wins over
another and the type's default behavior is identical however many surfaces discovered it.

### Identity preservation

`PreserveIdentity` on a discovery surface is a **capability requirement**, not a behavior change:

> Types discovered through this surface must have generated cloning capability compatible with an operation
> requiring identity preservation.

It must not mean "change the type's default `FastDeepClone()` behavior". The implementation therefore feeds a
capability flag (`TypeModel.SupportsStateTracking`) instead of the identity configuration
(`TypeModel.NeedsStateTracking`/`PreserveIdentity`), which stay exactly as the type's own attributes define them.

Mechanically the capability is the existing state-aware machinery: `InternalFastDeepClone(source, state)` is
emitted for every root and already honors a supplied state (guarded `GetKnownRef`/`AddKnownRef`), so no second
implementation is generated. What the capability adds is that a capable root's helpers — implicit POCO helpers,
collection/dictionary/array helpers, `Cloner<T>` branches and derived-type helpers — also accept and thread that
state (`CloneGeneratorContext.StateCapable`, `ImplicitHelperNeedsState`). Reference-typed implicit types always
participate in a capable file so repeated references stay shared. With a `null` state (the ordinary
`FastDeepClone()` path) every one of those additions is a no-op, so the default call keeps its exact previous
shape, speed and allocation profile.

The capability is requested by:

1. `NeedsStateTracking` (the type's own cycles/identity configuration already tracks — the capability is free),
2. any `[FastClonerPreserveIdentity]` on the type (an explicit identity configuration is exactly the case where
   the operation-level override needs to be available and accurate), and
3. a `PreserveIdentity = true` discovery surface.

### Operation-level override

`FastCloneOptions.PreserveIdentity` (shared assembly, next to `FcGeneratedCloneState`) is the explicit
operation-level request: `value.FastDeepClone(FastCloneOptions.PreserveIdentity)`. This is deliberately *not*
`CloneBehavior`, which describes `Reference`/`Shallow`/`Ignore` — a different dimension.

The generated overload delegates to the existing entry point when the option is absent and to
`InternalFastDeepClone(source, new FcGeneratedCloneState(preservingOperation: true))` when it is present, making the
request the strongest requirement for that invocation: topology is preserved whether the type's default preserves or
not.

The overload is emitted **only when the generated graph proves it can carry one tracking state across everything it
deep clones**. Two things can withhold it:

- a part of the graph that necessarily delegates to the runtime cloner (see *Generated/runtime boundary* below), or
- the root not being state capable at all.

So the contract is unconditional for every surface on which the call compiles:

> `value.FastDeepClone(FastCloneOptions.PreserveIdentity)` preserves reference topology across everything that
> operation actually deep clones.

A caller cannot obtain a clone that ignores the requirement: where the guarantee cannot be met the call does not
compile, and — for a requirement or a configured identity — the generator says why instead of leaving it to
guesswork (`FCG013`, `FCG014`).

The state carries `FcGeneratedCloneState.IsPreservingOperation` so the *shape* decision (does a helper take a state
at all?) stays separable from the *call-site* decision (is the state handed over?). `state != null` alone is not
enough: a root can also allocate a state for its own configured default (cycles, a `[FastClonerPreserveIdentity]`
type, a member-level opt-in), and that state must keep resolving member configuration exactly as before. Member and
type level `PreserveIdentity` are therefore defaults that apply to the ordinary call; when the supplied state is a
preserving operation, `CloneGeneratorContext.GetMemberStateArgument` threads the state even for members that opted
out, because the explicit operation is the strongest requirement for that invocation. `Reference`, `Shallow` and
`Ignore` are a different dimension (`CloneBehavior`) and are not affected.

This is what fixes the case that used to make a negative member override win even under an explicit operation:

```csharp
[FastClonerClonable]
public class Root
{
    [FastClonerPreserveIdentity(false)]
    public List<Node> Nodes { get; set; } = [];
}

root.FastDeepClone();                              // member default: no tracking for Nodes
root.FastDeepClone(FastCloneOptions.PreserveIdentity); // explicit: tracked, Nodes[0] == Nodes[1]
```

### Which members can carry the state

Whether a state can be handed to a member is a *shape* question (`CloneGeneratorContext.MemberCanTrack`) and whether
it is handed over by default is a *configuration* question (`MemberTracksByDefault`). Members whose clone call
always accepts a state — `Clonable` members (their own generated file) and the generated `Cloner<T>` helper — take
the state whenever one is in scope, even when the enclosing file is not itself state capable. That threading is what
lets a preserving operation cross a file the generator did not have to make capable, and it is exactly the threading
the committed generator already performed, so no previously shared reference stops being shared.
Collection/dictionary/array/implicit helpers generated *in the same file* can only carry the state when the file is
capable, since their signatures are emitted here.

Three generated-to-generated transitions were repaired so the state is never dropped on the way:

- the `Cloner<T>` branch for a `[FastClonerInclude]` argument that is `IsClonable` and declared in this
  compilation now calls that root's `InternalFastDeepClone(source, state)` instead of its state-free public entry
  point;
- a non-public member (`UnsafeAccessor` path) is cloned by this file's generated helper for collection, dictionary,
  array and implicit member types, instead of always going to the runtime cloner;
- an element/key/value that is the root's own type parameter is cloned through the generated `Cloner<T>` helper
  (which carries the `[FastClonerInclude]` dispatch and its state) instead of straight to the runtime cloner.

A clonable argument dispatched through `Cloner<T>` is also part of the capability closure
(`IdentityCapabilityRequirements`), because the helper hands the state straight to that argument's own file.

### Capability propagation across generated files

A requirement does not stop at a file boundary: `IdentityCapabilityRequirements.Expand` walks each capable root's
generated graph (clonable members, clonable collection elements and dictionary keys/values, `Cloner<T>`-dispatched
clonable arguments, derived types) and ORs the capability into every type the graph clones through another generated
file. Without the closure, a preserving operation would reach a member's own file and find a root that never declared
itself capable.

The closure only adds the *capability* (helpers gain a state parameter they ignore when handed `null`), so files
that do not take part in any identity requirement are emitted byte-identically to before — verified by diffing the
generated output of the whole test project.

Element/key/value types nested below a collection (`List<List<Node>>`) are modelled only for roots that can carry a
state (`TypeModelFactory` gated on `supportsStateTracking`). Without that, the innermost element had no generated
model and its clones were delegated to the runtime cloner, which is the one place a generated graph cannot keep
sharing the supplied state. The modelling is gated so ordinary roots keep their existing generated output.

### Combining several requirements for one root

Capability is a union, never a contest. `Surface A → Form (PreserveIdentity required)` and
`Surface B → Form (no requirement)` produce one `Form` root whose default behavior is whatever `Form`'s own
configuration says; the requirement merely ensures the generated graph can serve a preserving operation. Roots
are deduplicated by fully qualified name and the capability flag is OR-ed.

If the discovered type is already `[FastClonerClonable]`, discovery does not skip the requirement and does not
emit a second root: the requirement is carried as an FQN-keyed requirement and OR-ed into the model produced by
the clonable pipeline before code generation.

### Generated/runtime boundary

The runtime cloner always creates its **own** `FastCloneState`, and the generated `FcGeneratedCloneState` cannot be
shared with it. Every generated path that used to hand a part of the graph to it has been inventoried, repaired
where the target is statically knowable, or reported where it is not.

Repaired (a preserving operation no longer drops the state merely by crossing generated code):

1. `Cloner<T>` for a `[FastClonerInclude]` argument that is `IsClonable` **and declared in this compilation** — the
   helper now calls that root's `InternalFastDeepClone(source, state)`. Arguments from a *referenced* assembly keep
   using the public entry point, because their generated `InternalFastDeepClone` is internal; that case is a
   boundary.
2. Collection/dictionary/array element, key and value types that are the **root's own type parameter** — cloned
   through the generated `Cloner<T>` (which carries the `[FastClonerInclude]` dispatch and the supplied state)
   instead of straight to the runtime cloner.
3. Non-public members on the `UnsafeAccessor` path (`net8+`) — cloned by this file's generated helper for
   collection/dictionary/array/implicit member types. Previously every non-public member except `Safe`/`Clonable`
   went to the runtime cloner.
4. Element/key/value types nested below a collection (`List<List<Node>>`) — now modelled for state-capable roots
   (see above), so the innermost element no longer falls back.

Inherently resolved at runtime (cannot be repaired without a runtime-state bridge, because the target type is not
knowable at generation time):

- `Cloner<T>.Clone` fallthrough, i.e. members of kind `Object`/`Other` (an `object`-typed member, a type the
  generator could not model) and any closed `[FastClonerInclude]` argument that matches no dispatch branch;
- collection/dictionary/array element/key/value with no generated model (an interface or abstract type without
  dispatch, a type without a usable constructor, a `T` in a root with more than one type parameter);
- the unknown-derived-type fallback for abstract and polymorphic roots;
- the whole-root fallback for init-only members combined with circular tracking, and readonly reference fields in a
  struct (the entire type is cloned by the runtime cloner, with a comment saying the state is ignored);
- the TFM-below-`net8` non-public accessor bridge (`BridgeProxyEmitter` proxy `DeepCloneField`/`DeepCloneProperty`).

All of them are detected while the file is generated — the same analysis that emits the code records the reason —
which gives two guarantees:

- **The operation is withheld.** The options overload is only emitted when no boundary was recorded, so a supported
  preserving operation has no known generated-state escape path.
- **The requirement is reported.** A root that must support the operation gets a diagnostic instead of a silent
  best-effort implementation:

  | ID | Severity | Raised when |
  |----|----------|-------------|
  | `FCG013` | Error | a `[FastClonerDiscoverGenericArguments(PreserveIdentity = true)]` requirement cannot be supplied for a type, because its graph necessarily delegates to the runtime cloner |
  | `FCG014` | Warning | a type that configures identity itself (`[FastClonerPreserveIdentity]` on the type or a member) has the same boundary; the runtime fallback for that member is pre-existing behavior, so this is reported rather than broken |

  Roots that never requested identity preservation (including roots that only track state for circular references)
  get neither diagnostic and keep their existing generated output.

### Future option: bridging the runtime state

`FCG013`/`FCG014` make the contract honest without a runtime change, but they cannot make an `object`-typed or
custom-handler subgraph participate. Closing that would need, roughly:

- `FcGeneratedCloneState` able to expose (or be built around) the runtime `FastCloneState`'s known-reference map;
- an internal runtime entry point that accepts that map (`FastCloner.DeepClone(source, state)` in one form or
  another) so the cloner can register into and read from the same identity map;
- the generated `Cloner<T>` fallthrough and the non-public accessor bridge passing the state through it.

That is a runtime + shared-assembly change and is deliberately **not** part of this issue. It should be driven by a
concrete consumer graph (for example a real `Form` graph from Supervisor) that actually crosses one of the
boundaries above.

### Confirmed pre-existing limitations (reported, not fixed here)

- **Member-level negative override (against a preserving child default).** `[FastClonerPreserveIdentity(false)]` on
  a member is expressed by passing `null` as the child's state (`InternalFastDeepClone(child, null)`) when the
  enclosing state is not a preserving operation, i.e. "use the child type's default". When the child type's own
  default is preserving, the child allocates its own state and tracks anyway, so the override cannot suppress
  tracking inside that member's subgraph for an *ordinary* call. Both the working case (non-preserving child
  default) and the limitation are covered by characterization tests in `IdentityPreservationTests`. This is
  *unchanged* by the capability and by the operation-level override: an explicit preserving operation intentionally
  wins over the override, which is the intended new semantics rather than this limitation.

### Marked method vs marked containing type

For a method usage, only the *method's* closed type arguments are collected when the method itself carries the
attribute; the containing type's closed arguments are collected when the containing type is a discovery point
(itself marked, or declaring a marked method). An unmarked generic method on a marked containing type therefore
does not contribute its type arguments.


### Known boundaries

- Discovery observes closed generic *syntax*. Generic method type arguments that are left to inference
  (`Operations.Execute()` with nothing written out) are not observed; the type-argument form is.
- Types that are already `[FastClonerClonable]` keep their own generated entry point. A `PreserveIdentity`
  requirement from a discovery surface adds the capability to that root; it cannot change, and does not attempt
  to change, the default behavior the type configured for itself.
- `FastDeepClone(FastCloneOptions)` exists only where the capability was requested. Ordinary roots keep the
  overload out of their generated surface so the default path cannot be mistaken for a tracked one.

