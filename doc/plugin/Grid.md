# Rulealize.Plugin.Grid

| | |
| --- | --- |
| Identifier | `Rulealize.Plugin.Grid` |
| Namespace | `grid` |
| Version | `1.1.0` |
| Reserved prefix | none |
| Depends on | [the value model](../value-model.md), and nothing else |

Two-dimensional boards, coordinates, directions, and rays.

**Not one item of Reversi-specific vocabulary.** "Sandwich" and "flip" are not concepts
Grid has; the rule set builds them out of `grid.ray` and `seq.takeWhile`. Whether that
boundary holds is the measure of whether the plugin design is sound.

The same Grid should describe gomoku (`grid.ray` plus a run length), draughts, and the game
of life. [Chess was written with the 1.1 additions](../dsl-example-chess.md) — promotion
and captured pieces needed no dedicated plugin, only a way to build a new board as a value.

**The only plugin that provides all three kinds of node.**

## Nodes

| Node | Kind | Used in Reversi |
| --- | --- | --- |
| `grid.board` | schema | ○ `state.schema.board` |
| `grid.square` | schema | — (added in 1.1) |
| `grid.at` | expression | ○ `flips1`, `canPlace`, `terminal.when` |
| `grid.coords` | expression | ○ `hasAnyMove`, `inputs.place.params`, `terminal.when` |
| `grid.cells` | expression | ○ `terminal.result` |
| `grid.ray` | expression | ○ `flips1` |
| `grid.directions` | expression | ○ `flips` |
| `grid.with` | expression | — (added in 1.1) |
| `grid.withMany` | expression | — (added in 1.1) |
| `grid.set` | effect | ○ `inputs.place` |
| `grid.setMany` | effect | ○ `inputs.place` |

## What 1.1 added

### `grid.with` / `grid.withMany`

```jsonc
{ "op": "grid.with",     "grid": <expression:board>, "coord": <expression:coord>,     "value": <expression> }
{ "op": "grid.withMany", "grid": <expression:board>, "coords": <expression:Sequence>, "value": <expression> }
```

The **expression form** of `grid.set` / `grid.setMany`. Nothing is written; a board goes in
and a different board comes out. Coordinates are handled as strictly as in the effect
versions — off the board or `null` is an evaluation fault.

**Needed because `when` cannot see the position the move leads to.** A guard is evaluated
against the state as the input found it, and `effects` run only after the guard passes.
Reversi's legality is a property of the position in front of you, so that order is fine.
Chess's legality is "and your own king is not then capturable", which cannot be asked
without building the later position as a value.

That it can be answered at all is because **a board is one value in one field**. A rule
that depends on the whole state after a transition — several fields at once — still cannot
be written, and that is a question for the runtime rather than for this plugin.

### `grid.square`

```jsonc
{ "op": "grid.square", "width": 8, "height": 8, "coord": "algebraic", "nullable": true }
```

**The schema for a state field holding a single coordinate.** Its JSON form is the board's
notation (`"e3"`).

Without it the state cannot hold a coordinate. Putting one in a `type.string` lets
`grid.at` read it — [value model §1.1](../value-model.md) requires the text form to be
accepted — but it can no longer be compared with `cmp.eq` against a coordinate that
`grid.coords` produced, because `Text` and `Opaque` are different kinds and different kinds
are never equal.

Chess needs exactly one such field: the square a pawn just skipped over.

## The opaque types introduced

| Type tag | Meaning | Canonical text |
| --- | --- | --- |
| `grid/coord` | a position on the board | the board's `coord` notation, e.g. `"d3"` |
| `grid/direction` | a direction vector | `"<dx>,<dy>"`, e.g. `"1,-1"` |

Per [value model §1.1](../value-model.md) these convert to and from text. For coordinates
that is what makes `GetValidInputs`'s output (`{ "at": "d3" }`) and an input document's
arguments work at all.

---

## `grid.board`

The schema of a board.

### Form

```jsonc
{
  "op": "grid.board",
  "width": <integer>,        // static
  "height": <integer>,       // static
  "coord": "<notation>",     // static, "index" if omitted
  "cell": <schema node>      // static
}
```

