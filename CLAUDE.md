## Project
Rulealize — "Plugin-oriented state transition and rule execution runtime driven by declarative JSON DSL" (README.md).

One C# class library (`src/Rulealize.csproj`, `net10.0`, nullable + implicit usings enabled), with an xUnit suite in `test/` and six host applications in `sample/`.

This project is designed to reference the base classes defined in a separate NuGet package, `Rulealize.Abstraction`.

## Architecture
Rulealize follows a typical plugin architecture with the following principles:

- Rulealize has no knowledge of any plugin-specific types.
- Plugins do not reference Rulealize.
- Rulealize depends only on `Rulealize.Abstraction`.
- Plugin DLLs are loaded at runtime, and the capabilities they provide are registered automatically.

## Naming
Folder names and namespaces are singular by default (`src/Internal/Document`, `src/Internal/Evaluation`, `ruleset/`).

Folders the build produces are folders too, and follow the same rule: the plugin DLLs land in `plugin` beside the executable, and the linked rule set documents in `RuleSet`. A source tree that is singular and an output tree that is not would leave the question the rule exists to remove.

Plural is used only where it carries meaning the singular does not:

- The name would otherwise collide with a type — `Rulealize.Internal.RuleSet` stays singular because no such type lives in it, whereas a namespace holding a `RuleSet` type would need the plural, for the same reason `System.Collections` has it.
- A test class suffix — `ChessTests` marks a class of tests, which is what distinguishes it from a helper like `StandardRuntime` sitting in the same folder.

The default is singular rather than plural because words like `Building` and `Evaluation` have no plural form, so a repository can only ever be consistent in the singular direction. Deciding once removes the per-folder question of whether the name describes a container or its contents.

## JSON DSL
The JSON DSL is designed to define generic rules.

It is intended to describe the rules of systems such as board games (e.g. Reversi, Shogi), simulations, and other rule-based applications.

The set of features available in the JSON DSL depends entirely on the plugins loaded by the library.

The library achieves its extensibility and generality through its plugin system.

## Usage Overview

### State Transition Based on Rules

Create a `RuleRuntime` instance and load the required plugins into it.

`RuleRuntime` provides a `CreateContext` method that creates a `RuleContext` instance from a RuleSet JSON document.

`RuleContext` provides an `ApplyToState` method. This method accepts an input document and a state document, applies the input to the state, and returns where the transition arrived.

Asynchrony belongs at the boundary only. Evaluation is pure computation over in-memory documents, so the `string` overloads are synchronous; the `Async` suffix is reserved for the `Stream` overloads, where reading a document genuinely is I/O (`CreateContextAsync`, `ApplyToStateAsync`).

### What May Happen Next

Not every rule set settles its next state from the input alone. Where an operation resolves something nobody chose — a card off a deck — the input has more than one outcome, and `ApplyToState(input, state)` refuses it with an `InvalidOperationException` rather than picking one.

`RuleContext.GetOutcomes` enumerates those outcomes with a probability on each, most likely first, and each carries the state it leads to. `ApplyToState(input, state, outcome)` replays one that already happened, which is what makes a recorded transition reproduce the state it was recorded against. The runtime never generates the choice; sampling one belongs to the host.

An input that draws nothing has exactly one outcome, of probability one, so a traversal is `GetValidInputs` then `GetOutcomes` whether a rule set has chance in it or not.

### Retrieving Valid Inputs for a Given State

Use `RuleContext.GetValidInputs`.

Its parameters are the current State and a validation limit.

The validation limit exists to prevent combinatorial explosion during rule evaluation.