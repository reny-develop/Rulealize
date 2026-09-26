# A rule set in five minutes

Enough to read one and to write one. [The full guide](ruleset-guide.md) is the same subject
with the reasons left in, and every heading below says where to find the long version.

A rule set is a JSON document. It declares what a position looks like, what may be done to
one, and when there is nothing left to do. Load it into a runtime and you can ask three
questions about any position: **what is legal here**, **where does this move lead**, and **is
this over**.

---

## One whole rule set

Count up to ten, one to three at a time. Nothing has been left out of this — it is a complete
document, and it is [`ruleset/countdown.json`](../ruleset/countdown.json), which the test
suite compiles and compares against what is printed here.

```jsonc
{
  "$schema": "rulealize/ruleset/v1",   // a label; the runtime does not read it
  "id": "countdown",                   // what this rule set is called
  "version": "1.0.0",

  // Everything this document is allowed to say comes from these six. Nothing is built in,
  // so a rule set that adds two numbers has to ask for arithmetic.
  "requires": [
    { "plugin": "Rulealize.Plugin.TypeSchema", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.State",      "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Comparison", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Arithmetic", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Sequence",   "version": "^1.4" },
    { "plugin": "Rulealize.Plugin.Binding",    "version": "^1.0" }
  ],

  // A position is one number between 0 and 10, and a new one starts at 0.
  "state": {
    "schema":  { "total": { "op": "type.int", "min": 0, "max": 10 } },
    "initial": { "total": 0 }
  },

  "inputs": {
    "add": {
      // What you may call it with: 1, 2 or 3.
      "params": { "n": { "domain": { "op": "seq.range", "from": 1, "count": 3 } } },

      // Whether it is allowed right now: only if it does not overshoot.
      "when": {
        "op": "cmp.lte",
        "left": { "op": "math.add", "of": ["$total", "@n"] },
        "right": 10
      },

      // What it does.
      "effects": [
        { "op": "state.set", "path": "total",
          "value": { "op": "math.add", "of": ["$total", "@n"] } }
      ]
    }
  },

  // When there is nothing left to do, and what to call the ending.
  "terminal": {
    "when": { "op": "cmp.eq", "left": "$total", "right": 10 },
    "result": "done"
  }
}
```

`$total` reads the state field `total`. `@n` reads the argument the move was called with.
Both are shorthand, explained below.

---

## What that buys you

```console
$ rulealize restore countdown.json
  …
6 plugins -> plugin
'countdown.json' compiles against it.

$ rulealize moves countdown.json
countdown@1.0.0 from the initial state (ongoing)
add(n: 1)
add(n: 2)
add(n: 3)
3 legal inputs, 3 candidates evaluated
```

Nothing in the runtime knows how to count to ten. It read the document.

**Read the next one twice.** From a position of 8:

```console
$ rulealize moves countdown.json --state at8.json
countdown@1.0.0 from 'at8.json' (ongoing)
add(n: 1)
add(n: 2)
2 legal inputs, 3 candidates evaluated
```

Three candidates, two legal. That is the division the whole language turns on:

- a **domain** says what an argument *may* be — here, always 1, 2 or 3
- a **guard** (`when`) says whether *this particular* argument is allowed *right now*

One input with a domain of three is three moves, and the guard is what removes `add(n: 3)`
when the total is 8. Then:

```console
$ rulealize moves countdown.json --state at10.json
countdown@1.0.0 from 'at10.json' (terminal (done))
no legal input
```

Applying a move hands back the next position, as a document:

```console
$ rulealize apply countdown.json "add(n: 3)"
add(n: 3) applied to the initial state
{
  "$schema": "rulealize/state/v1",
  "ruleSet": "countdown@1.0.0",
  "data": {
    "total": 3
  }
}
```

A position is a string. Store it in a column, resume it on another machine.

---

## The ten keys

These are the only keys the core knows at the top level. Misspell one and the document is
refused — it will not be quietly ignored.