**A schema node.** It appears only inside `state.schema`.

| Key | |
| --- | --- |
| `width` / `height` | the dimensions, at least 1 |
| `coord` | the canonical text form of a coordinate; below |
| `cell` | the type of each square; any schema node |

### `coord` notations

| Value | Form | Example (top-left / bottom-right of 8×8) |
| --- | --- | --- |
| `"algebraic"` | a lower-case letter for the file, a 1-based number for the rank | `"a1"` / `"h8"` |
| `"index"` | `"<x>,<y>"`, 0-based | `"0,0"` / `"7,7"` |

`"algebraic"` is available only when `width` is 26 or less; otherwise a static error.

For `"algebraic"` the origin is **`a1` at bottom-left with y increasing upward**, as chess
and Reversi have always had it. `"index"` puts **`0,0` at top-left with y increasing
downward**. Two notations disagreeing about which way is up invites confusion, and
following each notation's own convention was judged to invite less.

### `cell` and the empty square

```jsonc
"cell": { "op": "type.enum", "values": ["black", "white"], "nullable": true }
```

Grid does not reference [TypeSchema](TypeSchema.md). What arrives at `cell` is "some schema
node", and building it is the core's business.

**Grid does not require `nullable`, but since `grid.at` answers `Null` off the board, a
board whose `cell` is not nullable can tell "off the board" from a real cell value** —
because a real one is then never `Null`. Reversi makes its cell nullable and deliberately
conflates the two; see `grid.at`.

### The JSON form

A board is serialized as a **sparse object**: the keys are coordinates in canonical text,
the values are cell values.

```jsonc
"board": { "d4": "white", "e4": "black", "d5": "black", "e5": "white" }
```

`Null` squares are left out entirely. Shorter than writing all 64, and the diffs are
readable. **This form is Grid's own business and [State](State.md) is not involved** — to
switch to a dense array, change Grid's implementation and nothing else.

The in-memory representation need not be sparse. Holding an 8×8 array and flattening to
sparse on serialization is the natural thing.

---

## What a coordinate may be written as

Every node taking a coordinate — `grid.at`, `grid.set`, `grid.ray` and the rest — accepts
two things.

| Kind | Example | Where it comes from |
| --- | --- | --- |
| `Opaque(grid/coord)` | — | the output of `grid.coords` or `grid.ray` |
| `Text` | `"d3"` | an input document's `args`, read in the board's notation |

Text that does not parse in the board's notation is an evaluation fault. **Text naming a
square off the board parses successfully** — what happens to an off-board coordinate is
each node's own business, below.

What `Null` does is also each node's business.

Accepting both forms is why the domain of `inputs.place.params.at` can produce a sequence
of opaque values while an input document writes `"at": "d3"`.

---

## `grid.at`

Reads a square.

### Form

```jsonc
{ "op": "grid.at", "grid": <expression:board>, "coord": <expression:coord> }
```

### How it evaluates

| `coord` | Result |
| --- | --- |
| on the board | that square's value (`Null` when empty) |
| **off the board** | **`Null`** |
| **`Null`** | **`Null`** |

### Why off-board and null coordinates are allowed

The most important judgement in Grid's design.

Reversi's `flips1` reads the position one past the end of a run.

```jsonc
"coord": { "op": "seq.elementAt", "source": "@ray",
           "index": { "op": "seq.count", "source": "@run" } }
```

When the ray is opponent stones all the way to the edge, `seq.elementAt` goes out of range
and returns `Null`. Make `grid.at` fault on a `Null` coordinate and whoever writes the rule
set has to add an explicit "compare the ray's length with the run's length" check.

**Off the board, a `Null` coordinate and an empty square all collapse into `Null`, which
makes them one question: "there is no stone of mine there."** That meshing with
[Comparison](Comparison.md)'s null-safe `cmp.eq` is what keeps `flips1`'s condition to a
single level.

The price is that **a mistyped coordinate quietly becomes `Null`**. Coordinates are almost
never written as literals — nearly all of them come from `grid.coords` or `grid.ray` — so
the exposure was judged small, and three board games later nothing has been traced to it.

---

## `grid.coords`

Every coordinate on the board.

### Form

