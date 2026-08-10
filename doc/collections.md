# Putting collections in the state — the design, and what came of it

The answer to the hole [shogi](dsl-example-shogi.md) opened up: `state.schema` was a flat
map of scalars, with nowhere to put a multiset or a sequence.

**Built.** [`type.list`](plugin/TypeSchema.md) (TypeSchema 1.1),
[`Rulealize.Plugin.Record`](plugin/Record.md), [Sequence 1.2](plugin/Sequence.md), and
`SchemaNode.Normalize` (Abstraction 0.2.0). Shogi was rewritten and its perft numbers did
not move.

- Assumes: [the value model](value-model.md), [the State plugin](plugin/State.md)


## 1. What it was costing

`shogi.json` was 925 lines, of which **72 — about 8% — were boilerplate for the hand**.

| | Lines |
| --- | --- |
| the fourteen fields `bP` … `wR` in `state.schema` | 14 |
| the hand-updating effects of `move` and `drop` (14 × 2) | 28 |
| `held`'s two-level `branch.match`, and `handKinds` | 30 |

And none of it could be trimmed. `state.set`'s `path` is a literal, so "increment the
counter for piece kind `@kind`" is unsayable and all fourteen have to be written out.

Chess's threefold repetition, shogi's perpetual check, and the histories, queues and line
items of anything that is not a game were all on the far side of the same wall.

## 2. The principle — paths stay literal

**`state.set` is not going to accept `"hand.P"`.** That gives up the three things
[the State plugin](plugin/State.md) names: checking every path up front, reading which
field an input writes off the document, and not carrying schema validation into run time.

Instead, **follow the precedent `grid.board` already set.**

> The inside of a board is not reachable by path. To the state a board is one opaque value,
> and reading and writing squares belongs to `grid.at` and `grid.set`.

Collections take the same seam. **A path names the whole field; the inside is touched with
the vocabulary of the plugin that defined it.**

## 3. Lists and records are not symmetric

The temptation is to build two things of the same shape. The value model says otherwise.

| | Kind of value | Existing vocabulary | What is needed |
| --- | --- | --- | --- |
| List | `Sequence` | **all of `seq.*` already works** | a schema node, and nothing else |
| Record | `Record` | **none** | a schema, a way to read, a way to write |

Hold a sequence as a `Sequence` and `seq.count`, `seq.any`, `seq.where` and
`seq.elementAt` work on day one. Building a dedicated reading vocabulary would only mean
owning two nodes that mean the same thing.

Therefore:

- **`type.list` goes in [TypeSchema](plugin/TypeSchema.md).** It is a pure schema node with
  no expressions, which does not violate TypeSchema's character — the line that plugin
  holds is "no expression nodes", not "scalars only". Only its one-line description needed
  rewording.
- **Records become a new plugin, `Rulealize.Plugin.Record`, namespace `rec`.** It provides
  all three kinds of node, the same posture as Grid.

The asymmetry looks untidy and **is an asymmetry with a reason**, which also serves the
discoverability `requires` is for ([the criteria](dsl-example-reversi.md)): a rule set that
only needs a sequence does not have to require `Rulealize.Plugin.Record`.

## 4. `type.list`

### Form

```jsonc
{
  "op": "type.list",
  "element": <schema node>,      // required
  "minLength": <integer>,        // optional, static
  "maxLength": <integer>         // optional, static
}
```

### The value

A `Sequence` whose every element satisfies `element`.

Because it is a `Sequence`, reading it needs no new vocabulary.

```jsonc
{ "op": "seq.count", "source": "$history" }
{ "op": "seq.any", "source": "$queue", "as": "j",
  "predicate": { "op": "cmp.eq", "left": "@j", "right": "@target" } }
```

### The JSON form

An array. The `element` schema node decides the shape of each entry.

```jsonc
"history": [ { "board": { … }, "turn": "white" }, … ]
```

### Writing

Hand `state.set` an expression returning a `Sequence`. There is no dedicated effect node.

```jsonc
{ "op": "state.set", "path": "history",
  "value": { "op": "seq.concat", "of": ["$history", { "op": "seq.of", "of": ["@position"] }] } }
```

### `maxLength` is not decoration

`GetValidInputs` walks a sequence end to end, and the state is serialized on every
transition. An unbounded history grows the document with the square of the move count.

Chess's threefold repetition **has a bound in principle.** A repetition can only be
established since the last irreversible move — a capture or a pawn move — and the
fifty-move rule caps that at a hundred plies. `maxLength: 100` is the rule itself, not a
compromise.

## 5. `Rulealize.Plugin.Record`

