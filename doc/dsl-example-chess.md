# The JSON DSL under test — what writing chess showed

[Reversi](dsl-example-reversi.md) established that a game can be written in general
vocabulary alone. Chess was written to ask something else.

> **Can an input whose destination depends on its origin be written without changing the
> core?**

The domains of `inputs.*.params` are evaluated independently of one another
([`RuleContext.Collect`](../src/RuleContext.cs)). Candidates are the product of the
domains, and no domain may refer to another parameter's value. Write a chess move as
`from` and `to` and **every position has 64 × 64 = 4,096 candidates**, against 20 to 40
real legal moves.

- Subject: [ruleset/chess.json](../ruleset/chess.json)
- Checked by: [test/ChessTests.cs](../test/ChessTests.cs)
- Conclusion: **it can, and the core did not change.** What was missing was vocabulary, all
  of which could be specified without reference to any particular rule set.


## 1. The results

### 1.1 Correctness

Perft — the leaf count of the legal move tree — has published values, so self-consistency
does not get you a pass.

| Position | Depth | Expected | |
| --- | --- | --- | --- |
| opening | 1 | 20 | ○ |
| opening | 2 | 400 | ○ |
| opening | 3 | 8,902 | ○ |
| opening | 4 | 197,281 | ○ |
| Kiwipete | 1 | 48 | ○ |
| Kiwipete | 2 | 2,039 | ○ |
| Kiwipete | 3 | 97,862 | ○ |

Kiwipete (`r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -`) is used in
perft collections as the position where nearly every easily-mistaken rule is live at once:
castling on both wings for both sides, pins, and a pawn capture that opens a line.

So promotion, en passant, castling, pins, getting out of check and stalemate are all
correct. **Chess can be written in the JSON DSL.**

### 1.2 Candidate counts

| Position | As two parameters | As a compound | Ratio |
| --- | --- | --- | --- |
| opening | 4,096 | 20 | 205× |
| Kiwipete | 4,096 | 48 | 85× |

`ValidInputSet.Evaluated` — how many times `when` ran — is 20 in the opening position and
48 at Kiwipete. **The guard never sees anything but a pseudo-legal move.**


## 2. The vocabulary that was missing

All of it satisfies "the specification can be written without naming a particular rule
set". Not one chess-specific node was built.