```jsonc
{ "op": "grid.coords", "of": <expression:board> }
```

### How it evaluates

Every coordinate as a sequence of `Opaque(grid/coord)`, of length `width × height`.

**The enumeration order has to be deterministic.** It is row-major from the top left — in
`"index"` notation `(0,0), (1,0), …` — so that the order `GetValidInputs` reports does not
change between runs.

### Example (Reversi)

```jsonc
// inputs.place.params — the domain candidates come from
"at": { "domain": { "op": "grid.coords", "of": "$board" } }

// hasAnyMove — scan every square
{ "op": "seq.any", "source": { "op": "grid.coords", "of": "$board" }, "as": "c",
  "predicate": { "op": "def.call", "def": "canPlace", "args": { "at": "@c" } } }
```

Its use as a domain is where `GetValidInputs` starts. On 8×8 that is 64 candidates, well
inside any sensible `validationLimit`.

---

## `grid.cells`

Every cell value on the board.

### Form

```jsonc
{ "op": "grid.cells", "of": <expression:board> }
```

Values rather than coordinates. Same order as `grid.coords`.

### Example (Reversi's `terminal.result`)

```jsonc
{ "op": "seq.count", "source": { "op": "grid.cells", "of": "$board" },
  "as": "c", "where": { "op": "cmp.eq", "left": "@c", "right": "black" } }
```

`grid.coords` plus `grid.at` writes the same thing; this is shorter when only the values
are wanted.

---

## `grid.ray`

The coordinates running from a square in a direction.

### Form

```jsonc
{
  "op": "grid.ray",
  "grid": <expression:board>,
  "from": <expression:coord>,
  "dir": <expression:direction>,
  "length": <expression:Number>   // optional
}
```

### How it evaluates

The coordinates reached by stepping from `from` in direction `dir`.

- **`from` itself is not included**; it starts one step out.
- It stops on leaving the board.
- `length` cuts it off after that many steps.

`from` off the board or `Null` gives the empty sequence rather than a fault.

The elements are `Opaque(grid/coord)`.

### Why `from` is excluded

Include it and Reversi's `flips1` has to skip the first element every time. "Look outward
from the neighbour" is what a ray is mostly for, and excluding it is shorter. A rule set
wanting the origin already has `from`.

### Example (Reversi's `flips1`)

```jsonc
{ "op": "grid.ray", "grid": "$board", "from": "@at", "dir": "@dir" }
```

On an 8×8 board this is at most 7 long. It is enumerated twice, by `seq.takeWhile` and
`seq.elementAt`, which is what makes [Sequence](Sequence.md)'s re-enumerability a
requirement rather than a nicety.

---

## `grid.directions`

A set of directions.

### Form

```jsonc
{
  "op": "grid.directions",
  "of": <expression:board>,
  "kind": "<kind>"       // static
}
```

### `kind`

| Value | | Count |
| --- | --- | --- |
| `"orthogonal"` | up, down, left, right | 4 |
| `"diagonal"` | the four diagonals | 4 |
| `"eight"` | all of the above | 8 |

The elements are `Opaque(grid/direction)`, in a deterministic order (`"eight"` runs
row-major from `(-1,-1)`).

It takes `of` so the directions it returns agree with that board's coordinate system.

### Writing a single direction

A direction has a canonical text form, so **a rule set writes one as a string literal**:
`"0,-1"` is a direction wherever a direction is expected, exactly as `"d3"` is a
coordinate. `grid.directions` is a convenience for the common sets, not the only way to
obtain one.

Shogi's forward direction is a definition that picks the literal by colour:

```jsonc
"fwd": { "params": ["colour"], "body":
  { "op": "branch.match", "value": "@colour", "cases": { "black": "0,-1", "white": "0,1" } } }
```

---

## `grid.set`

Writes one square.

### Form

```jsonc
{
  "op": "grid.set",
  "target": <expression:board>,
  "coord": <expression:coord>,
  "value": <expression>
}
```

**An effect node.** It appears only as an element of `inputs.*.effects`.

### How it applies

1. `target`, `coord` and `value` are evaluated **against the snapshot**.
2. `value` is written to `coord` of that board in the draft.

