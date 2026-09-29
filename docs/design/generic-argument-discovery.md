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
- referenced-assembly declarations acting as discovery points from a consuming compilation.

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
- a type that already carries `[FastClonerClonable]` is skipped: its own pipeline already emitted the entry
  point, and a second one would be a duplicate member of the same extension class;
- types declared in another assembly are rooted only when the generator would already clone them implicitly
  (public parameterless constructor). Emitting member-wise cloners for arbitrary foreign types (e.g. `HttpClient`)
  would depend on state the generator cannot access, and would turn an innocuous usage into a build break.

Roots are deduplicated by fully qualified name; when the same type is reached through several surfaces, the
identity-preserving model wins because identity is a property of the discovered type rather than of whichever
surface discovered it first.

### Identity preservation

`PreserveIdentity = true` feeds `true` through `TypeModelFactory`/`StateRequirementAnalyzer`, i.e. exactly the
`[FastClonerPreserveIdentity]` path. One gap surfaced: an implicitly cloned member type whose own members are all
safe types kept `NeedsStateTracking = false`, so it was cloned once per reference and shared identity was lost —
while the runtime cloner preserved it. Since identity preservation is useless without that, the implicit models
of a preserving root now get a state slot (`TypeModelFactory.PromoteIdentityTracking`). The change is inert
unless the root asks for identity preservation, and it closes a generated/runtime behavioral difference.

### Known boundaries

- Discovery observes closed generic *syntax*. Generic method type arguments that are left to inference
  (`Operations.Execute()` with nothing written out) are not observed; the type-argument form is.
- Types that are already `[FastClonerClonable]` keep their own generated entry point; `PreserveIdentity` declared
  on a discovery surface does not override a root that already exists through its own attribute.