| key | | |
| --- | --- | --- |
| `id`, `version` | what this rule set is called | **required** |
| `requires` | the vocabularies it may draw on | in practice required |
| `state` | `schema` (the fields) and `initial` (where one starts) | required |
| `inputs` | the named things that may be done | required |
| `terminal` | when it is over, and what to call the ending | optional |
| `definitions` | expressions given a name, so a rule is written once | optional |
| `uses`, `held` | rule sets this one holds, and what it may say about them | optional |
| `$schema` | a label, never read | optional |

Long version: [§1](ruleset-guide.md#1-the-shape-of-the-whole-document).

---

## Reading a node

Everything below those ten keys is a **node**: an object with an `op` in it.

```jsonc
{ "op": "cmp.eq", "left": "$stage", "right": "draft" }
```

`op` is the operation's name — the node's verb. Read that as `eq($stage, "draft")`, written as
an object because a rule set is data, not code. The name has two halves:

```
cmp . eq          cmp = which vocabulary        eq = which of its operations
```

`cmp` is claimed by `Rulealize.Plugin.Comparison`, which is why that line is in `requires`.
Nodes nest: wherever a value belongs, another node may go instead.

Three string shorthands save the most common nodes:

| written | means |
| --- | --- |
| `"$total"` | the state field `total` |
| `"@n"` | the argument or binding named `n` |
| `"#opponent"` | the definition named `opponent` |

And every operation is one of three kinds, which decides where it may be written:

| kind | | goes in |
| --- | --- | --- |
| **expression** | computes a value | `when`, `domain`, effect arguments, `terminal` |
| **effect** | writes to the state | only inside `effects` |
| **schema** | declares a field's type | only inside `state.schema` |

Long version: [§3](ruleset-guide.md#3-nodes). What an `op` you have never seen means:
[§14](ruleset-guide.md#14-reading-a-vocabulary-you-have-not-met).

---

## Writing one, in this order

1. **`id` and `version`.** Anything; they are how documents refer to each other.
2. **`state.schema`.** List the fields, give each a type. Then `state.initial`, one value per
   field. Ask of every field: is this a *rule*, or is it *this instance*? People, dates and
   prices usually belong in the state document, not in the schema.
3. **One input, `effects` first.** Get one thing writing to the state.
4. **Add `when`.** Now it is only allowed sometimes.
5. **Add `params` if the move takes an argument**, with a `domain` saying what it may be.
   Repeat 3–5 per input.
6. **`terminal`**, if the thing ends.
7. **`requires`.** Every vocabulary you reached for, including the one behind `$`, `@` or `#`.
   Get it wrong and the document is refused by name, so this is cheap to fix.

Then `rulealize restore x.json` and `rulealize moves x.json`, and read what comes back.

Long version: [§13](ruleset-guide.md#13-shaping-a-rule-set) is the same list with the mistakes
that produced it.

---

## When things break

Almost everything is caught up front, when the document is compiled — before a single move is
evaluated. The message carries the path of the offending node.

| caught when the document loads | caught while it runs |
| --- | --- |
| an unknown `op`, or one from a vocabulary `requires` does not name | a value of the wrong kind |
| a key nobody knows, a missing required key | ordering or arithmetic against null, division by zero |
| a node in a position its kind does not allow | a `branch.match` with no matching case |
| a path that is not a field of the schema | effects that build a state the schema forbids |
| an undefined or circular definition | |

Reading past the end of a list, or off the side of a board, is on **neither** list: it gives
null, and rules are written to rely on that.

Long version: [§12](ruleset-guide.md#12-reading-an-error).

---

## Where to go next

| | |
| --- | --- |
| [the full guide](ruleset-guide.md) | this, with the reasons — values, nesting, chance, composition |
| [`ruleset/approval.json`](../ruleset/approval.json) | the next step up: three inputs, so there is a choice of what to do as well as what to do it with |
| [`ruleset/roster.json`](../ruleset/roster.json) | no turn, no board, no opponent — proof this is not a game engine |
| [`ruleset/reversi.json`](../ruleset/reversi.json) | a whole game, still short |
| [the README](../README.md) | running one from C# rather than from the command line |
