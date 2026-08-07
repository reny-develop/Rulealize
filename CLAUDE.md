## Project
Rulealize — "Plugin-oriented state transition and rule execution runtime driven by declarative JSON DSL" (README.md).

Single C# class library (`src/Rulealize.csproj`, `net10.0`, nullable + implicit usings enabled).

This project is designed to reference the base classes defined in a separate NuGet package, `Rulealize.Abstraction`.

## Architecture
Rulealize follows a typical plugin architecture with the following principles:

- Rulealize has no knowledge of any plugin-specific types.
- Plugins do not reference Rulealize.
- Rulealize depends only on `Rulealize.Abstraction`.
- Plugin DLLs are loaded at runtime, and the capabilities they provide are registered automatically.

## JSON DSL
The JSON DSL is designed to define generic rules.

It is intended to describe the rules of systems such as board games (e.g. Othello, Shogi), simulations, and other rule-based applications.

The set of features available in the JSON DSL depends entirely on the plugins loaded by the library.

The library achieves its extensibility and generality through its plugin system.

## Usage Overview

### State Transition Based on Rules

Create a `RuleRuntime` instance and load the required plugins into it.

`RuleRuntime` provides a `CreateContext` method that creates a `RuleContext` instance from a RuleSet JSON document.

`RuleContext` provides an `ApplyToState` method. This method accepts an InputRule JSON document and a State JSON document, applies the rule to the state, and performs the corresponding state transition.

Asynchrony belongs at the boundary only. Evaluation is pure computation over in-memory documents, so the `string` overloads are synchronous; the `Async` suffix is reserved for the `Stream` overloads, where reading a document genuinely is I/O (`CreateContextAsync`, `ApplyToStateAsync`).

### Retrieving Valid Inputs for a Given State

Use `RuleContext.GetValidInputs`.

Its parameters are the current State and a validation limit.

The validation limit exists to prevent combinatorial explosion during rule evaluation.