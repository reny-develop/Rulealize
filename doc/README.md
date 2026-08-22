# Documentation

Two documents live here, and both are normative.

**The specification** says what the DSL is. It is what you read to write a rule set.

**[The runtime's semantics](runtime.md)** says what this library does with a rule set once
it has one. Read second: the DSL can be written without it, but nothing about
`GetValidInputs`, what `GetOutcomes` enumerates, or the order two effects see the state in
can be predicted without it.

## The specification

Read in this order. Every plugin specification assumes the value model, and says so.

| | Where it lives | |
| --- | --- | --- |
| [The value model, and the three kinds of node](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/value-model.md) | `Rulealize.Abstraction` | the kinds of value, equality, null propagation, and what expression, effect and schema nodes may do. What every plugin assumes, and the only thing holding plugins together |
| [The standard vocabulary](plugin.md) | here | the standard plugins and what each provides, plus what a vocabulary an application keeps to itself can do and what it costs |
| A specification per plugin | each plugin's own repository | what one version of one vocabulary provides, normatively. Reached from the table on the page above |
| [The runtime's surface](runtime.md) | here | node placement, snapshot semantics, definitions and their cache, `validationLimit`, draws and what `GetOutcomes` enumerates, asynchrony, the round trip an argument and a drawn value both make |

**Only the index is here** for the first three. The documents every specification assumes
describe types `Rulealize.Abstraction` defines, and a plugin's specification is released by
the plugin. The runtime's surface is the exception, because it describes this repository.

Getting a rule set running at all — the smallest complete document, the API, the exceptions,
loading plugins — is in [the README](../README.md). The worked rule sets live in
[`ruleset/`](../ruleset/), every one of them held down by a test and most of them
demonstrated by a sample; [`sample/README.md`](../sample/README.md) says what each one is
for.