| | |
| --- | --- |
| Identifier | `Rulealize.Plugin.Record` |
| Namespace | `rec` |
| Reserved prefix | none |

### 5.1 Two schema nodes

Built on a **closed key set**. Not an open map, on the same judgement that put `type.enum`
ahead of `type.string` plus a regular expression: **what is declared can be checked.**

```jsonc
// a record of heterogeneous fields
{ "op": "rec.of", "fields": { "<name>": <schema>, … } }

// a record of like values, looked up by key
{ "op": "rec.map", "keys": ["<key>", …], "value": <schema> }
```

`rec.map` is separate because **it says something different**. "Every value is of the same
type" cannot be expressed by `rec.of`, and shogi's hand is precisely that.

```jsonc
// fourteen fields become four lines
"hand": {
  "op": "rec.map", "keys": ["black", "white"],
  "value": { "op": "rec.map", "keys": ["P", "L", "N", "S", "G", "B", "R"],
             "value": { "op": "type.int", "min": 0 } }
}
```

Keys are serialized in declaration order, so that the output is deterministic — the rule
`state.schema` already follows.

### 5.2 Expression nodes

```jsonc
{ "op": "rec.at",   "record": <expression>, "key": <expression:Text> }
{ "op": "rec.has",  "record": <expression>, "key": <expression:Text> }
{ "op": "rec.with", "record": <expression>, "key": <expression:Text>, "value": <expression> }
{ "op": "rec.keys", "of": <expression> }
```

> **`rec.has` was added during implementation.** It was not in the design. Having decided
> that `rec.at` faults on an absent key, there was then **no safe way to ask** — and shogi,
> putting a captured piece into a hand, has to establish first that the piece is a kind
> that can be held. Without it the rule set carries a list of the seven kinds alongside the
> schema. Not visible until it was used.

- **`rec.at`** — returns the value. `Null` when `record` is `Null`, matching `tuple.at`.
  **An undeclared key is an evaluation fault**, and this is where it parts company with
  `grid.at`. A board legitimately has squares that do not exist; a closed record's absent
  key is only ever the writer's mistake, and no rule depends on it.
- **`rec.with`** — a new record with one key replaced. Pure, the counterpart of `grid.with`,
  and needed by [rules that ask about the position after a move](dsl-example-chess.md).
  Writing an undeclared key is a fault, which is what makes **a record satisfy its schema by
  construction.**
- **`rec.keys`** — the keys as a `Sequence` of `Text`. The way in to iterating.

```jsonc
// shogi's handKinds collapses to one expression
{ "op": "seq.where", "source": { "op": "rec.keys", "of": "#myHand" }, "as": "k",
  "predicate": { "op": "cmp.gt",
                 "left": { "op": "rec.at", "record": "#myHand", "key": "@k" }, "right": 0 } }
```

### 5.3 Effect nodes

```jsonc
{ "op": "rec.set",    "target": "$<field>", "key": <expression>, "value": <expression> }
{ "op": "rec.update", "target": "$<field>", "key": <expression>, "as": "<name>", "value": <expression> }
```

`target` resolves as `grid.set`'s does — the path comes off an `IStateLocation`, and that
the schema there is a record is checked at build time.

**The current value is read from the draft**, the same reason `grid.set` does it: several
effects of one input have to pile up. A rule where one move both adds to and removes from a
hand — shogi has none, but they are ordinary enough — requires it.

`rec.update` binds the current value to `as`. The keyed version of `state.update`.

```jsonc
// twenty-eight blocks become one
{ "op": "rec.update", "target": "$hand", "key": "#me", "as": "h",
  "value": {
    "op": "branch.if",
    "cond": { "op": "def.call", "def": "isCapture", "args": { "m": "@m" } },
    "then": { "op": "rec.with", "record": "@h",
              "key": { "op": "def.call", "def": "captured", "args": { "m": "@m" } },
              "value": { "op": "math.add", "of": [
                  { "op": "rec.at", "record": "@h",
                    "key": { "op": "def.call", "def": "captured", "args": { "m": "@m" } } }, 1] } },
    "else": "@h" } }
```

### 5.4 Nesting

The `element` of a `type.list` and the fields of `rec.of` and `rec.map` are all arbitrary
schema nodes, so a record of records, a sequence of records, and a sequence of boards are
all directly writable. Reaching deep to write is `rec.update` composed with `rec.with` —
the example above goes two levels, `$hand` to the colour to the piece kind.

## 6. What actually shrank

`shogi.json` went **925 lines → 880**.

