# Documentation

Three kinds of document live here, and they are read for different reasons.

**The specification** says what the DSL is. It is what you read to write a rule set, and it
is normative — where it and a design note disagree, the specification is right.

**[The runtime's semantics](runtime.md)** says what this library does with a rule set once
it has one. Also normative, and read second: the DSL can be written without it, but nothing
about `GetValidInputs` or the order two effects see the state in can be predicted without
it.

**The design record** says how the DSL got that way. Each entry takes one subject, states
the question it was written to answer, and reports what came of it — including the times the
answer was not the expected one. None of it is required reading to use the library. It is
here because the claim this project makes — that the core knows nothing of any plugin, and
that general vocabulary is enough — is only worth anything with the evidence attached.

## The specification

Read in this order. Every plugin specification assumes the value model, and says so.

| | Where it lives | |
| --- | --- | --- |
| [The value model, and the three kinds of node](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/value-model.md) | `Rulealize.Abstraction` | the kinds of value, equality, null propagation, and what expression, effect and schema nodes may do. What every plugin assumes, and the only thing holding plugins together |
| [The standard vocabulary](plugin.md) | here | the twelve plugins and what each provides, plus the conventions for a vocabulary you keep to yourself |
| A specification per plugin | each plugin's own repository | what one version of one vocabulary provides, normatively. Reached from the table on the page above |
| [The runtime's surface](runtime.md) | here | node placement, snapshot semantics, definitions and their cache, `validationLimit`, asynchrony, the input round trip |

**Only the index is here** for the first three. Both of the documents every specification
assumes describe types `Rulealize.Abstraction` defines, and a plugin's specification ships
with the plugin so that it can change when the plugin releases and not before —
[the reasoning](plugin.md#why-the-specifications-are-not-in-this-repository). The runtime's
surface is the exception, because it describes this repository.

The shape of the three documents — RuleSet, State and InputRule — is worked out in
[the Reversi walkthrough](dsl-example-reversi.md) below, which is the closest thing to a
DSL tutorial here. Getting a rule set running at all — the smallest complete document, the
API, the exceptions, loading plugins — is in [the README](../README.md).

## The design record

In the order written. Each is self-contained, and each names its subject rule set in
[`ruleset/`](../ruleset/) and the tests that hold it to account.

| | The question | How it went |
| --- | --- | --- |
| [Reversi](dsl-example-reversi.md) | what would be enough JSON to write? | the ten plugins, the three documents, and six design decisions settled |
| [Chess](dsl-example-chess.md) | can an input whose destination depends on its origin be written without changing the core? | it can. Tuple, and Sequence and Grid 1.1, were the vocabulary that was missing |
| [Shogi](dsl-example-shogi.md) | does the compound parameter fall apart at a fourth element? | it does not — the prediction was wrong. What broke was `state.schema`, which had nowhere to hold a captured piece |
| [Collections in the state](collections.md) | how does a multiset or a sequence live in a state whose paths must stay literal? | `type.list`, the Record plugin, and Sequence 1.2 |
| [A shift roster](dsl-example-roster.md) | is the DSL board-shaped? three board games is not evidence of generality | it is not. `grid.` appears zero times |
| [A deployment pipeline](dsl-example-deploy.md) | what does a rule set look like when its vocabulary is not all published plugins? | `AddPlugin` takes an instance, and the contract does not change |

Reversi and chess come first for a reason: Reversi is where the design is worked out, and
chess is the first thing it had to survive that it was not designed against. The rest can be
read in any order.

### One that is not here

[The registry](https://github.com/reny-develop/Rulealize.Registry/blob/main/doc/design.md) —
an index of the plugins and rule sets other people publish, and the ledger of which
namespaces and shorthand characters are already taken — is a design record of the same kind,
and it is kept in its own repository. Its subject is the ecosystem around this library
rather than this library, so it belongs with the thing it describes, for the reason
[a plugin's specification does](plugin.md#why-the-specifications-are-not-in-this-repository).
The one thing it asks of this repository is a way to enumerate a runtime's operations, which
does not exist yet.
