# Rulealize.Plugin.State

| | |
| --- | --- |
| Identifier | `Rulealize.Plugin.State` |
| Namespace | `state` |
| Version | `1.0.0` |
| Reserved prefix | `$` |
| Depends on | [the value model](../value-model.md), and nothing else |

Reading and writing the state.

**Reading (an expression node) and writing (an effect node) live in one plugin** because
they share one path syntax and there is no sense in loading half of it. Split them and the
specification of a path splits in two along with them.

This plugin does not know the **structure** of the state. It does not interpret `board` as
a board or `turn` as an enumeration. It moves the value at a path in and out as a value of
the value model, and reading it as a board is [Grid](Grid.md)'s business.

## Nodes

| Node | Kind | Used in Reversi |
| --- | --- | --- |
| `state.get` | expression | ○ everywhere, as the sugar `$` |
| `state.set` | effect | ○ `inputs.place`, `inputs.pass` |
| `state.update` | effect | — |

---

## Paths

### What a path is

**The name of a field of `state.schema`, and nothing more.**

```
"turn"
"board"
"hand"
```

A path is **static**; it cannot be assembled by an expression. Three reasons.

- It can be checked against `state.schema` at `CreateContext`.
- Static analysis can say which fields an input writes.
- Dynamic paths would push schema validation out to run time.

### A path does not reach inside a field

`"board.d3"` is not a path. Neither is `"hand.black.P"`. The inside of a field belongs to
whichever plugin declared its schema — squares are `grid.at` and `grid.set`, record keys
are `rec.at` and `rec.set` — and this plugin treats the field as one opaque value.

**This is the seam the decomposition turns on.** If State knew a board's internal
representation — sparse or dense, which coordinate notation — Grid could no longer be
replaced. [Record](Record.md) follows the same seam deliberately, and
[collections](../collections.md) is where the choice is argued out.

There is no dotted syntax and none is reserved. An earlier version of this document
reserved one against future nested schemas, which was written before records existed;
records arrived, they nest, and the way to reach into one is `rec.at` rather than a path.
Reserving a syntax that the design has since decided against is worse than having none.

---

## `state.get`

### Form

```jsonc
{ "op": "state.get", "path": "<path>" }   // path is static
```

Sugar: `"$<path>"`

### How it evaluates

Reads `path` out of the current **state snapshot**.

Which snapshot that is depends on context.

| Context | What is read |
| --- | --- |
| `inputs.*.when` | the state handed to `GetValidInputs` or `ApplyToState` |
| `inputs.*.effects` | **the same** — the state before any effect ran |
| the body of a definition | whatever the caller's context is |
| `terminal` | the state being judged |

That `state.get` inside `effects` reads the value from **before** the effects is the
snapshot semantics of [value model §5](../value-model.md). Reversi's `inputs.pass` depends
on it.

```jsonc
{ "op": "state.set", "path": "passes",
  "value": { "op": "math.add", "of": ["$passes", 1] } }
```

Even if another element of the same `effects` array writes `passes`, `$passes` returns the
value the input arrived at.

### Errors

| Condition | When |
| --- | --- |
| `path` is not in the schema | static |
| `path` is an expression | static |

A value being `Null` is not an error, as long as the schema allows it.

### Example (Reversi)

```jsonc
"me": { "op": "state.get", "path": "turn" }
```

`$board` is what gets handed to the `grid` / `of` / `target` keys of the `grid.*` nodes.
State takes the board value out and never looks inside it.

---

## `state.set`

### Form

```jsonc
{ "op": "state.set", "path": "<path>", "value": <expression> }   // path is static
```

**An effect node.** It appears only as an element of `inputs.*.effects`.

### How it applies

1. `value` is evaluated **against the snapshot**.
2. The result is written to `path` in the draft.

Two writes to one path: **the last one wins**.

### Schema validation

The value written **is checked against the field's schema** when the transition commits,
after the schema has settled it. A rule set whose effects assemble a state the schema
forbids fails at the transition responsible, with the input named, rather than handing that
state back and failing on the next read. See [TypeSchema](TypeSchema.md) for what this
costs and why the earlier worry about the cost was misplaced.

