# Rulealize.Plugin.TypeSchema

| | |
| --- | --- |
| Identifier | `Rulealize.Plugin.TypeSchema` |
| Namespace | `type` |
| Version | `1.1.0` |
| Reserved prefix | none |
| Depends on | [the value model](../value-model.md), and nothing else |

The vocabulary `state.schema` is written in.

**What this plugin provides is [schema nodes](../value-model.md#4-the-three-kinds-of-node),
not expression nodes.** They appear only inside `state.schema` and are never evaluated.

A board comes from [Grid](Grid.md)'s `grid.board`; TypeSchema supplies the type that goes
in its `cell`. The two mesh only through the value model — Grid does not reference
TypeSchema, and `grid.board` takes "some schema node" for its `cell` without knowing what.

## Nodes

| Node | Kind | Used in Reversi |
| --- | --- | --- |
| `type.enum` | schema | ○ `board.cell`, `turn` |
| `type.int` | schema | ○ `passes` |
| `type.bool` | schema | — |
| `type.string` | schema | — |
| `type.list` | schema | — (added in 1.1) |

## `type.list`

```jsonc
{
  "op": "type.list",
  "element": <schema node>,      // required
  "minLength": <integer>,        // optional, static
  "maxLength": <integer>         // optional, static
}
```

The value is a **`Sequence`**. The JSON form is an array.

### Why a collection lives in a scalar vocabulary

The line this plugin holds is not "is it a scalar" but **"does it have expression nodes"**.
`type.list` needs no reading vocabulary at all — the value is a `Sequence`, so `seq.count`,
`seq.any` and `seq.where` read it from day one.

```jsonc
{ "op": "seq.count", "source": "$history" }
```

Writing is `state.set` with an expression returning a `Sequence`; there is no dedicated
effect node.

```jsonc
{ "op": "state.set", "path": "history",
  "value": { "op": "seq.concat", "of": ["$history", { "op": "seq.of", "of": ["@position"] }] } }
```

[Records](Record.md), by contrast, had no existing vocabulary whatsoever, and became a
plugin of their own. **The asymmetry is real and has a reason**
(→ [collections](../collections.md)).

### The bound is not decoration

State is serialized on every transition, so an unbounded history grows the document with
the square of the move count.

And the bound is usually the rule rather than a compromise. The history chess needs for
threefold repetition only reaches back to the last irreversible move, and the fifty-move
rule caps that at a hundred plies.

Since the transition check below, a bound is also enforced rather than merely declared: a
rule set whose guard lets the list grow past its own `maxLength` is told so at the
transition that overran it.

### A lazy sequence is settled

The `Sequence` written into a `type.list` may be lazy. When the transition commits,
`SchemaNode.Normalize` enumerates it once, so a state never ends up holding a way to
recompute itself out of the state before it.

---

## What a schema node is for

`state.schema` serves three purposes.

1. **Validating states from outside** — a context that has been through `CreateContext`
   checks the state documents handed to it. Accepting states written by hand or produced by
   another system makes this unavoidable.
2. **Validating the opening position** — that `state.initial` satisfies the schema, checked
   at `CreateContext`.
3. **Validating what the effects built** — since the transition check below.

Running on `state.initial` alone is possible; it just gives up the first of these.

## The key every schema node takes

| Key | Default | |
| --- | --- | --- |
| `nullable` | `false` | whether `Null` is allowed. Static |

`nullable` is a shared modifier rather than a variant of each type so that Reversi's very
common shape — a cell is `black`, `white`, or empty — fits on one line.

---

## `type.enum`

A finite set of `Text` values.

### Form

```jsonc
{
  "op": "type.enum",
  "values": ["<value>", ...],   // static
  "nullable": <boolean>         // optional
}
```

### What it accepts

A `Text` equal to one of `values`. With `nullable`, also `Null`.

`values` may not be empty, and may not repeat; both are static errors.

### Example (Reversi)

```jsonc
"turn": { "op": "type.enum", "values": ["black", "white"] }
```

```jsonc
"cell": { "op": "type.enum", "values": ["black", "white"], "nullable": true }
```

A board cell is nullable because an empty square is `Null` — a design
[Comparison](Comparison.md)'s `cmp.isNull` and [Grid](Grid.md)'s `grid.at` both assume.

Making "empty" a third enum value `"empty"` was considered and rejected. It would make the
value of an empty square differ from what `grid.at` returns off the board, which breaks the
null propagation `flips1` depends on ([value model §3](../value-model.md)). **Collapsing
off-the-board and empty into the single idea "no stone here" is what keeps Reversi short.**

### The order of the values

The order of `values` carries no meaning. Use `type.int` where order is wanted.

---

## `type.int`

A whole number.

### Form

```jsonc
{
  "op": "type.int",
  "min": <integer>,       // optional, static
  "max": <integer>,       // optional, static
  "nullable": <boolean>   // optional
}
```

### What it accepts

A `Number` with no fractional part, between `min` and `max` inclusive. Either bound may be
omitted, and an omitted bound is unbounded.

The value model's `Number` does not distinguish integers from fractions, so "is a whole
number" is expressed here as a constraint.

### Example (Reversi)

```jsonc
"passes": { "op": "type.int", "min": 0, "max": 2 }
```

`max: 2` restates what the "two consecutive passes ends the game" rule already says, but
the two mean different things. `terminal.when` is a **rule about transitions** — reaching 2
ends it — while this is a **constraint on the state space**: no state with 3 exists.

The redundancy earns its keep by catching an invalid state from outside, such as
`passes: 5`. What it does not do is guarantee the two agree with each other — see below.

---

## `type.bool`

### Form

```jsonc
{ "op": "type.bool", "nullable": <boolean> }
```

The value has to be a `Bool`.

---

## `type.string`

### Form

```jsonc
{
  "op": "type.string",
  "minLength": <integer>,   // optional, static
  "maxLength": <integer>,   // optional, static
  "nullable": <boolean>     // optional
}
```

A `Text` whose length is in range.

Constraint by regular expression is **not provided**, so that validating a rule set never
depends on which regular expression dialect is underneath. Most cases wanting a pattern are
better served by `type.enum`.

---

## When validation happens

| What is checked | When |
| --- | --- |
| `state.initial` | at `CreateContext` |
| a state document from outside | on entry to `ApplyToState` / `GetValidInputs` |
| what an input's `effects` built | when the transition commits |

A failure names the path, the value, and the constraint it broke, and reports every
violation rather than the first.

---

## Decided

- **What the effects build is checked, on every transition.** This was left open on the
  grounds that validating the whole state on every transition costs too much, "especially
  where `GetValidInputs` is trying hundreds of candidates", and might be worth having only
  as a diagnostic mode. **That reasoning was wrong on its facts.** Candidate search never
  builds a state draft — it evaluates guards — so the hundreds of candidates behind a
  domain pay nothing. And only fields an input actually wrote are checked, since anything
  untouched came out of a document or an earlier commit and has been checked once already.
  The cost is a handful of checks per transition, and a diagnostic mode is not needed for
  something that cheap. `SchemaNode.Validate` had been part of `Rulealize.Abstraction` and
  implemented by every schema node all along, with nothing calling it.
- **A state-space constraint and a transition rule are still not checked against each
  other**, and cannot be without inference. But the disagreement is no longer silent. The
  `passes` example above is the shape of it: a `max` in the schema and a `terminal.when`
  that has to stop before it. When the two disagree, the transition that oversteps now
  fails and names the input, instead of returning a state that the next read would reject.
  That is a worse diagnostic than a static check and a much better one than nothing — and
  the repository's own test suite had a rule set with a `maxLength: 2` list and a guard
  admitting 99, with a test pinning the overrun as correct behaviour, which is how long a
  disagreement like this survives when nothing looks.
- ~~**`type.record` / structured schemas**~~ — resolved. Shogi's hand demanded it and it
  became [`rec.of` / `rec.map`](Record.md). Why the namespace is not `type` is in
  [collections](../collections.md).
- **Inference from schemas into expression types is not being built.** It would turn three
  run time faults into static ones — `branch.match`'s exhaustiveness, `cmp`'s kind
  mismatches, and `tuple.at`'s index bound — and it is the thing four other open questions
  across these specifications are waiting on. It is also the largest single piece of work
  the design has left. The condition for starting is the DSL settling down: **the vocabulary
  went 10 → 11 → 12 plugins over three rule sets**, and building inference over a set of
  node shapes that is still growing means rebuilding it. When a rule set gets written that
  needs no new vocabulary, that is the signal.
