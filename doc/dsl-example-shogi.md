# The JSON DSL under test — shogi, where the prediction was wrong

[Chess §6](dsl-example-chess.md) said the remaining problem with compound parameters was
how they read to write, and that shogi would settle it.

> The tag scheme is a workaround for §3.3 rather than a design. A rule that needs a fourth
> element — a shogi move carrying both a promotion choice and a drop square — would nest
> and fall apart.

**It did not fall apart. The prediction was wrong.** And what shogi actually pressed on was
somewhere else entirely.

- Subject: [ruleset/shogi.json](../ruleset/shogi.json)
- Checked by: [test/ShogiTests.cs](../test/ShogiTests.cs)


## 1. Correctness

| Position | Depth | Expected | |
| --- | --- | --- | --- |
| opening | 1 | 30 | ○ |
| opening | 2 | 900 | ○ |
| opening | 3 | 25,470 | ○ |

On top of that, promotion, forced promotion, pieces with no legal square ahead of them,
two pawns on a file, **dropped-pawn mate**, hands going up and down, and mate detection are
each tested individually — 22 cases.

The board runs a1 to i9, with files a–i and ranks 1–9, black on the rank 1 side. That is
not the customary notation, but it is a left-right mirror of the standard opening position,
and the board's symmetry leaves perft unchanged.


## 2. Why the prediction was wrong

A shogi move is **(from, to, promote) — three elements**, exactly as in chess.

What was expected to demand a fourth was the drop. But a drop is **a separate input**, and
its two parameters **are genuinely independent**.

| Input | Parameters | Domain | Candidates |
| --- | --- | --- | --- |
| `move` | `m` (compound) | generate every legal move | **30** in the opening position (`from`/`to`/`promote` would be 81×81×2 = **13,122**) |
| `drop` | `piece`, `to` | the kinds in hand / the empty squares | 7 × empty squares (measured 7×78 = **546**) |

`piece`'s domain looks only at the hand and `to`'s looks only at the board. Neither needs
the other, so the product costs nothing, and 546 sits well inside any sensible
`validationLimit`.

**So shogi puts a case that needs a compound parameter and a case that is fine as two plain
ones side by side in one rule set.** Separate what should be separate and the compound
parameter's element count does not grow.

Forcing them into a single `play` input would give four elements, with the second slot
being a union of "origin square or piece kind". That is **the writer's choice, not the
game's requirement**.


## 3. What shogi actually demanded — the hand

**The state could not hold a collection.** That was the real pain.

