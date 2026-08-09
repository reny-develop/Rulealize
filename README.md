# Rulealize

Plugin-oriented state transition and rule execution runtime driven by declarative JSON DSL.

A rule set is a JSON document. The runtime turns it into nodes, applies inputs to states,
and lists what is legal from here. What a rule set is allowed to say is decided entirely by
which plugins are loaded — the core provides no operations at all, not even booleans.

```csharp
RuleRuntime runtime = new RuleRuntime().LoadPluginsFrom("plugin");

RuleContext reversi = runtime.CreateContext(File.ReadAllText("reversi.json"));

ValidInputSet moves = reversi.GetValidInputs(reversi.InitialState, validationLimit: 128);
// place(at: d3), place(at: c4), place(at: f5), place(at: e6) — all black's

TransitionResult next = reversi.ApplyToState(
    moves[0].ToInputDocument(reversi.RuleSet),
    reversi.InitialState);
```

## What the core knows

Eight reserved keys, and one more for telling a node from anything else:

```
$schema  id  version  requires  state  definitions  inputs  terminal        op
```

Everything else in the document is vocabulary. A node is an object carrying an `op`; the
value of `op` selects a factory from a table the plugins filled in, and the rest of the
object is that plugin's business. The core never sees a plugin type and never learns what
an operation does — not even that `$board` is shorthand for reading a state field, which is
a string expansion a plugin registered against a character it reserved.

That is why `requires` is worth reading. It lists the vocabularies a rule set draws on, and
it can only say something because the standard set is cut finely: a rule set that needs
`Rulealize.Plugin.Arithmetic` is one that counts something.

## The three kinds of node, and when things fail

| Kind | Produces | Appears in |
| --- | --- | --- |
| expression | a value | guards, effect arguments, definition bodies, parameter domains, `terminal` |
| effect | a write to the state | elements of an input's `effects` |
| schema | the type of a state field | `state.schema` |

Placement is enforced while the rule set is compiled. So is everything else the document
can settle on its own: unknown operations, missing keys, an expression where a literal
belongs, a local nothing declared, an undefined definition, an argument list that does not
match a definition's parameters, a cycle between definitions, a state path that is not in
the schema.

The reason for pushing so much into `CreateContext` is `GetValidInputs`. It evaluates a
guard against every candidate in a parameter's domain, and a fault that first appears on
the forty-first candidate is a fault that reaches production.

What is left to fail at run time is short: a value of the wrong kind, an ordering
comparison against null, division by zero, a `branch.match` with no matching case. Reading
past the end of a sequence and reading a square off the board are not on that list — they
produce null, and rule sets are built on their doing so.

## Snapshot semantics

Every expression an input's effects evaluate reads the state as it was when the input
arrived. Writes accumulate in a draft and are committed together.

This is what lets Reversi's placement be written in the order a person would describe it —
put the stone down, then flip what it captured — instead of hoisting the capture set into a
binding to keep the second effect from rescanning a board that already has the new stone on
it.

An effect that does need to build on what an earlier effect wrote reads the field back from
the draft. Both halves are in play at once when two effects edit one board: the second
starts from a board that already has the new stone, while the expressions inside it still
compute captures from the position as it stood before the move.

## Definitions

Held by the core as a name, a parameter list and a body; never evaluated by it. A plugin
supplies the vocabulary for referring to one and calling one, so a rule set that defines
nothing need not load it.

Bodies are hygienic — a body sees the state, the other definitions, and its own parameters,
and nothing from wherever it was called. Arguments are the only way in.

Bodies are also pure, so the runtime caches a result against the definition, its arguments,
and the snapshot, for as long as that snapshot lasts. Over a whole `GetValidInputs` sweep
that matters: Reversi's capture computation is reached from a guard and again from the
effect that follows it, with the same coordinate, for each of sixty-four candidates, and
each evaluation walks eight rays.

Recursion is refused. Termination could not be guaranteed otherwise, and with the call
graph fixed the cost of an evaluation has an upper bound that can be estimated.

## `GetValidInputs` and the limit

Candidates are the product of an input's parameter domains, sifted by its guard. Reversi
produces sixty-five — sixty-four squares and a pass — which is nothing. A shogi move
written as `from`, `to` and a promotion flag produces thirteen thousand, which is not.

