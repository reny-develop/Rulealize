# Rulealize

**Rules as a JSON document, not as code.** Rulealize compiles a declarative rule set into a
runtime that applies an input to a state, lists every input that is legal from here, and
says whether a state is final.

It is not a game engine. Board games are in here because they are unforgiving test cases —
[Reversi](sample/Reversi/), [chess](sample/Chess/), [shogi](sample/Shogi/) — and so, for
the opposite reason, are [a shift roster](sample/Roster/) and
[a deployment pipeline](sample/Deploy/). The roster rule set has no turn, no opponent, no
board, and not one `grid.` operation in it.

What a rule set is allowed to say is decided entirely by which plugins are loaded. The core
provides no operations at all, not even booleans.

```console
$ dotnet run --project sample/Reversi -- --auto
Loaded 12 plugins:
  bind    Rulealize.Plugin.Binding 1.0.0  shorthand '@'
  branch  Rulealize.Plugin.Branch 1.0.0
  cmp     Rulealize.Plugin.Comparison 1.0.0
  grid    Rulealize.Plugin.Grid 1.1.0
  state   Rulealize.Plugin.State 1.0.0  shorthand '$'
  …

Rule set: reversi@1.0.0   inputs: place, pass

    a b c d e f g h
 8  - - - - - - - -  8
 7  - - - - - - - -  7
 6  - - - - . - - -  6
 5  - - - @ O . - -  5
 4  - - . O @ - - -  4
 3  - - - . - - - -  3
 2  - - - - - - - -  2
 1  - - - - - - - -  1
    a b c d e f g h

 @ black 2    O white 2    turn: black    passes: 0
 legal: place(at: e6), place(at: f5), place(at: c4), place(at: d3)
```

Nothing in that sample knows the rules of Reversi. It loads a folder of plugins, compiles a
document, asks what is legal and applies what was chosen.

> Requires `net10.0`. `Rulealize`, `Rulealize.Cli` and each of the standard plugins
> are on nuget.org — a plugin is an ordinary package, because the runtime finds its assembly
> by scanning a folder and nothing else about it is special.

## Why you might want this

**It tells you what is legal.** `GetValidInputs` takes the product of an input's parameter
domains and sifts it with that input's guard. That is the move list for a game AI, the set
of enabled buttons on a screen, and the branching factor of a scheduling search — and none
of it is code anybody wrote twice.

**A rule set is data.** It ships, versions and diffs on its own, and the same host binary
runs a different set of rules. The Deploy sample switches between an ordinary policy and a
lockdown policy without recompiling, and the Roster sample runs a completely different week
— other people, three days instead of five — through the same document, because the people
were never in the document.

**A wrong rule set is refused before it runs.** Everything decidable from the document is
decided in `CreateContext`, with a JSON pointer to the offending node. A guard that is only
reached by the forty-first candidate is not a place to discover a typo.

**The core knows nothing about your domain.** No plugin type crosses into it, no operation
is built in. What your rules can say is exactly what you loaded, and a rule set's `requires`
list says which vocabularies that was.

## Try it

```sh
dotnet add package Rulealize              # the library
dotnet tool install -g Rulealize.Cli      # and the command that assembles a plugin folder

rulealize restore reversi.json
```

```console
  Rulealize.Plugin.Binding 1.0.0
  Rulealize.Plugin.Grid 1.1.0
  …
10 plugins -> plugin
'reversi.json' compiles against it.
```

