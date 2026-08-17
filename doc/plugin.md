# The standard vocabulary

Twelve plugins, each in its own repository, each with its own specification. A specification
says what one version of one plugin provides — Sequence is at 1.2 and Grid at 1.1 while most
of the others are still at 1.0 — so each entry below links out to the repository that
releases it.

| Plugin | Namespace | Provides |
| --- | --- | --- |
| [Binding](https://github.com/reny-develop/Rulealize.Plugin.Binding/blob/main/doc/specification.md) | `bind` | scoped bindings (`let`) and local references |
| [Branch](https://github.com/reny-develop/Rulealize.Plugin.Branch/blob/main/doc/specification.md) | `branch` | branching, on a condition (`if`) and on a value (`match`) |
| [Definition](https://github.com/reny-develop/Rulealize.Plugin.Definition/blob/main/doc/specification.md) | `def` | referring to and applying what `definitions` declares |
| [Logic](https://github.com/reny-develop/Rulealize.Plugin.Logic/blob/main/doc/specification.md) | `logic` | boolean operations |
| [Comparison](https://github.com/reny-develop/Rulealize.Plugin.Comparison/blob/main/doc/specification.md) | `cmp` | equality, ordering, and asking about null |
| [Arithmetic](https://github.com/reny-develop/Rulealize.Plugin.Arithmetic/blob/main/doc/specification.md) | `math` | arithmetic |
| [TypeSchema](https://github.com/reny-develop/Rulealize.Plugin.TypeSchema/blob/main/doc/specification.md) | `type` | the vocabulary `state.schema` is written in |
| [Sequence](https://github.com/reny-develop/Rulealize.Plugin.Sequence/blob/main/doc/specification.md) | `seq` | building, transforming and folding sequences |
| [State](https://github.com/reny-develop/Rulealize.Plugin.State/blob/main/doc/specification.md) | `state` | reading and writing the state |
| [Grid](https://github.com/reny-develop/Rulealize.Plugin.Grid/blob/main/doc/specification.md) | `grid` | two-dimensional boards, coordinates, directions |
| [Tuple](https://github.com/reny-develop/Rulealize.Plugin.Tuple/blob/main/doc/specification.md) | `tuple` | a compound value that has a canonical text form |
| [Record](https://github.com/reny-develop/Rulealize.Plugin.Record/blob/main/doc/specification.md) | `rec` | records in the state, read and written by a computed key |

Every specification assumes two documents, and both are in `Rulealize.Abstraction` because
both describe types that package defines:
[the value model](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/value-model.md)
and [the notation the specifications are written in](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/specification-notation.md).

## Writing one

```sh
dotnet new install Rulealize.Templates
dotnet new rulealize-plugin -n Rulealize.Plugin.Text
```

What comes out builds and runs before anything has been written to it, and
[**writing a vocabulary**](https://github.com/reny-develop/Rulealize.Templates/blob/main/doc/writing-a-vocabulary.md)
is the guide to the part a template cannot write. The loop is
[`Rulealize.Cli`](https://github.com/reny-develop/Rulealize.Cli): `rulealize plugins` says
what loaded and what it registered, and `rulealize play` runs a rule set against it.

## A vocabulary that is not distributed

The twelve above are found as DLLs in a folder. But `RuleRuntime.AddPlugin` takes an
**instance**, so a vocabulary does not have to be an assembly on disk to be one.

```csharp
RuleRuntime runtime = new RuleRuntime()
    .LoadPluginsFrom("plugin")
    .AddPlugin(new DeployVocabulary(freezeCalendar, ownershipMap));
```

A project using this library for its own business rules will always turn up operations
worth writing and not worth publishing. Rather than dressing one up as a plugin and
shipping it, implement `IRulealizePlugin` in your own assembly and hand it over this way.
The contract is identical to a published plugin's — it has a manifest, it claims a
namespace, and its name appears in the rule set's `requires`.

The worked example is [sample/Deploy/](../sample/Deploy/).

A rule set requiring `Acme.Deploy.Rules` is refused by a runtime without it **exactly as it
would be for a plugin that was missing from the feed**. The difference between the two
routes stays down to `new` versus a folder scan.

### What only an in-process vocabulary can do

A plugin discovered by scanning is required to be public with a parameterless constructor,
which makes it structurally stateless.

Passing an instance lifts that restriction, so a vocabulary **can be handed an immutable
snapshot loaded at start-up**. A holiday calendar, a price list, an org chart, an ownership
map — data someone else owns, updated on its own schedule, with no business being carried
in each individual state document.

### Vendor-qualify the identifier and the namespace

`Acme.Deploy.Rules` and `acme`, not `Rules` and `deploy`. A vocabulary that squats on a
plain name will collide with a published plugin eventually, and by then rule sets are in
production.

### Purity — this is not a matter of style

**Every operation registered has to be a pure function of its arguments and that immutable
snapshot.**

`GetValidInputs` evaluates a guard once per candidate in a parameter's domain. An operation
that reaches outside means

- one query per candidate, so combinatorial blow-up becomes I/O blow-up
- the same question answered two ways inside one call
- snapshot semantics broken, since "expressions read the state as the input found it" is a
  guarantee that rests on the state being all they read

Values that change belong in the state document. **The current date is a field handed to an
operation, not something an operation goes and finds out** — which is why
`sample/Deploy`'s `acme.frozen` takes `date` as an argument.
