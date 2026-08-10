# The plugin specifications

What each of the standard vocabularies provides, in detail. The first ten are what writing
[Reversi](../dsl-example-reversi.md) called for; the eleventh, Tuple, and version 1.1 of
Sequence and Grid are what [chess](../dsl-example-chess.md) turned out to need; the
twelfth, Record, along with `type.list` and Sequence 1.2, are what
[shogi](../dsl-example-shogi.md) showed was missing (→ [collections](../collections.md)).

Every plugin assumes [the value model and the three kinds of node](../value-model.md). Read
that first.

| Plugin | Namespace | Provides |
| --- | --- | --- |
| [Binding](Binding.md) | `bind` | scoped bindings (`let`) and local references |
| [Branch](Branch.md) | `branch` | branching, on a condition (`if`) and on a value (`match`) |
| [Definition](Definition.md) | `def` | referring to and applying what `definitions` declares |
| [Logic](Logic.md) | `logic` | boolean operations |
| [Comparison](Comparison.md) | `cmp` | equality, ordering, and asking about null |
| [Arithmetic](Arithmetic.md) | `math` | arithmetic |
| [TypeSchema](TypeSchema.md) | `type` | the vocabulary `state.schema` is written in |
| [Sequence](Sequence.md) | `seq` | building, transforming and folding sequences |
| [State](State.md) | `state` | reading and writing the state |
| [Grid](Grid.md) | `grid` | two-dimensional boards, coordinates, directions |
| [Tuple](Tuple.md) | `tuple` | a compound value that has a canonical text form |
| [Record](Record.md) | `rec` | records in the state, read and written by a computed key |

## Common to all of them

### How to read a specification

The notation used in each node's "form".

- `<expression>` — any expression node, or a JSON literal evaluated as one
- `<expression:T>` — the value has to be of kind `T`
- a key marked `?` — optional
- a key marked **static** — a literal rather than an expression, read at `CreateContext`

### When things are checked

- **At `CreateContext` (static)** — an unknown `op`, a missing required key, a node in a
  position its kind does not allow, an expression where a static key belongs, a `def.call`
  whose arguments do not match the definition's parameters
- **At evaluation (dynamic)** — a value of the wrong kind, ordering or arithmetic against
  null, division by zero

Nothing that can be settled statically is left to run time.

### The plugin manifest

Every plugin declares an identifier, a version, the namespace it provides, and the prefix
it reserves. Colliding namespaces and colliding prefixes are detected when plugins load.

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

The worked example is [the deployment pipeline](../dsl-example-deploy.md) and
[sample/Deploy/](../../sample/Deploy/).

### Why there is no lighter registration API

An API for registering one expression at a time, with no manifest, would be more
convenient. The reason not to build it is `requires`.

`requires` is worth reading only because **every vocabulary has a manifest**. Let
vocabularies arrive by two different routes and you get vocabularies that cannot be named
in `requires`, vocabularies that miss the `OperationTable` collision check, and
vocabularies that never appear in `RuleRuntime.Plugins` — and a rule set stops being a
document you can read to find out what it needs.

So the difference between the routes stays down to `new` versus a folder scan. A rule set
requiring `Acme.Deploy.Rules` is then refused by a runtime without it **exactly as it would
be for a plugin that was missing from the feed**.

### What only an in-process vocabulary can do

A plugin discovered by scanning is required to be public with a parameterless constructor
(`PluginProbe`), which makes it structurally stateless.

Passing an instance lifts that restriction, so a vocabulary **can be handed an immutable
snapshot loaded at start-up**. A holiday calendar, a price list, an org chart, an ownership
map — data someone else owns, updated on its own schedule, with no business being carried
in each individual state document.

### The conventions

| | |
| --- | --- |
| identifier and namespace | **Vendor-qualify them.** `Acme.Deploy.Rules` and `acme`, not `Rules` and `deploy`. A private vocabulary squatting on a plain name will collide with a published plugin eventually, and by then rule sets are in production |
| reserved prefix | **Claim none.** One character per plugin and very few that can ever be used; a vocabulary with an audience of one should not spend one |
| version | what the vocabulary's compatibility is expressed in. Removing an op or changing what one means means a new major (which is how `requires` reads `^`) |

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