| | Before | After |
| --- | --- | --- |
| schema | 14 fields | 7 lines of `rec.map` |
| update effects | **28 blocks** | **two `rec.update`** |
| `gained` / `spent` | 34 lines | gone |
| `held` | 17 lines of two-level `branch.match` | 8 lines of two `rec.at` |
| `handKinds` | seven kinds written into a `seq.of` | `rec.keys` |

**The original estimate of "72 lines down to about 12" was far too optimistic.** Only 45
lines went. Line count does misrepresent it somewhat — the 28 that vanished were each
close to 200 characters — but even allowing for that, the estimate was wrong.

The quality of the reduction matters more. Thanks to `rec.has`, **the list of seven piece
kinds disappeared from the rule set.** `handKinds` used to spell them out separately from
the schema, which was a disagreement waiting to happen. It now asks the record for its own
keys.

Chess's threefold repetition is within reach too. Stack "board + turn + castling rights +
en passant" as a `rec.of` inside a `type.list` and comparison is **structural equality of
values**, directly (`BoardValue`'s equality is already defined over geometry and squares).
No node for fingerprinting a position is needed.

## 7. What else this pulled in

### 7.1 Sequence 1.2 — built

| Node | For |
| --- | --- |
| `seq.concat` | appending to a history. Writable as `seq.of` plus `selectMany`, which made the most ordinary operation the least readable |
| `seq.take` / `seq.skip` | trimming a history to the last N, which is how a `maxLength` is kept |

### 7.2 `SchemaNode.Normalize` (Abstraction 0.2.0) — built

```csharp
public virtual RuleValue Normalize(RuleValue value) => value;
```

`StateDraft.Commit` calls it per field. It had two aims.

1. **Settling a lazy sequence.** What gets written into a `type.list` may be lazy, and a
   lazy sequence stored in the state holds on to the evaluation context it was built from
   — the previous snapshot. Serializing on every transition meant no harm came of it in
   practice, but **it depended on a property nobody had written down.**
2. **Somewhere for a constraint like `maxLength` to be checked on the way in.** At the time,
   schema validation ran only when a document was read, so a broken state went back to the
   caller and surfaced on the next read.

The second aim is now met, and not by `Normalize`: a transition **settles each written
field and then checks it against its schema**. `Normalize` does what its name says and
nothing more, and rejecting is `Validate`'s job — see
[TypeSchema](plugin/TypeSchema.md) for why the objection to running it was mistaken.

The default implementation is the identity, so **no existing plugin needed changing**.
`StateDraft.Commit` calls it only for fields that were written; a field nobody touched came
out of a document or an earlier commit and has settled once already.

## 8. What was decided, and what was learned

### Decided

- Paths stay literal; the inside belongs to the plugin (following `grid.board`'s seam)
- `type.list` holds a `Sequence` and lives in TypeSchema, with no reading vocabulary
- Records have a closed key set, and `rec.with` refusing an undeclared key makes them
  **satisfy their schema by construction**
- An undeclared key read by `rec.at` is an evaluation fault (`grid.at`'s leniency is not
  inherited)
- Effect nodes read from the draft, as `grid.set` does

### Learned by building it

- **`rec.has` is necessary** (§5.2), as a consequence of making `rec.at` strict. Missed at
  design time.
- **`rec.keys` is in ordinal order.** A record is a value, and it may have been built from a
  literal that never saw a schema, so declaration order does not always exist.
- **The estimate was wrong** (§6). 72 → 12 was the guess; 925 → 880 was the outcome.

### Since settled

- **An open key set is not wanted.** The case for it was copying external data, and when
  that case arrived — roster, assigning real people to real shifts — it wanted a list of
  records rather than an open record. **A key set fixed by the instance is not a key set,
  it is a list**; [roster §4.1](dsl-example-roster.md) works the distinction out.
- **Records cannot be input arguments**, which is correct: `Record` has no canonical text,
  so a domain returning one fails when the argument is resolved. A compound input uses
  [`tuple`](plugin/Tuple.md), and [Tuple](plugin/Tuple.md) records why the division between
  the two compounds is a clean one rather than a gap.
- **Comparing sequence elements is as expensive as it looked, and bounded.** `seq.any` over
  a sequence of positions compares boards repeatedly. What keeps it in hand is the same
  `maxLength` argued for in §4: the histories that make sense to keep are the ones a rule
  bounds, and a hundred positions is not a problem. A rule set that wants an unbounded
  history has a bigger problem than the comparison cost.
- **`rec.of` is where inference would start**, and inference is not being built yet;
  [TypeSchema](plugin/TypeSchema.md) records the condition for starting.