> **Resolved.** This section is what produced [the collections design](collections.md) and
> became [`rec.map` and `rec.update`](https://github.com/reny-develop/Rulealize.Plugin.Record/blob/main/doc/specification.md). What follows describes it as
> found; today's `shogi.json` has one `hand` field, and went from 925 lines to 880.

```jsonc
"bP": { "op": "type.int", "min": 0 },
"bL": { "op": "type.int", "min": 0 },
// … seven kinds for black, seven for white, fourteen fields
```

`state.schema` was a flat map of scalars, so the hand — a multiset — had to be spread over
**fourteen counters, piece kind × side**. Three consequences.

### 3.1 Fourteen lines of effects, in two inputs

`state.set`'s `path` is a literal ([the State plugin](https://github.com/reny-develop/Rulealize.Plugin.State/blob/main/doc/specification.md)). "Increment the
counter for kind K" is unsayable, so all fourteen get enumerated.

```jsonc
{ "op": "state.set", "path": "bP",
  "value": { "op": "math.add", "of": ["$bP",
    { "op": "def.call", "def": "gained", "args": { "m": "@m", "colour": "black", "kind": "P" } }] } },
{ "op": "state.set", "path": "bL", … },   // thirteen more
```

Both `move` and `drop` move pieces in and out of a hand, so **twenty-eight blocks of the
same shape** line up. Factoring out `gained` and `spent` does not help with the part that
reads `$bP`.

### 3.2 Reading takes two levels of match

```jsonc
"held": { "params": ["colour", "kind"], "body": {
  "op": "branch.match", "value": "@colour", "cases": {
    "black": { "op": "branch.match", "value": "@kind",
               "cases": { "P": "$bP", "L": "$bL", … } },
    "white": { … } } } }
```

Fourteen static state references, selected by `branch.match` because a path cannot be
computed. **The ban on dynamic paths** — the price of the three benefits
[the State plugin](https://github.com/reny-develop/Rulealize.Plugin.State/blob/main/doc/specification.md) lists — costs most right here.

### 3.3 The schema cannot state the invariant

"The pawns on the board and in both hands total eighteen" is unwritable in this shape.
There are fourteen independent `type.int`s and nothing else.


## 4. Dropped-pawn mate — the complete version of §3.1 went through

[Chess §3.1](dsl-example-chess.md) said it got away with the after-position question
"because a board is one value in one field", and that a rule depending on the whole state
after a transition could not be written. Shogi's dropped-pawn mate sits right on that
boundary, and **it went through.**

```
after dropping the pawn: the opponent is in check, and the opponent has no legal move
```

Two things made it work.

1. **Parameterizing the whole move generator over `(board, colour)`** —
   `targetsFrom(b, colour, f)`, `movesFor(b, colour)`, `safeAfterMove(b, colour, m)`,
   `hasBoardMove(b, colour)`. Chess's `attacked(b, sq, by)` widened to cover everything,
   and it is **a discipline rather than a constraint**.
2. **A one-ply search into the reply.** Splitting `hasBoardMove` from `hasAnyPlay` avoids
   recursion (`hasAnyPlay` → `dropLegal` → `dropMate` → `hasBoardMove`).

**It is exact, not an approximation.** A dropped pawn gives check from the square directly
in front of the king, so there is no gap between them and interposing is impossible in
principle. "The opponent has no **board** move" is therefore equivalent to "the opponent
has no legal move", and that is what licenses cutting drops out of the recursion in
`dropLegal`.

But the condition for it working has not changed. **The hand does not affect this rule**,
so replacing the board alone was enough. A rule where the turn, the board and the hand all
matter at once is still unwritable.


## 5. By-products

- **`attacked` is implemented differently from chess's.** Chess works backwards along rays.
  Shogi has fourteen kinds in six patterns, many of them asymmetric, and a reverse table
  would be "the move generator, implemented a second time". The shogi version generates
  forwards from all 81 squares and matches. It is slower, and generation and attack cannot
  drift apart by construction. **The DSL made that choice the natural one**, because
  parameterizing the definitions was the only way to write it at all.
- **`ValidInput.Arguments[key]` throws on a key that is not there.** With more than one
  input the results of `GetValidInputs` are mixed together, so asking a `move` result for
  `Arguments["piece"]` fails. A test hit it. Small, and real — and see below.


## 6. The conclusion about making domains parameter-dependent

**The evidence is in.**

| | Chess | Shogi |
| --- | --- | --- |
| as two parameters | 4,096 | 13,122 |
| as a compound | 20 | 30 |
| elements in a move | 3 | 3 |

**The element count did not grow.** So the argument "it falls apart at four or five
elements, therefore the change is needed" is gone. What is left is the weaker half of the
original list.

- the return value is `{"m": "e2|e4|-"}` and loses its structure
- `index: 0/1/2` have no names
- a rejection message cannot name the parameter that was wrong

These are real, and with **two games both fitting in three elements** there is no urgency.

Worth recording that **[settling chess §3.2](dsl-example-chess.md) made the change
cheaper.** `BindArguments` already walks the parameters in declaration order, matching each
against its domain one at a time. To make domains parameter-dependent, the applying side
would only need to **evaluate the next domain in a context with the bindings so far**
(`EvaluationContext.Bind` returns a new context, so it is a few lines). The remaining work
concentrates in `Collect` and `Walk` on the search side.

### Reprioritizing

**The hole the hand exposed is bigger.** `type.list` and `type.record` — or a multiset
schema node — would

- delete shogi's fourteen fields and twenty-eight effects
- make chess's threefold repetition possible
- be the precondition for non-game uses: queues, histories, line items

Parameter-dependent domains improve only the shape of the return value. **The same effort
spent on the expressiveness of the state goes further.**


## 7. What is still open, and what has closed

- ~~**`type.list` / `type.record` / a multiset**~~ — resolved, and this section is what
  caused it. See [the collections design](collections.md).
- ~~**Referring to `Arguments` safely**~~ — withdrawn. §5 recorded this as a small real
  corner, and [roster §4.5](dsl-example-roster.md) reached the opposite conclusion on
  better grounds: `ImmutableDictionary` throwing `KeyNotFoundException` for a key it does
  not have is a dictionary behaving correctly. A rule set with several inputs returns
  mixed results and the caller filters on `.Input` first, which is an ordinary
  responsibility rather than a rough edge. Roster's conclusion is the one that stands.
- ~~**Repetition (sennichite)**~~ — was recorded as depending on whether the state could
  hold a history. It can, since [`type.list`](https://github.com/reny-develop/Rulealize.Plugin.TypeSchema/blob/main/doc/specification.md). It is not written into
  `shogi.json`, for the same reason chess does not write threefold repetition: it is a
  long, well-understood rule that would not test anything the rule set does not already
  test. The blocker is gone; only the writing is left.
- **Invariants across state fields** — §3.3, and still unwritable. Becoming a record did
  not change it: `rec.map` constrains one field, and there is no predicate language for
  `state.schema`. [Record](https://github.com/reny-develop/Rulealize.Plugin.Record/blob/main/doc/specification.md) records what it would take, and that both
  shogi and roster want it while neither is blocked by it.
- **Asking about the whole state after a transition** — the remainder of §4, and the one
  place shogi came within a step of being blocked. Tracked in
  [chess §7](dsl-example-chess.md), where the shape of an answer is sketched.
