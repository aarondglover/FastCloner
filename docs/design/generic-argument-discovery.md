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

The exact public shape is subject to upstream maintainer preference.

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

## Status

Design proposal only. Implementation should wait for upstream feedback on whether this capability and public API shape fit FastCloner's direction.