| Added | For |
| --- | --- |
| [`Rulealize.Plugin.Tuple`](https://github.com/reny-develop/Rulealize.Plugin.Tuple/blob/main/doc/specification.md) (new) | the compound parameter itself — a compound value with a canonical text form |
| [`seq.of`](https://github.com/reny-develop/Rulealize.Plugin.Sequence/blob/main/doc/specification.md) (Sequence 1.1) | a sequence literal, for the knight's eight offsets |
| [`grid.with` / `grid.withMany`](https://github.com/reny-develop/Rulealize.Plugin.Grid/blob/main/doc/specification.md) (Grid 1.1) | building the board after a move, as a value |
| [`grid.square`](https://github.com/reny-develop/Rulealize.Plugin.Grid/blob/main/doc/specification.md) (Grid 1.1) | a state field holding one coordinate |

That `seq.of` was missing also means Sequence did not satisfy
[criterion A, independent loadability](dsl-example-reversi.md), on its own. Reversi never
noticed because all of its sequences come out of `grid.*`.

**What turned out not to be needed** is worth recording too.

- **A node for reading a rank or a file.** `seq.count(grid.ray(c, dir))` gives the distance
  to the edge, which decides both a pawn's starting rank (one square behind it) and the
  promotion rank (zero in front).
- **A node for building a single direction.** A direction has a canonical text form, so
  `dir` is written as the string literal `"0,-1"`. [Grid](https://github.com/reny-develop/Rulealize.Plugin.Grid/blob/main/doc/specification.md) had recorded that
  directions could only be obtained from `grid.directions`, and that was simply false.
- **A dedicated plugin for promotion or captured pieces**, which [Grid](https://github.com/reny-develop/Rulealize.Plugin.Grid/blob/main/doc/specification.md) had
  expected. Promotion replaces a value on the board, so a way to update a board as a value
  was all it took.


## 3. The three holes it found

### 3.1 `when` cannot see the position the move leads to

**This is a separate constraint from the domain-dependency one.**

A guard evaluates against the state as the input found it, and `effects` run only after it
passes. Reversi's legality is a property of the position in front of you, so that order
suits it. Chess's legality is "and your own king is not then capturable", which needs a way
to ask about the later position.

`grid.with` answered it, **because a board is one value in one field.**

```jsonc
"legal": { "params": ["m"], "body": {
  "op": "bind.let",
  "bind": { "b": { "op": "def.call", "def": "boardAfter", "args": { "m": "@m" } } },
  "in": { "op": "logic.not", "value": { "op": "def.call", "def": "attacked", "args": {
      "b": "@b",
      "sq": { "op": "def.call", "def": "kingSquare", "args": { "b": "@b", "colour": "#me" } },
      "by": "#foe" } } } } }
```

**A rule that depends on the whole state after a transition — several fields at once — is
still unwritable.** There is no way to apply `effects` speculatively and get the resulting
state as an expression, and no plugin can supply one. Chess getting away with it was close
to luck. [Shogi §4](dsl-example-shogi.md) pushed the same trick as far as it goes.

### 3.2 `ApplyToState` was not checking the domain (**fixed**)

**The most important thing this exercise found, and it changed the core.**

As written at the time, [`RuleContext.Apply`](../src/RuleContext.cs) bound the input
document's arguments directly and never evaluated the domain at all. The only thing
standing between a document and a transition was `when`.

Reversi was unharmed: its domain is all 64 squares and every rule about placement is in the
guard. In chess the rules about how pieces move are **in the domain** — that is what a
compound parameter is — so `when` is only a check-safety filter. A pawn advancing three
squares went through without so much as an exception.

#### The resolution: **a domain is part of the rules**

Two options.

| | | Consequence |
| --- | --- | --- |
| A | a domain is a **hint** about the candidate space; `when` is the authority | a rule set that narrowed its domain to avoid blow-up owes a second copy of the rule in its guard |
| B | a domain **is the rule**, and the runtime checks it when applying | applying a move costs a walk of the domain |

**B.** Under A, the only reason to narrow is combinatorial blow-up, and that is exactly
when writing the rule twice costs most — to write and to run. Making chess restate
pseudo-legality in its guard is asking for a second move generator.

Implemented in
[`RuleContext.BindArguments` / `Resolve` / `Matches`](../src/RuleContext.cs). Three things
were settled along with it.

1. **A match is equal values, or text that is an opaque value's canonical form.** Only the
   latter is the return leg of the trip [`ValidInput.ToInputDocument`](../src/ValidInput.cs)
   opens, and it goes no wider. **`"2"` still does not match `2`** — [value model
   §2](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/value-model.md)'s "different kinds are unequal" holds at the boundary too.
2. **What gets bound is the domain's value** (→ §3.3).
3. **Failure is `IllegalInputException`**, not distinguished from rejection by the guard.
   To the caller both mean "not a move you can make here".

The domain stops at the first match. Sequences are lazy, so what is actually paid for is
the part of the domain up to the argument.

#### What it cost, measured

Over a deep perft — opening to depth 4 plus Kiwipete to depth 3, about 300,000 positions —
**32 s → 35 s, about 9%.** Not the doubling that was expected.

The reason is in §5. `ApplyToState` already evaluated `terminal` on every transition, and
that includes `hasLegalMove`, which generates every pseudo-legal move and tests each for
legality. **A dominant cost was already there, so one short-circuited partial generation
hid behind it.** In a game whose ending does not depend on whether a legal move exists, the
ratio would be worse.

### 3.3 The kind of a value depended on how it arrived (**fixed**)

A consequence of point 2 above. **What gets bound is the domain's value**, so the candidate
`GetValidInputs` offered and the same move applied from a document put exactly the same
value in front of every expression.

Before the fix they did not: opaque via `GetValidInputs`, text via `ApplyToState`. Chess
had worked around it by **carrying the kind of move in a tag**.

```
"-"  ordinary / "2"  pawn double step / "ep"  en passant
"q" "r" "b" "n"  promotion / "0-0" "0-0-0"  castling
```

The tag survives the fix. Being able to say what kind of move it is without comparing
coordinates is good design on its own terms, independent of the asymmetry it was working
around.

**What remains true**: normalization guarantees both routes produce the same value, and it
does **not** guarantee that **the elements agree in kind**. The coordinates in a chess move
are opaque, from `grid.coords`, in an ordinary move and written-out text in a castling
move. That is the rule set's own doing rather than anything about the entry point. Matching
them uses `branch.match`, which compares canonical text — the
[`corner` definition](../ruleset/chess.json) does this when updating castling rights.


## 4. What could not be written

- **Threefold repetition** — a state is a position, and repetition is a property of the
  game record. Writable with a history in the state, and `state.schema` had no type for a
  sequence. It has one now: [`type.list`](https://github.com/reny-develop/Rulealize.Plugin.TypeSchema/blob/main/doc/specification.md), which
  [collections §6](collections.md) sketches the repetition rule against.
- **Draws by insufficient material** — writable, and left out because it is a long
  enumeration of combinations and nothing else. Not a question of expressiveness.


## 5. Cost

The opening position to perft(4) and Kiwipete to perft(3) together take about 30 seconds
for roughly 300,000 positions, so 0.1 ms or so per position — a reasonable range for
interpreting a DSL.

But there is a place where it is **structurally paying twice**. `ApplyToState` evaluates
`terminal` on every transition, and `terminal.when` includes `hasLegalMove`. Every move
made therefore runs a whole legal-move search.

```jsonc
"terminal": { "when": { "op": "logic.or", "any": [
  { "op": "cmp.gte", "left": "$idle", "right": 100 },
  { "op": "logic.not", "value": "#hasLegalMove" } ] } }
```

The cheap test is placed first for `logic.or`'s short-circuit, but `idle >= 100` is almost
never true so it never helps. With checkmate and stalemate as terminal conditions this is
unavoidable, and **"a game whose ending depends on whether a legal move exists pays double
per transition" is a property worth understanding as specified behaviour.**


## 6. What this implies for the core change to make domains parameter-dependent

Is the change unnecessary if a compound parameter suffices?

> **Since**: [shogi settled it](dsl-example-shogi.md). The second prediction below is
> **wrong** — a shogi move also fits in three elements, and the drops that were expected to
> demand a fourth turned out to be a separate input with two independent domains.

**Reasons for the change**

- The structure of the return value is lost. It is `{"m": "e2|e4|-"}`, not
  `{"from":"e2","to":"e4"}`, so the caller takes a string apart.
- ~~The tag scheme is a workaround for §3.3 rather than a design. A rule that needs a
  fourth element — a shogi move carrying both a promotion choice and a drop square — would
  nest and fall apart.~~ Wrong.
- `tuple.of` and `tuple.at` push into the value model something that is really a question
  about the shape of an input.

**Reasons to wait**

- The reduction from 4,096 to 20 was actually achieved, which removes the performance
  motive entirely.

Settling §3.2 as "a domain is part of the rules" **put one premise in place**: it is now
the runtime's official position that rules live in domains, so extending them is a coherent
change that widens where a rule may be written. Before that, the same change would only
have widened an unchecked area.

What is left is how it reads to write, and shogi is where that gets decided.

→ [It did](dsl-example-shogi.md). A shogi move fits in three elements too, so the
prediction about element counts was wrong. The readability argument survives but is weak,
and what shogi actually exposed was **nowhere to put the captured pieces**. The same effort
spent on the expressiveness of the state goes further.


## 7. What is still open, and what has closed

- **Asking about the whole state after a transition** — the remainder of §3.1, and the
  largest thing the DSL still cannot say. The shape of an answer is known: the core already
  holds `inputs` the way it holds `definitions`, so the precedent is available — the core
  applies an input's effects speculatively and a plugin provides the vocabulary for asking,
  returning the resulting state as a `Record` for `rec.at` to read. It would take an
  Abstraction hook, core support, and a thirteenth plugin. It is not being built, because
  across five rule sets nothing is actually blocked: chess and shogi both reach far enough
  with `grid.with`, and neither roster nor deploy asks the question at all. **The trigger is
  a rule set that cannot be written** — a rule where the board and another field both
  matter after the move, which shogi came within one step of needing
  ([shogi §4](dsl-example-shogi.md)).
- ~~**`type.list` / `type.record`**~~ — resolved. Threefold repetition, captured pieces and
  the histories of non-game uses all needed them, and they became
  [`type.list` and `rec.of` / `rec.map`](collections.md).
- **The cost of the terminal test** — §5. Letting a caller skip evaluating `terminal` would
  help most in exactly this shape of game, and nothing has asked for it: the perft harness
  is the heaviest user in the repository and finds 30 seconds acceptable. Worth doing when
  a caller is measured against it, and it is a public API addition rather than a design
  question. Note that §3.2's checking cost hides behind this one, so touching this first
  makes that one relatively larger.
- **`tuple.text`** — settled in [Tuple](https://github.com/reny-develop/Rulealize.Plugin.Tuple/blob/main/doc/specification.md): not built, and if it ever is, it
  belongs in Comparison rather than Tuple. `branch.match` covers the case at the price of
  writing the cases out.
- **Skipping the domain check when applying** — for a caller feeding back a move it got
  from `GetValidInputs`, the check recomputes a known answer. Measured at 9%, which is not
  enough to justify an opt-out that would let a caller turn off a rule check by mistake.
  The shape it would take, if wanted, is a flag on the call rather than a change to the
  rules.
