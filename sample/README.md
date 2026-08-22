# Samples

One directory per sample application. Each is a self-contained host: it builds the standard
plugins into a `plugin` folder beside its executable, loads them by scanning that folder,
and compiles a rule set.

All but **Deploy** name no plugin type at all, so each shows the same discovery path a
deployed application takes. Deploy is the exception, and deliberately: the standard
vocabularies found by scanning, and one more that is a class in the sample itself.

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
| [`Blackjack/`](Blackjack/) | a hand of blackjack, where the next state is not whoever moves' to decide | `dotnet run --project sample/Blackjack -- --auto --odds` |

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

What the sample chooses not to do is let the vocabulary reach outside. Today's date is a
state field handed to `acme.frozen` as an argument rather than a clock read, because
`GetValidInputs` evaluates a guard once per candidate in a parameter's domain and a clock
would be read once per candidate too. What the other choice costs, and why the runtime
allows it, is in
[the standard vocabulary](../doc/plugin.md#a-vocabulary-that-is-not-distributed).

**Blackjack** is the one where whoever moves does not settle what happens. ann says `hit`
and the deck says which card, and those are two questions asked with two calls:
`GetValidInputs` for the move, `GetOutcomes` for the thirteen ranks that could arrive and
how likely each of them is. Read it after Reversi — the loop is the same two calls in the
same order, and the only difference is that the inner one turns thirteen times instead of
once. Chess's `--perft` walks its tree through exactly that loop, and the published numbers
still agree.

**There is no random number generator below the runtime.** Sampling one of the outcomes is
five lines in `Program.cs`, marked as such, and it is the only place chance enters. That is
what keeps a hand replayable: an input document and an outcome document determine the next
state between them, so a recorded pair produces the state it was recorded against.
`--seed 7` deals the same hand every time for the same reason.

`--odds` prints, before each decision, the exact chance it busts the hand on the next card.
No search and no sampling — the outcomes of one move are the whole distribution, and that
same sum over probability × value is the base case of an expectimax.
