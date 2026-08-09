# Samples

One directory per sample application. Each is a self-contained host: it builds the twelve
standard plugins into a `plugins` folder beside its executable, loads them by scanning
that folder, and compiles a rule set from its own `RuleSets` directory. None of them names
a plugin type, so each shows the same discovery path a deployed application takes.

The rule sets are the ones the test suite works on, so anything a sample does can be read
next to the tests that pin it down and the notes in [`doc/`](../doc/).

| | | |
| --- | --- | --- |
| [`Reversi/`](Reversi/) | a playable Reversi in the terminal | `dotnet run --project sample/Reversi -- --auto` |
| [`Chess/`](Chess/) | chess, and `--perft` to count the legal move tree against the published numbers | `dotnet run --project sample/Chess -- --perft 3` |
| [`Shogi/`](Shogi/) | shogi, including drops — two inputs of different shapes in one rule set | `dotnet run --project sample/Shogi -- --auto` |
| [`Roster/`](Roster/) | a shift roster, which is not a game at all: no turn, no opponent, no board | `dotnet run --project sample/Roster -- --solve` |

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
through the same rule set, because the people were never in the rule set. That distinction
is easy to miss after writing three board games: a chess board really is always eight by
eight, so baking the instance into the schema costs nothing there and everything here.

## Adding a sample

Create `sample/<Name>/` with a `Rulealize.Sample.<Name>.csproj` alongside a `RuleSets`
directory, then add it to [`Rulealize.slnx`](../Rulealize.slnx) under the `sample` folder
and to the table above. The project file needs three things beyond the usual:

```xml
<ProjectReference Include="..\..\src\Rulealize.csproj" />
<Import Project="..\..\StandardPlugins.props" />
<Content Include="RuleSets\*.json" CopyToOutputDirectory="PreserveNewest" />
```

`StandardPlugins.props` locates the plugin repositories relative to itself, so it needs no
adjusting for the extra directory level.