`validationLimit` bounds how many guards are evaluated. `Truncated` says whether it stopped
the search early; a truncated result is a subset of what is legal, never a wrong entry. The
real fix for a large domain is to narrow it before the guard runs, which is a job for the
plugin that owns the domain.

The method is synchronous on purpose. It performs thousands of node evaluations per call,
and an asynchronous signature over that path would cost more than it could buy.

## Where asynchrony belongs

At the boundary, and nowhere else. Evaluation is pure computation over documents that are
already in memory, so the methods that take a `string` are synchronous — `CreateContext`,
`ApplyToState`, `GetValidInputs`, `GetTerminalStatus`. An `Async` suffix over a body that
can only ever return an already-completed task tells the caller something untrue about
where it may yield.

Reading a document off a stream genuinely is I/O, and that is what the asynchronous
overloads are for: `CreateContextAsync(Stream)` and `ApplyToStateAsync(Stream, Stream)`.
They await the read and then run the same synchronous evaluation.

## Arguments have to survive the round trip

`GetValidInputs` hands back moves; feeding one straight back to `ApplyToState` must
produce the move it described. So an argument is written in its own JSON form — a number
stays a number, a boolean stays a boolean.

Only a value with no JSON form of its own is written as text: a coordinate, a direction,
anything opaque. That is what the value model's canonical text is for, and why a plugin
whose values can be input arguments has to accept both its own type and that text on the
way back in.

## Documents

```jsonc
// rulealize/state/v1
{ "$schema": "rulealize/state/v1", "ruleSet": "reversi@1.0.0",
  "data": { "board": { "d4": "white", … }, "turn": "black", "passes": 0 } }

// rulealize/input/v1
{ "$schema": "rulealize/input/v1", "ruleSet": "reversi@1.0.0",
  "input": "place", "args": { "at": "d3" } }
```

The frame is all the core fixes. How each field inside `data` becomes JSON is decided by
the schema node that declared it — a board is a sparse coordinate map because a grid plugin
says so, and changing it to a dense array would touch one file in that plugin and nothing
else.

State documents come from outside, so they are checked against the schema on the way in,
and every violation is reported rather than the first:

```
The state does not satisfy state.schema.
  board.z9: is not a square of a 8×8 (algebraic) board.
  turn: Expected one of black, white but got "green".
  passes: Expected at most 2 but got 7.
```

Comments and trailing commas are accepted in every document this runtime reads. A rule set
of any size needs somewhere to say why a rule is the way it is.

## API

| Member | |
| --- | --- |
| `RuleRuntime.AddPlugin` / `LoadPlugins` / `LoadPluginsFrom` | build the vocabulary |
| `RuleRuntime.CreateContext` / `CreateContextAsync` | compile a rule set |
| `RuleContext.InitialState` | the opening position, as a state document |
| `RuleContext.ApplyToState` / `ApplyToStateAsync` | apply an input to a state |
| `RuleContext.GetValidInputs` | what is legal from here |
| `RuleContext.GetTerminalStatus` | whether a state is final, and its outcome |

Exceptions: `RuleSetBuildException` for a document that is not a valid rule set,
`RuleDocumentException` for a state or input document this rule set cannot accept,
`IllegalInputException` for a move the rules do not allow, `RuleEvaluationException` for
values that make an operation meaningless, and `PluginLoadException` for a set of plugins
that cannot be used together.

A context is immutable and holds no position, so one serves any number of concurrent games.

## Loading plugins

A plugin is a public, concrete `IRulealizePlugin` with a parameterless constructor. Nothing
else marks one — no attribute, no naming convention, no manifest beside the DLL — because
the interface is already the contract.

`LoadPluginsFrom` takes a DLL or a folder, and skips assemblies with no plugin in them, so
pointing it at an application's own output folder is harmless. Two plugins claiming one
namespace, or one shorthand character, are refused when they are loaded rather than when a
rule set first touches the contested name.

### Vocabulary an application keeps to itself

`AddPlugin` takes an instance, so a vocabulary does not have to be an assembly on disk to
be one. A project using this library for its own rules will have operations worth writing
and not worth publishing, and it reaches them by implementing `IRulealizePlugin` in its own
code:

