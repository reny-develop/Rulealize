# Rulealize.Plugin.Sequence

| | |
| --- | --- |
| Identifier | `Rulealize.Plugin.Sequence` |
| Namespace | `seq` |
| Version | `1.2.0` |
| Reserved prefix | none |
| Depends on | [the value model](../value-model.md), and nothing else |

Building, transforming and folding the value model's `Sequence`.

**This plugin does not know where a sequence came from.** In Reversi it handles what
[Grid](Grid.md)'s `grid.ray`, `grid.coords` and `grid.directions` returned, and it does not
reference Grid. The two mesh because the value model defines `Sequence` as a shared kind.

**The clearest candidate for replacement.** Evaluation strategy — eager or lazy —
parallelism, and how intermediate results are buffered are all changeable by swapping this
plugin out, so long as re-enumerability below is preserved.

## Nodes

| Node | Kind | Used in Reversi |
| --- | --- | --- |
| `seq.empty` | expression | ○ `flips1` |
| `seq.of` | expression | — (added in 1.1) |
| `seq.any` | expression | ○ `canPlace`, `hasAnyMove`, `terminal.when` |
| `seq.count` | expression | ○ `flips1`, `terminal.result` |
| `seq.elementAt` | expression | ○ `flips1` |
| `seq.takeWhile` | expression | ○ `flips1` |
| `seq.selectMany` | expression | ○ `flips` |
| `seq.where` | expression | — |
| `seq.select` | expression | — |
| `seq.concat` | expression | — (added in 1.2) |
| `seq.take` | expression | — (added in 1.2) |
| `seq.skip` | expression | — (added in 1.2) |

---

## What a sequence is

### Finite

Every sequence is finite. No node produces an infinite one.

`GetValidInputs` walks sequences end to end, so termination is guaranteed by the
specification rather than hoped for — the same reason [Definition](Definition.md) refuses
recursion.

### Re-enumerable (required)

As [value model §1.2](../value-model.md) says, **enumerating one sequence value more than
once has to produce the same run of values each time**.

Reversi's `flips1` is what demands it.

```jsonc
"bind": {
  "ray": { "op": "grid.ray", ... },
  "run": { "op": "seq.takeWhile", "source": "@ray", ... }   // ← first enumeration
},
"in": {
  ...
  "coord": { "op": "seq.elementAt", "source": "@ray", ... }  // ← second
}
```

`@ray` is evaluated once by `bind.let`, and the sequence it produced is enumerated twice.
Implement a lazy sequence as a single-use iterator and the second enumeration comes back
empty and the rule quietly breaks.

Two ways to satisfy it:

- re-run the underlying computation on each enumeration (pure, so the result is the same)
- buffer on the first enumeration

Either is fine. **A single-use iterator is not.**

### The elements

Any value. One sequence may mix kinds — the value model does not ask for homogeneity.
Reversi has sequences of coordinates (`Opaque`), of directions (`Opaque`), and of cell
values (`Text` and `Null`).

---

## The shape the iterating nodes share

A node taking a predicate or a projection introduces a name for the element with `as`.

```jsonc
{
  "op": "seq.<name>",
  "source": <expression:Sequence>,
  "as": "<name>",        // static, optional
  "<predicate or projection>": <expression>
}
```

- The name is visible **only inside that node's predicate or projection**.
- It shadows an outer binding of the same name.
- Referring to it is [Binding](Binding.md)'s `bind.local`, sugar `@`.

**Sequence introduces bindings but owns no vocabulary for referring to one.** That falls
out of the decomposition; the scope machinery itself belongs to the evaluation context in
Abstraction.

`as` may be omitted only when the predicate or projection does not refer to the element.
Omit it and write a `@` reference anyway and you get [Binding](Binding.md)'s unbound-name
error, statically.

### Evaluation order

Elements are processed **in sequence order**. The meaning of the short-circuiting nodes
(`seq.any`, `seq.takeWhile`) depends on it. A parallel implementation is allowed, but what
it produces has to match what running in order would have produced.

---

## `seq.empty`

### Form

```jsonc
{ "op": "seq.empty" }
```

The empty sequence.

In Reversi's `flips1` this says "nothing to flip in this direction". `seq.selectMany`
(in `flips`) passes an empty sequence straight through, so the directions that do not work
out disappear on their own.

---

## `seq.of`

### Form

```jsonc
{ "op": "seq.of", "of": [ <expression>, … ] }
```

A sequence of the elements written out. An empty array is the empty sequence.

### The only way to write a sequence down

The value model gives `Sequence` no JSON literal ([§1](../value-model.md)). Without this
node **every sequence in a rule set has to come from some other plugin**. Reversi did not
notice, because all of its sequences come out of `grid.*` — which also means Sequence did
not satisfy [criterion A, independent loadability](../dsl-example-reversi.md), on its own.

