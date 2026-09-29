# Generic argument discovery points

## Goal

Allow FastCloner source generation to treat closed generic arguments observed through explicitly marked generic API surfaces as clone roots.

This extends root discovery only. It does not introduce a new cloning mechanism or change FastCloner's existing recursive graph analysis and code generation.

## Proposed API

```csharp
[FastClonerDiscoverGenericArguments]
public interface IContainer<T>
{
}
```

A closed usage such as:

```csharp
IContainer<Order>
```

would cause `Order` to be treated as a source-generation root.

The same concept should apply to other generic declarations and API surfaces:

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

## Discovery semantics

When a marked API surface is observed in a closed generic form, all relevant closed generic arguments are treated as clone roots.

Examples:

```csharp
Pipeline<Order, Result>
Processor<Customer, CustomerSnapshot>
IStage<Form>
Operations.Execute<Request, Response, Context>()
```

For a marked member on a generic containing type, discovery should consider both:

- generic arguments belonging to the member itself, where applicable;
- generic arguments belonging to the closed containing type.

Open or unbound generic arguments should be ignored until a closed usage is available.

Once a concrete root is discovered, it should flow through FastCloner's existing recursive type analysis and code-generation pipeline.

## Identity preservation

A discovery point may optionally require identity-preserving cloning:

```csharp
[FastClonerDiscoverGenericArguments(PreserveIdentity = true)]
```

This should reuse FastCloner's existing identity-tracking semantics.

Given:

```text
source:

A ──► B ◄── C
```

the clone should preserve the same internal reference topology while remaining detached from the source graph:

```text
clone:

A' ──► B' ◄── C'
```

That means repeated references to the same source object resolve to the same cloned object, while distinct source objects remain distinct.

## Non-goals

This proposal does not:

- introduce a new clone engine;
- change existing `FastClonerClonable`, `FastClonerInclude`, or `FastClonerRegister` semantics;
- require domain types to opt into this discovery mechanism;
- select only one generic argument by position;
- change existing runtime reflection behavior.

## Implementation direction

The source generator should discover marked generic declarations or members from consumer syntax/semantic usage, resolve their constructed generic context, collect the closed generic arguments, and feed those types into the same root-analysis path used by existing generic discovery.

The implementation should preserve FastCloner's incremental-generator characteristics and avoid introducing broad compilation invalidation.