### Example (Reversi's `inputs.place.effects`)

```jsonc
[
  { "op": "grid.set", ... },
  { "op": "grid.setMany", ... },
  { "op": "state.set", "path": "passes", "value": 0 },
  { "op": "state.set", "path": "turn", "value": "#opponent" }
]
```

`#opponent` derives from `#me` (that is, `$turn`), so snapshot semantics puts the opponent
of the colour that moved into the field. Applied one at a time, the result would depend on
whether an earlier effect had already written `turn`.

---

## `state.update`

Updates a field by reference to its current value.

### Form

```jsonc
{
  "op": "state.update",
  "path": "<path>",     // static
  "as": "<name>",       // static
  "value": <expression>
}
```

**An effect node.**

### How it applies

1. `path` is read from the snapshot and bound to `as`.
2. `value` is evaluated under that binding.
3. The result is written to `path` in the draft.

`state.set` combined with `state.get` does the same thing, and Reversi's `inputs.pass` is
written the latter way.

```jsonc
// equivalent
{ "op": "state.set", "path": "passes",
  "value": { "op": "math.add", "of": ["$passes", 1] } }

{ "op": "state.update", "path": "passes", "as": "n",
  "value": { "op": "math.add", "of": ["@n", 1] } }
```

The advantage is avoiding a repeated long path, which is not much. Reversi does not use it.

---

## The state document

What `ApplyToState` takes and returns.

```jsonc
{
  "$schema": "rulealize/state/v1",
  "ruleSet": "reversi@1.0.0",
  "data": {
    "board": { "d4": "white", "e4": "black", "d5": "black", "e5": "white" },
    "turn": "black",
    "passes": 0
  }
}
```

The keys directly under `data` are the fields of `state.schema`.

How each field's value becomes JSON is decided by the schema node that declared it. The
sparse form of `board` is `grid.board`'s doing ([Grid](Grid.md)); this plugin is not
involved.

What State fixes is the frame — `$schema`, `ruleSet`, `data` — and no more.

### Matching the `ruleSet`

A state document that names a `ruleSet` is read when **the identifier matches exactly and
the major version matches**. `reversi@1.0.0` and `reversi@1.4.2` are interchangeable;
`reversi@2.0.0` and `chess@1.0.0` are refused, with both names in the message.

The major version is the unit because it is where this project says meaning changed — the
same reading `requires` gives a plugin version with `^`. Nothing finer can be asserted
here: a minor revision may well have added a state field, and a document written before it
will be missing one. But that is a missing field, and the schema check names it. **Identity
is decided here and shape is decided there**, so a document from a compatible revision that
no longer fits says which field is wrong instead of collapsing into a version mismatch that
names none of them.

A document that names no `ruleSet` at all is accepted and checked against the schema like
any other. Declining to claim an identity is not the same as claiming the wrong one, and a
state written by hand has no reason to be forced into one.

---

## Decided

- **State version compatibility is identifier plus major version**, as above. This was
  open, and in the meantime the runtime compared the whole `id@version` string, so a state
  written by `reversi@1.0.0` could not be read by `reversi@1.1.0` — a revision that changed
  nothing about the state still invalidated every stored state.
- **Effects are validated against the schema on commit.** Was open; see
  [TypeSchema](TypeSchema.md) for why the objection to it did not hold up.
- **Paths do not nest, and no nesting syntax is reserved.** Recorded above.
- **No delta representation for states.** `ApplyToState` returns the whole state. A long
  game accumulating states would rather have diffs, and none of the five rule sets
  accumulates states — the one with a history, roster, bounds it at five entries and
  truncates. Worth revisiting when a rule set stores its own history unbounded, which the
  `maxLength` argument in [TypeSchema](TypeSchema.md) suggests should not happen.
- **No read-only derived fields.** Whether a value like a stone count should live in the
  state or be recomputed is settled in favour of recomputing: `terminal.result` counts with
  `seq.count`, and a derived field in the state is a second copy of a fact that
  [`state.schema` cannot check against the first](TypeSchema.md).
