# Rulealize.Plugin.Comparison

| | |
| --- | --- |
| Identifier | `Rulealize.Plugin.Comparison` |
| Namespace | `cmp` |
| Version | `1.0.0` |
| Reserved prefix | none |
| Depends on | [the value model](../value-model.md), and nothing else |

Equality, ordering, and asking about null.

Separate from [Arithmetic](Arithmetic.md), because comparison applies to `Text` and
`Opaque` as well and therefore reaches further than anything numeric does. Reversi uses
`math.add` in exactly one place and `cmp` everywhere.

Three-way comparison lives here rather than in `math` for the same reason.

## Nodes

| Node | Kind | Used in Reversi |
| --- | --- | --- |
| `cmp.eq` | expression | ○ `flips1`, `terminal.result` |
| `cmp.ne` | expression | — |
| `cmp.lt` / `cmp.lte` / `cmp.gt` / `cmp.gte` | expression | ○ `gte` in `terminal.when` |
| `cmp.compare` | expression | ○ `terminal.result` |
| `cmp.isNull` | expression | ○ `canPlace`, `terminal.when` |
| `cmp.coalesce` | expression | — |

---

## Equality — `cmp.eq` / `cmp.ne`

### Form

```jsonc
{ "op": "cmp.eq", "left": <expression>, "right": <expression> }
{ "op": "cmp.ne", "left": <expression>, "right": <expression> }
```

### How it evaluates

Both sides are evaluated and compared by the equality rules of
[value model §2](../value-model.md). `cmp.ne` is the negation of `cmp.eq`.

**Null-safe.** Neither side being `Null` is a fault.

| Left | Right | `cmp.eq` |
| --- | --- | --- |
| `Null` | `Null` | `true` |
| `Null` | anything not `Null` | `false` |
| `"black"` | `"black"` | `true` |
| `1` | `"1"` | `false` (different kinds) |

### What null-safety buys

This is the property Reversi's `flips1` is built on.

```jsonc
{
  "op": "cmp.eq",
  "left": { "op": "grid.at", "grid": "$board",
            "coord": { "op": "seq.elementAt", "source": "@ray",
                       "index": { "op": "seq.count", "source": "@run" } } },
  "right": "#me"
}
```

When the ray is opponent stones all the way to the edge, `seq.elementAt` goes out of range
and returns `Null`, and `grid.at` returns `Null` too. If `cmp.eq` faulted there, whoever
writes the rule set has to add an explicit "is the ray longer than the run" check. Being
null-safe removes that step.

**Keeping boundary checks out of the DSL is the whole aim.** Ordering is deliberately not
null-safe, and the asymmetry is the point: "a value that is absent does not equal that
value" has one obvious reading, while "is an absent value larger or smaller" has none.

### Comparing across kinds

Different kinds are simply unequal; this is not a fault. That is what lets the cell value
`grid.at` returns — `Text` or `Null` — be compared against `Text` without a kind check at
every site.

---

## Ordering — `cmp.lt` / `cmp.lte` / `cmp.gt` / `cmp.gte`

### Form

```jsonc
{ "op": "cmp.gte", "left": <expression>, "right": <expression> }
```

### How it evaluates

Both sides are evaluated, ordered, and the answer returned as `Bool`.

### What can be ordered

| Kind | Order |
| --- | --- |
| `Number` | numeric |
| `Text` | ordinal, by code point; culture-independent |
| `Bool` | `false` < `true` |
| anything else | an evaluation fault |

Text ordering is fixed as ordinal so that a rule set cannot change meaning with the locale
it runs under.

### Null

**Either side being `Null` is an evaluation fault.** Unlike equality, this is not
null-safe.

An order has to be total to mean anything, and there is no natural total order that
includes `Null` — SQL makes you say `NULLS FIRST` or `NULLS LAST`, and most languages
refuse the comparison outright. Rather than freezing an arbitrary choice into the
specification, it faults and whoever writes the rule set reaches for `cmp.isNull` or
`cmp.coalesce`.

### Mismatched kinds

**An evaluation fault.** Unlike equality, comparing across kinds is not allowed:
`cmp.lt(1, "a")` has no meaningful answer.

### Example (Reversi's `terminal.when`)

```jsonc
{ "op": "cmp.gte", "left": "$passes", "right": 2 }
```

`passes` is a `type.int` bounded 0 to 2, so it is never `Null` and never runs into the
restriction above.

---

## `cmp.compare`

Three-way comparison.

### Form

```jsonc
{ "op": "cmp.compare", "left": <expression>, "right": <expression> }
```

### How it evaluates

Compares by the ordering rules above and returns the result as `Text`.

| Relation | Result |
| --- | --- |
| `left` < `right` | `"lt"` |
| `left` = `right` | `"eq"` |
| `left` > `right` | `"gt"` |

Text rather than a number, so it meshes directly with [Branch](Branch.md)'s `branch.match`.
Returning `-1` / `0` / `1` would make the case keys read `"-1"`, which is nobody's idea of
legible.

Null and mismatched kinds behave as they do for ordering: an evaluation fault.

### Example (Reversi's `terminal.result`)

```jsonc
{
  "op": "branch.match",
  "value": { "op": "cmp.compare", "left": "@b", "right": "@w" },
  "cases": { "gt": "black", "lt": "white", "eq": "draw" }
}
```

Counting the stones and naming a winner. One three-way comparison rather than a `cmp.gt`
and a `cmp.lt` nested inside a `branch.if`, because the nested form is the one where the
draw gets forgotten.

---

## `cmp.isNull`

### Form

```jsonc
{ "op": "cmp.isNull", "value": <expression> }
```

### How it evaluates

Evaluates `value` and returns `true` when it is `Null`.

### Example (Reversi's `canPlace`)

```jsonc
{ "op": "cmp.isNull", "value": { "op": "grid.at", "grid": "$board", "coord": "@at" } }
```

"That square is empty", paired with [Grid](Grid.md)'s decision that `grid.at` answers
`Null` for an empty square.

`cmp.eq(x, null)` decides the same thing. The dedicated node exists to say so: a bare JSON
`null` written as an expression leaves the reader working out whether it is structure or a
value.

---

## `cmp.coalesce`

### Form

```jsonc
{ "op": "cmp.coalesce", "of": [<expression>, ...] }
```

### How it evaluates

Evaluates `of` in order and returns the first value that is not `Null`, short-circuiting
there. All `Null`, or an empty array, gives `Null`.

Mostly for flattening a `Null` into a default before ordering or arithmetic. Not used in
Reversi.

---

## Decided

- **`Sequence` and `Record` cannot be ordered**, and stay an evaluation fault. Defining a
  lexicographic order would be easy and nothing has ever wanted one.
- **`Opaque` cannot be ordered.** The trigger written down for reconsidering this was
  wanting `GetValidInputs` to produce a stable output order — and that turns out to be
  satisfied already, by construction rather than by sorting: `grid.coords` enumerates
  deterministically, `rec.keys` is ordinal, and candidates are the product of domains
  walked in order. The condition never fired. Ordering opaque values would also mean
  putting "an opaque value that provides an order" into the value model itself, which is a
  concept the model does not otherwise need.
- **No `cmp.between`.** It is `logic.and` over two of `cmp.lte`, and across five rule sets
  range tests have not been common enough to be worth a second way to write one.