Chess's knight exposed it. Its eight offsets match no `kind` of `grid.directions`; they are
simply eight directions, and the rule set has to be able to say so.

```jsonc
"knightDirs": { "op": "seq.of",
  "of": ["1,2","2,1","2,-1","1,-2","-1,-2","-2,-1","-2,1","-1,2"] }
```

### How it evaluates

The element expressions are evaluated **on each enumeration**. Re-enumerability then holds
by construction, and since every node is pure the result does not change.

---

## `seq.any`

### Form

```jsonc
{
  "op": "seq.any",
  "source": <expression:Sequence>,
  "as": "<name>",              // optional
  "predicate": <expression:Bool>   // optional
}
```

### How it evaluates

- With a `predicate`: evaluated per element, returning `true` at the first one that holds
  (short-circuit). All false gives `false`.
- Without: `true` when the sequence is non-empty.

The empty sequence is always `false`.

### Why the short-circuit matters

`hasAnyMove` may evaluate `canPlace` for all sixty-four squares, and stops at the first
playable one. Since `inputs.pass.when` is `logic.not(#hasAnyMove)`, in a position where
passing is illegal — which is most of them — it settles early. Without the short-circuit,
every pass check walks 64 squares × 8 directions of rays.

### Example (Reversi)

```jsonc
// canPlace: is there anything to flip from this square
{ "op": "seq.any",
  "source": { "op": "def.call", "def": "flips", "args": { "at": "@at" } } }

// hasAnyMove: is there a move anywhere
{ "op": "seq.any", "source": { "op": "grid.coords", "of": "$board" }, "as": "c",
  "predicate": { "op": "def.call", "def": "canPlace", "args": { "at": "@c" } } }
```

The first omits `predicate` (a non-empty test), the second has one.

---

## `seq.count`

### Form

```jsonc
{
  "op": "seq.count",
  "source": <expression:Sequence>,
  "as": "<name>",          // optional
  "where": <expression:Bool>   // optional
}
```

### How it evaluates

With a `where`, the number of elements it holds for; without, the length. The result is a
`Number`.

No short-circuit — every element is visited.

### Example (Reversi)

```jsonc
// flips1: the length of the run of opponent stones = the index of the next square on the ray
{ "op": "seq.count", "source": "@run" }

// terminal.result: how many black stones
{ "op": "seq.count", "source": { "op": "grid.cells", "of": "$board" },
  "as": "c", "where": { "op": "cmp.eq", "left": "@c", "right": "black" } }
```

The first is the neat one: because indices start at zero, the length of `run` *is* the
index of the element just past the run.

---

## `seq.elementAt`

### Form

```jsonc
{
  "op": "seq.elementAt",
  "source": <expression:Sequence>,
  "index": <expression:Number>
}
```

### How it evaluates

The element at `index`, counting from zero.

**Out of range returns `Null` rather than faulting.**

This is where the null propagation chain of [value model §3](../value-model.md) starts, and
why Reversi's `flips1` handles "the ray is opponent stones all the way to the edge" with no
boundary check.

```
seq.elementAt(out of range) → null → grid.at(null) → null → cmp.eq(null, "black") → false
```

A negative `index`, or one with a fractional part, is an evaluation fault — distinct from
out of range, because it is not "past the end of the sequence" but "not an index".

### Cost

A sequence is not necessarily random-access, so an implementation may walk `index + 1`
elements from the front. In `flips1` a ray is at most 7 long, so it does not matter.

---

## `seq.takeWhile`

### Form

```jsonc
{
  "op": "seq.takeWhile",
  "source": <expression:Sequence>,
  "as": "<name>",
  "predicate": <expression:Bool>
}
```

### How it evaluates

Evaluates `predicate` from the front and returns everything **before** the first element
where it is false. That element is not included, and nothing after it is evaluated.

False on the first element gives the empty sequence; true throughout gives the whole thing.

### Why this is the centre of Reversi

Reversi's "sandwich" test has the shape

1. one or more opponent stones in a row, and
2. one of mine immediately after.

Part 1 is `seq.takeWhile`, part 2 is `seq.elementAt` plus `cmp.eq`. The "one or more" of
part 1 is secured further out: `flips` concatenates what `flips1` returned and `canPlace`'s
`seq.any` tests it for non-emptiness.

```jsonc
{
  "op": "seq.takeWhile", "source": "@ray", "as": "c",
  "predicate": { "op": "cmp.eq",
                 "left": { "op": "grid.at", "grid": "$board", "coord": "@c" },
                 "right": "#opponent" }
}
```

Hitting an empty square (`Null`) also makes `cmp.eq` false and stops it. Because "not an
opponent stone" covers an empty square, one of mine, and the edge of the board, three
stopping conditions are written as one predicate.