```csharp
RuleRuntime runtime = new RuleRuntime()
    .LoadPluginsFrom("plugin")
    .AddPlugin(new DeployVocabulary(freezeCalendar, ownershipMap));
```

Same interface, same manifest, same namespace claim, same `requires` line in the rule set.
What changes is the constructor: a plugin found by scanning is built through a parameterless
one and has nowhere to receive anything, while this one can be handed a snapshot of data the
rule set has no business carrying.

Two conventions and one rule make it safe.

- **Vendor-qualify the identifier and the namespace.** `Acme.Deploy.Rules` and `acme`, not
  `Rules` and `deploy`. A private vocabulary that squats on a plain name will collide with a
  published plugin eventually, and by then rule sets are in production.
- **Claim no shorthand character.** There is one per plugin and only a handful that can ever
  be used. A vocabulary with an audience of one should leave them.
- **Operations must be pure.** `GetValidInputs` evaluates a guard once per candidate in a
  parameter's domain, so an operation that reads a clock or a database turns a domain into a
  query storm and answers one question two ways inside a single call. External data belongs
  in an immutable snapshot taken before the runtime is built, or in the state document. The
  current date is a state field; it is not something an operation goes and finds out.

`requires` keeps working throughout, and that is the point of doing it this way rather than
inventing a lighter registration path. A rule set naming `Acme.Deploy.Rules` is refused by a
runtime without it, with the name in the message — the same failure as for a plugin that was
not on the feed. [`sample/Deploy/`](sample/Deploy/) is the worked example.

## Building

`Rulealize.Abstraction` is not on nuget.org yet, so `NuGet.config` points at a folder feed.
Produce it from the abstraction repository first:

```
dotnet pack path\to\Rulealize.Abstraction\src\Rulealize.Abstraction -c Release -o path\to\LocalNuGet
```

with `LocalNuGet` a sibling of this repository. Then `dotnet build`.

The twelve standard plugins live in their own repositories, one per vocabulary:
Binding, Branch, Definition, Logic, Comparison, Arithmetic, TypeSchema, Sequence, State,
Grid, Tuple, Record.

## Repository layout

| | |
| --- | --- |
| [`src/`](src/) | the runtime |
| [`test/`](test/) | xUnit tests — `dotnet test` |
| [`sample/`](sample/) | one directory per sample application — see [`sample/README.md`](sample/README.md) |
| [`ruleset/`](ruleset/) | the rule set documents, one copy of each |
| [`doc/`](doc/) | how the design was arrived at |

A rule set lives in one place and is consumed from two: the test suite compiles every
document in `ruleset/`, and a sample links the one it demonstrates. They used to be copies
kept in step by hand, which is why [`RuleSets.props`](RuleSets.props) now exists — "the
sample runs the document the tests pin down" is worth more as a build fact than as a rule
somebody remembers.

Both the tests and the samples need the twelve plugins, so both import
[`StandardPlugins.props`](StandardPlugins.props). It builds each plugin from its own
repository beside this one and drops the DLL into a `plugin` folder next to the
executable. The references are not compile-time references — neither project can name a
plugin type — so what gets exercised is the same folder scan a deployed application does.

Point `PluginRepositoryRoot` somewhere else if the plugin repositories are not siblings:

```
dotnet test -p:PluginRepositoryRoot=D:\somewhere\
```

## Design notes

[`doc/`](doc/) works the design out on Reversi: the [DSL](doc/dsl-example-reversi.md), the
[value model](doc/value-model.md), and a [specification per plugin](doc/plugin/README.md).
Reversi is a good test of the boundary because the rule set that describes it contains no
Reversi-specific vocabulary at all.

What that design then had to survive is written up one subject at a time:
[chess](doc/dsl-example-chess.md), where a move's destination depends on its origin;
[shogi](doc/dsl-example-shogi.md), where captured pieces have to be held somewhere;
[a shift roster](doc/dsl-example-roster.md), which is not a game and never mentions a board;
and [a deployment pipeline](doc/dsl-example-deploy.md), which is the first one whose
vocabulary is not entirely made of plugins. Each has a rule set in
[`ruleset/`](ruleset/) and a sample that plays with it.

## License

Apache-2.0.
