# Samples

One directory per sample application. Each is a self-contained host: it builds the twelve
standard plugins into a `plugin` folder beside its executable, loads them by scanning that
folder, and compiles a rule set.

The first four name no plugin type at all, so each shows the same discovery path a deployed
application takes. **Deploy** is the exception, and deliberately: twelve vocabularies found
by scanning, and a thirteenth that is a class in the sample itself.

The rule sets are not copies. Every one of them lives in [`ruleset/`](../ruleset/) and is
linked into both the sample that demonstrates it and the test suite that pins it down, so
anything a sample does can be read next to its tests.

| | | |
| --- | --- | --- |
| [`Reversi/`](Reversi/) | a playable Reversi in the terminal | `dotnet run --project sample/Reversi -- --auto` |
| [`Chess/`](Chess/) | chess, and `--perft` to count the legal move tree against the published numbers | `dotnet run --project sample/Chess -- --perft 3` |
| [`Shogi/`](Shogi/) | shogi, including drops — two inputs of different shapes in one rule set | `dotnet run --project sample/Shogi -- --auto` |
| [`Roster/`](Roster/) | a shift roster, which is not a game at all: no turn, no opponent, no board | `dotnet run --project sample/Roster -- --solve` |
| [`Deploy/`](Deploy/) | a deployment pipeline, with four operations the host provides itself | `dotnet run --project sample/Deploy -- --auto` |

Each runs interactively when given no arguments.

## What each one is for

**Reversi** is the shortest complete host there is: load, compile, ask what is legal, apply
what was chosen. Read this one first.

**Chess** shows what a move looks like when its destination depends on its origin. It is one
parameter carrying `from|to|tag`, because two parameters would make every position a
64 × 64 candidate space, so the host has to take the token apart to accept `e2e4` — that is
the cost, and about twenty guard evaluations instead of 4,096 is what it buys.
`--perft <depth>` counts the leaves of the legal move tree and compares them with the
published values; depth 4 is roughly two hundred thousand positions.

**Shogi** has two inputs of different shapes in one rule set — a `move` whose three parts
travel as one token, and a `drop` whose two parameters genuinely are independent — so the
host has to keep them apart. `ValidInput.Arguments` holds only the keys its own input
declares, which means anything asking about `m` filters on `Input` first.

**Roster** is the one that is not a board. Assigning people to shifts: no turn, so
`ValidInput.Actor` is null; no winner, so the outcome is `complete` or `stuck`; and no
`grid.` anywhere in the rule set. `GetValidInputs` is read here as "who may still be put on
what", which is both what a scheduling screen displays and what `--solve` branches on.

`--state other-week` runs a different week — other people, three days instead of five —
through the same rule set, because the people were never in the rule set.

**Deploy** is the one that does not get its whole vocabulary from a folder. `deploy.json`
uses four `acme.` operations that come from `DeployVocabulary`, a class in this project,
reaching the runtime through `AddPlugin`. The reason they are not a plugin is the
constructor: three of them answer from the organisation's freeze calendar and ownership map,
and a plugin discovered by scanning is built through a parameterless constructor with
nowhere to receive either. The fourth, `acme.newer`, is an algorithm — semantic version
precedence, which `cmp.lt` gets backwards because text ordering puts `2.4.0-rc.1` after
`2.4.0` — and no arrangement of the standard vocabulary computes it.

`--policy lockdown` and `--state friday` are the two axes, and they are worth running
together. Neither changes a character of `deploy.json`; one is a field in the state
document and the other is a table the rule set has never seen, and both end with everything
staged, everything signed off, and `blocked`.

What the sample does not do is let the vocabulary reach outside. Today's date is a state
field handed to `acme.frozen` as an argument rather than a clock read, because
`GetValidInputs` evaluates a guard once per candidate in a parameter's domain and needs the
same answer every time. The conventions this follows are in
[the standard vocabulary](../doc/plugin.md#a-vocabulary-that-is-not-distributed).