---

## `seq.selectMany`

### Form

```jsonc
{
  "op": "seq.selectMany",
  "source": <expression:Sequence>,
  "as": "<name>",
  "select": <expression:Sequence>
}
```

### How it evaluates

Evaluates `select` for each element and concatenates the resulting sequences **in the
original order**.

`select` returning anything but a `Sequence` is an evaluation fault; a single value is not
wrapped automatically.

### Example (Reversi's `flips`)

```jsonc
{
  "op": "seq.selectMany",
  "source": { "op": "grid.directions", "of": "$board", "kind": "eight" },
  "as": "d",
  "select": { "op": "def.call", "def": "flips1", "args": { "at": "@at", "dir": "@d" } }
}
```

Works out what each of the eight directions flips and flattens it into one run. The
directions that do not work out returned `seq.empty`, so they vanish in the concatenation.

### Duplicates

Concatenation does not remove them. In Reversi the eight rays are disjoint so none arise,
but that is a property of the rules and not something this plugin guarantees.

---

## `seq.where` / `seq.select`

### Form

```jsonc
{ "op": "seq.where",  "source": <expression:Sequence>, "as": "<name>", "predicate": <expression:Bool> }
{ "op": "seq.select", "source": <expression:Sequence>, "as": "<name>", "select": <expression> }
```

`seq.where` keeps the elements the predicate holds for; `seq.select` projects each element.
Both preserve order.

Unused in Reversi, where `seq.count`'s `where` and `seq.any`'s `predicate` cover everything.
Provided as general vocabulary — and both earn their place in roster, which filters staff
lists and projects names out of records.

---

## `seq.concat` / `seq.take` / `seq.skip` (1.2)

```jsonc
{ "op": "seq.concat", "of": [ <expression:Sequence>, … ] }
{ "op": "seq.take", "source": <expression:Sequence>, "count": <expression:Number> }
{ "op": "seq.skip", "source": <expression:Sequence>, "count": <expression:Number> }
```

`seq.concat` was once turned down on the grounds that `seq.of` plus `seq.selectMany`
already writes it. That is true and it was the wrong conclusion: **the most ordinary
operation on a sequence being the least readable one is a reason to add a node, not to
withhold one.** Adding `type.list` is what forced the issue.

```jsonc
{ "op": "state.set", "path": "history",
  "value": { "op": "seq.concat", "of": ["$history", { "op": "seq.of", "of": ["@position"] }] } }
```

`take` and `skip` are for keeping a bounded history. A `count` beyond the end is not an
error — how long a sequence is depends on the position, and asking for ten of something
that has three is a reasonable question with a short answer. A negative `count` is an
evaluation fault, the same treatment `seq.elementAt` gives a negative index: not "out of
range" but "not a count".

---

## Decided

- **Aggregates over a sequence belong here, and are not provided yet.** The unresolved part
  was where they belong — here or in [Arithmetic](Arithmetic.md) — and the value model
  settles it: an operation that takes a sequence is a sequence operation. `math.min` taking
  a fixed list of operands is a different node that happens to share a name. What is still
  missing is a reason: `seq.count` is the only fold five rule sets have needed, and roster,
  the one that does arithmetic over collections, gets by with `math.max` over two operands.
  When `seq.sum` or `seq.minBy` is wanted, it goes here.
- **No `seq.distinct`.** Implementable — the value model defines equality — and wanted by
  nothing. The case would be a rule where `seq.selectMany` produces duplicates that matter;
  in Reversi the rays are disjoint and in chess and shogi the move generators do not
  overlap.
- **No `seq.orderBy`.** Two things were said to be waiting on it, and both dissolved. The
  first, ordering `Opaque` values, is settled against in [Comparison](Comparison.md). The
  second, making `GetValidInputs` produce a stable order, is already true by construction —
  `grid.coords` enumerates deterministically, `rec.keys` is ordinal, and candidates are the
  product of domains walked in order. Sorting numbers and text needs no new comparison and
  could be built tomorrow; nothing has asked.
- **No `seq.zip`, and no indexed iteration.** `as` binds the element and not its position.
  `flips1` reaches a position through `seq.count` instead, which is the trick that makes it
  short rather than a workaround for a missing feature — the length of the run *is* the
  index of what follows it. A rule set genuinely needing to walk two sequences in step has
  not appeared.
- **No static upper bound on a sequence's length.** The idea was to sharpen the cost
  estimate for `GetValidInputs`, and `validationLimit` already answers that question, at
  run time, exactly: it bounds the guards evaluated and reports `Truncated`. A static bound
  would also need inference to be worth anything, which
  [TypeSchema](TypeSchema.md) records is not being built yet.