`target` is the expression naming the board to write to, in practice a `state.get` such as
`$board`. It is matched against the State plugin's path to work out where in the draft the
write lands.

### Off the board and `Null` coordinates

**Unlike `grid.at`, an evaluation fault.**

Lenient reads and strict writes is deliberate. A `Null` from a read is a meaningful answer
— "there is nothing there" — while the only thing a write off the board could meaningfully
do is nothing at all, and that silently swallows a mistake in the rules.

Reversi's `place` has already established through `when` (`canPlace`) that the square is on
the board and empty, so it never runs into the strictness.

---

## `grid.setMany`

Writes one value to several squares.

### Form

```jsonc
{
  "op": "grid.setMany",
  "target": <expression:board>,
  "coords": <expression:Sequence>,
  "value": <expression>
}
```

**An effect node.**

### How it applies

Enumerates `coords` and writes `value` to each. `value` is **evaluated once** and the same
value goes to every coordinate; it is not re-evaluated per square.

An empty sequence does nothing. Off-board and `Null` coordinates fault, as in `grid.set`.

### Example (Reversi's `inputs.place.effects`)

```jsonc
{ "op": "grid.setMany", "target": "$board",
  "coords": { "op": "def.call", "def": "flips", "args": { "at": "@at" } },
  "value": "#me" }
```

Turns everything captured to my colour in one go. `coords` is evaluated against the
snapshot, so it is unaffected by the `grid.set` just before it that put the new stone down
— which is [value model §5](../value-model.md)'s snapshot semantics doing visible work.

---

## What Reversi did not need

Things Grid deliberately lacks, or has not been given.

- **Adjacency (`grid.neighbors`)** — `grid.ray` with `length: 1` covers it, and shogi's
  step pieces are written exactly that way.
- **Regions and pattern matching** — gomoku's "five in a row" is expected to come out of
  `grid.ray` plus `seq.takeWhile`.
- **Rotating and reflecting a board** — useful for exploiting symmetry in a search, and not
  needed to state rules.
- **Relating two boards** — a state with two boards is two fields.

---

## Decided

- **No node for building a single direction, and none was ever needed.** This was recorded
  as something chess and shogi would make unavoidable, with a sketch of
  `{ "op": "grid.direction", "dx": 0, "dy": 1 }`. **The premise was simply wrong**: a
  direction has a canonical text form, so `"0,-1"` already is one. The claim that
  directions could only be got out of `grid.directions` was false when it was written.
- **No turn-relative directions either**, for the same reason. "Forward from my side"
  changes with the colour to move, and Grid knowing whose turn it is would be overreach —
  which was the original objection, and it stands. What was missing was noticing that the
  rule set can do it in three lines with `branch.match`, which is what shogi's `fwd` above
  is.
- **No `grid.atStrict`.** The lenient read has a real cost, a mistyped coordinate becoming
  `Null`, and after three board games nothing has been traced to it. Coordinates come from
  `grid.coords` and `grid.ray`, not from typing.
- **No `grid.setEach`** for writing a different value per coordinate. Chess's castling
  moves two pieces to two squares and writes two `grid.set` effects, which reads better
  than one node taking parallel sequences would have.
- **Non-rectangular boards belong in a different plugin**, if they ever arrive —
  `Rulealize.Plugin.HexGrid` or the like. Every node here is built on `width` and `height`,
  and generalizing them would complicate the common case to serve a case nobody has.
- **Coordinates get no ordering.** Settled in [Comparison](Comparison.md): the motivation
  was a deterministic output order, which `grid.coords` already provides by enumerating
  deterministically.
- **No `grid.coordsWhere` for narrowing a domain.** This was the one expected to become
  unavoidable on larger boards or with multi-parameter inputs, and both cases then arrived
  and were solved a different way. Chess would have had 4,096 candidates and has 20; shogi
  would have had 13,122 and has 30. What did it was a **compound parameter** whose domain
  computes destinations from origins ([Tuple](Tuple.md)), which narrows far more than a
  filtered coordinate list could — a predicate over squares cannot express "the squares
  this piece can reach from there". The remaining case for it, saving a `seq.where` around
  a `grid.coords`, is spelling.