**The document is the dependency list.** `requires` already names every vocabulary a rule set
draws on and which versions of each will do — it has to, because that is what the runtime
reads to refuse a document it cannot run — so there is nothing to write out a second time.
[`restore`](https://github.com/reny-develop/Rulealize.Cli) reads it, fetches what it names
into a `plugin` folder, and then compiles the document against what it just wrote. A folder
that comes back is one the document runs on, and it is the folder [Run it](#run-it) loads.

A plugin can also arrive as an ordinary package reference: `dotnet add package
Rulealize.Plugin.Grid` puts the assembly in the application's own output folder, and
`LoadPluginsFrom(AppContext.BaseDirectory)` passes over everything that is not a plugin. That
is the simpler arrangement when the rules ship with the binary rather than travelling on
their own schedule. [The standard vocabulary](doc/plugin.md) lists the twelve and what each
provides.

The samples are described in [`sample/README.md`](sample/README.md). Read Reversi
first — it is the shortest complete host there is.

## Write a rule set

Not a board. An approval that has to be submitted before it can be decided, and can only be
rejected for a reason from a fixed list.

```jsonc
{
  "$schema": "rulealize/ruleset/v1",
  "id": "approval",
  "version": "1.0.0",

  // The vocabularies this document draws on. Nothing else is in scope.
  "requires": [
    { "plugin": "Rulealize.Plugin.TypeSchema", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.State",      "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Comparison", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Logic",      "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Sequence",   "version": "^1.0" }
  ],

  // What a state is, and where one starts. `$stage` below is shorthand for reading
  // this field — a string expansion the State plugin registered against `$`.
  "state": {
    "schema": {
      "stage":  { "op": "type.enum", "values": ["draft", "review", "approved", "rejected"] },
      "reason": { "op": "type.enum", "values": ["scope", "cost", "timing"], "nullable": true }
    },
    "initial": { "stage": "draft", "reason": null }
  },

  "inputs": {
    "submit": {
      "when": { "op": "cmp.eq", "left": "$stage", "right": "draft" },
      "effects": [ { "op": "state.set", "path": "stage", "value": "review" } ]
    },

    "approve": {
      "when": { "op": "cmp.eq", "left": "$stage", "right": "review" },
      "effects": [ { "op": "state.set", "path": "stage", "value": "approved" } ]
    },

    // A parameter is a domain and a guard. The domain says what the argument may be,
    // and `GetValidInputs` walks it — so this one input becomes three legal moves.
    "reject": {
      "params": { "reason": { "domain": { "op": "seq.of", "of": ["scope", "cost", "timing"] } } },
      "when": { "op": "cmp.eq", "left": "$stage", "right": "review" },
      "effects": [
        { "op": "state.set", "path": "stage",  "value": "rejected" },
        { "op": "state.set", "path": "reason", "value": "@reason" }
      ]
    }
  },

  "terminal": {
    "when": {
      "op": "logic.or",
      "any": [
        { "op": "cmp.eq", "left": "$stage", "right": "approved" },
        { "op": "cmp.eq", "left": "$stage", "right": "rejected" }
      ]
    },
    "result": "$stage"
  }
}
```

Comments and trailing commas are accepted in every document this runtime reads. A rule set
of any size needs somewhere to say why a rule is the way it is.

## Run it

```csharp
RuleRuntime runtime = new RuleRuntime().LoadPluginsFrom("plugin");
RuleContext approval = runtime.CreateContext(File.ReadAllText("approval.json"));

// A context holds no position. The state travels in and out as a document, so a case can
// be suspended, stored and resumed by keeping nothing but this string.
string state = approval.InitialState;

while (!approval.GetTerminalStatus(state).IsTerminal)
{
    ValidInputSet moves = approval.GetValidInputs(state, validationLimit: 64);
    if (moves.Count == 0)
    {
        break;
    }

    // A move that came out of GetValidInputs goes straight back in — that round trip is
    // why an argument is written in its own JSON form. Pick properly; moves[0] is a stub.
    ValidInput chosen = moves[0];
    TransitionResult result = approval.ApplyToState(
        chosen.ToInputDocument(approval.RuleSet),
        state);

    state = result.State;
}
```

What `GetValidInputs` answers, stage by stage:

```
draft      submit
review     approve, reject(reason: scope), reject(reason: cost), reject(reason: timing)
rejected   —   terminal, result: rejected
```

Five candidates are evaluated every time — the three domains do not depend on the state, only
the guards do — and one input with a domain of three reasons is three legal moves. That is
what makes this the button list for a screen and the branch set for a search.

The document is [`ruleset/approval.json`](ruleset/approval.json), and
[`test/ApprovalTests.cs`](test/ApprovalTests.cs) holds it to everything this section claims.

## What is checked, and when

Everything the document can settle on its own is settled in `CreateContext`, and the
message carries a JSON pointer to the node:

```
/inputs/submit/when/left: 'stagee' is not a field of the state schema.
/inputs/reject/effects[0]/path: 'staeg' is not a field of the state schema.
/inputs/submit/effects[0]: 'cmp.eq' is an expression and cannot appear where an effect is expected.
```

Unknown operations, missing keys, unbound locals, undefined or cyclic definitions, an
argument list that does not match a definition's parameters, and a node used where its kind
does not belong are all refused there too.

State documents come from outside, so they are checked against the schema on the way in,
and every violation is reported rather than the first:

```
The state does not satisfy state.schema.
  stage: Expected one of draft, review, approved, rejected but got "shipped".
  reason: Expected one of scope, cost, timing but got "vibes".
```

What is left to fail during evaluation is short — a value of the wrong kind, an ordering
comparison against null, division by zero, a `branch.match` with no matching case, and a set
of effects that builds a state the schema forbids. Reading past the end of a sequence and
reading a square off the board are not on that list: they produce null, and rule sets are
built on their doing so.

## Documents

Three of them — `rulealize/ruleset/v1` above, and the two that travel per call. The core
fixes only the frame.

```jsonc
// rulealize/state/v1
{ "$schema": "rulealize/state/v1", "ruleSet": "reversi@1.0.0",
  "data": { "board": { "d4": "white", … }, "turn": "black", "passes": 0 } }

// rulealize/input/v1
{ "$schema": "rulealize/input/v1", "ruleSet": "reversi@1.0.0",
  "input": "place", "args": { "at": "d3" } }
```

How each field inside `data` becomes JSON is decided by the schema node that declared it — a
board is a sparse coordinate map because a grid plugin says so, and changing it to a dense
array would touch one file in that plugin and nothing else.

A state document is read when the `ruleSet` it names matches on identifier and major
version, so `reversi@1.0.0` and `reversi@1.4.2` are interchangeable and `reversi@2.0.0` is
not. Anything a revision did to the shape of the state is the schema's business rather than
the version's.

## API

| Member | |
| --- | --- |
| `RuleRuntime.AddPlugin` / `LoadPlugins` / `LoadPluginsFrom` | build the vocabulary |
| `RuleRuntime.Plugins` / `RuleRuntime.Operations` | which vocabularies are loaded, and every operation they provide |
| `RuleRuntime.CreateContext` / `CreateContextAsync` | compile a rule set |
| `RuleContext.InitialState` | the opening position, as a state document |
| `RuleContext.ApplyToState` / `ApplyToStateAsync` | apply an input to a state |
| `RuleContext.GetValidInputs` | what is legal from here |
| `RuleContext.GetTerminalStatus` | whether a state is final, and its outcome |
| `PluginRequirement.ReadFrom` | read a document's `requires` — no runtime, no plugin loaded |
| `PluginResolution.Resolve` | which versions those constraints call for, given what is published |

The last two are what a tool needs before there is a runtime to load anything into, and they
are here so that resolving and running cannot read `^1.0` differently
([why](doc/runtime.md#requires-read-before-there-is-a-runtime)).

Exceptions: `RuleSetBuildException` for a document that is not a valid rule set,
`RuleDocumentException` for a state or input document this rule set cannot accept,
`IllegalInputException` for a move the rules do not allow, `RuleEvaluationException` for
values that make an operation meaningless, and `PluginLoadException` for a set of plugins
that cannot be used together.

A context is immutable and holds no position, so one serves any number of concurrent games.

The methods taking a `string` are synchronous, because evaluation is pure computation over
documents already in memory; the `Async` overloads exist for the one thing that is genuinely
I/O, reading a document off a stream. That, along with snapshot semantics, the caching and
purity rules for definitions, and how `validationLimit` behaves, is in
[`doc/runtime.md`](doc/runtime.md).

## What the core knows

Eight reserved keys, and one more for telling a node from anything else:

```
$schema  id  version  requires  state  definitions  inputs  terminal        op
```

Everything else in the document is vocabulary. A node is an object carrying an `op`; the
value of `op` selects a factory from a table the plugins filled in, and the rest of the
object is that plugin's business. The core never sees a plugin type and never learns what
an operation does — not even that `$board` is shorthand for reading a state field, which is
a string expansion a plugin registered against a character it reserved. Three of those
expansions come with the standard vocabulary:

```jsonc
"$board"    // = { "op": "state.get",  "path": "board" }
"@at"       // = { "op": "bind.local", "name": "at" }
"#opponent" // = { "op": "def.ref",    "name": "opponent" }
```

That is why `requires` is worth reading. It lists the vocabularies a rule set draws on, and
it can only say something because the standard set is cut finely: a rule set that needs
`Rulealize.Plugin.Arithmetic` is one that counts something.

Nodes come in three kinds — expression, effect and schema — and where each may appear is
enforced at compile time. [`doc/runtime.md`](doc/runtime.md#the-three-kinds-of-node-and-when-things-fail)
has the table.

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

What a name that is never published still has to avoid, and what `GetValidInputs` costs an
operation that reaches past its arguments, is in
[the standard vocabulary](doc/plugin.md#a-vocabulary-that-is-not-distributed).

`requires` keeps working throughout, and that is the point of doing it this way rather than
inventing a lighter registration path. A rule set naming `Acme.Deploy.Rules` is refused by a
runtime without it, with the name in the message — the same failure as for a plugin that was
not on the feed. [`sample/Deploy/`](sample/Deploy/) is the worked example.

## Repository layout

| | |
| --- | --- |
| [`src/`](src/) | the runtime |
| [`test/`](test/) | xUnit tests — `dotnet test` |
| [`sample/`](sample/) | one directory per sample application — see [`sample/README.md`](sample/README.md) |
| [`ruleset/`](ruleset/) | the rule set documents, one copy of each |
| [`doc/`](doc/README.md) | the standard vocabulary, and the runtime's semantics |

A rule set lives in one place and is consumed from two: the test suite compiles every
document in `ruleset/`, and a sample links the one it demonstrates.

## Documentation

[`doc/`](doc/README.md) holds two things, and both are normative.

**The specification** is what you read to write a rule set: [the value model and the three
kinds of node](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/value-model.md),
which `Rulealize.Abstraction` carries because it is what both sides depend on, then [the
standard vocabulary](doc/plugin.md), whose entries each link to a specification
released by that plugin's own repository.

**[The runtime's semantics](doc/runtime.md)** is what the library does with a rule set:
snapshot semantics, definitions and their cache, `validationLimit`, where asynchrony
belongs, and what has to survive the round trip out through JSON and back.

## License

Apache-2.0.
