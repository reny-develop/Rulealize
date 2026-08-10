# The standard vocabulary

Twelve plugins, each in its own repository, each with its own specification. The first ten
are what writing [Reversi](dsl-example-reversi.md) called for; the eleventh, Tuple, and
version 1.1 of Sequence and Grid are what [chess](dsl-example-chess.md) turned out to need;
the twelfth, Record, along with `type.list` and Sequence 1.2, are what
[shogi](dsl-example-shogi.md) showed was missing (→ [collections](collections.md)).

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

## Why the specifications are not in this repository

Each links out, and that is deliberate. A specification says what one version of one plugin
provides — Sequence is at 1.2 and Grid at 1.1 while most of the others are still at 1.0 —
so it has to be able to change when that plugin releases and not before. Kept here, it
would ship on this repository's schedule, and there would be no commit to point at to say
what Sequence 1.2 meant.

It also follows the dependency the code already has. A plugin references
`Rulealize.Abstraction` and nothing else of this project's; a specification of that plugin
living here would be the one thing pointing the other way.

What stays is this page — the index of what the standard distribution contains, which
cannot live in twelve places — and the design record, which is cross-cutting by nature.
Which example forced which operation into existence is a fact about the DSL, not about any
one plugin, and it is written up in [the design record](README.md#the-design-record).

Both of the things every specification assumes are in `Rulealize.Abstraction`, because both
describe types that package defines:
[the value model](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/value-model.md)
and [the notation the specifications are written in](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/specification-notation.md).

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

The worked example is [the deployment pipeline](dsl-example-deploy.md) and
[sample/Deploy/](../sample/Deploy/).

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
