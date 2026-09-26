# Documentation

Four documents live here, in two halves. Two of them **teach** the DSL, and two of them
**define** it — and where a guide and a specification disagree, the specification is right
and the guide has a bug.

## The guides

Neither is normative. Both link into the half below at every point where the difference
could matter.

| | | |
| --- | --- | --- |
| [A rule set in five minutes](ruleset-quickguide.md) | start here | one complete document, annotated, and what a runtime answers when you feed it in. Enough to read a rule set and write a small one |
| [Writing a rule set](ruleset-guide.md) | then here | the same subject with the reasons in it, from an empty file to composition and chance. Fourteen sections, meant to be read straight through once |

The guides cover the language a document is written in. Getting one *running* — the API, the
exceptions, loading plugins — is [the README](../README.md).

## The specification

Normative, and split down one line: **what the DSL is**, which is what a rule set has to
satisfy, and **what this library does with a rule set once it has one**, which is
[the runtime's surface](runtime.md). A document can be written knowing only the first — but
nothing about `GetValidInputs`, what `GetOutcomes` enumerates, or the order two effects see
the state in can be predicted without the second.

Read in this order. Every plugin specification assumes the value model, and says so.

| | Where it lives | |
| --- | --- | --- |
| [The value model, and the three kinds of node](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/value-model.md) | `Rulealize.Abstraction` | the kinds of value, equality, null propagation, and what expression, effect and schema nodes may do. What every plugin assumes, and the only thing holding plugins together |
| [Vocabulary](plugin.md) | here | what decides which operations a rule set may write, where the published vocabularies are indexed, and what a vocabulary an application keeps to itself can do and what it costs |
| A specification per plugin | each plugin's own repository | what one version of one vocabulary provides, normatively. Released with the plugin, as `doc/specification.md` in its own repository |
| [The runtime's surface](runtime.md) | here | node placement, snapshot semantics, definitions and their cache, `validationLimit`, draws and what `GetOutcomes` enumerates, asynchrony, the round trip an argument and a drawn value both make |

**Nothing but a pointer is here** for the first three. The documents every specification
assumes describe types `Rulealize.Abstraction` defines, a plugin's specification is released
by the plugin, and the index of what is published is kept by Rulealize.Registry. The
runtime's surface is the exception, because it describes this repository.

## The worked documents

Neither half is where to look for a rule set to read. Those live in
[`ruleset/`](../ruleset/) — every one of them held down by a test, and most of them
demonstrated by a sample, which [`sample/README.md`](../sample/README.md) describes. The
smallest is [`countdown.json`](../ruleset/countdown.json), which the quick guide prints in
full; the largest are chess and shogi.
