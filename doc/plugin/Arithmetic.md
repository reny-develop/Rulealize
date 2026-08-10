# Rulealize.Plugin.Arithmetic

| | |
| --- | --- |
| Identifier | `Rulealize.Plugin.Arithmetic` |
| Namespace | `math` |
| Version | `1.0.0` |
| Reserved prefix | none |
| Depends on | [the value model](../value-model.md), and nothing else |

Arithmetic on `Number`.

Reversi reaches for this in exactly one place — incrementing the pass count — which makes
it the best illustration of why the vocabularies are cut this finely. **Had it been folded
into a `Rulealize.Plugin.Core`, reading Reversi's `requires` would tell you nothing about
whether the rule set does arithmetic.** Kept separate, `Arithmetic` appearing in `requires`
means something is being counted somewhere.

Counting the elements of a sequence is `seq.count` and belongs to [Sequence](Sequence.md),
not here.

## Nodes

| Node | Kind | Used in Reversi |
| --- | --- | --- |
| `math.add` | expression | ○ `inputs.pass.effects` |
| `math.sub` / `math.mul` / `math.div` / `math.mod` | expression | — |
| `math.min` / `math.max` | expression | — |
| `math.abs` | expression | — |

---

## How numbers are represented

The value model's `Number` does not distinguish integers from fractions. The
implementation is **decimal fixed point** (`decimal`).

Not binary floating point, so that a rule set does not mean something different depending
on how the runtime rounds. The quantities rule descriptions deal in — piece counts,
coordinates, scores, probabilities — are written in decimal, and `0.1 + 0.2 != 0.3`
deciding a rule is not a thing anyone wants to debug.

A result outside the range of `decimal` is an evaluation fault; overflow is not quietly
rounded away.

---

## Variadic — `math.add` / `math.mul`

### Form

```jsonc
{ "op": "math.add", "of": [<expression:Number>, ...] }
{ "op": "math.mul", "of": [<expression:Number>, ...] }
```

### How it evaluates

Every element of `of` is evaluated and folded in order.

- `math.add` of an empty array is `0`, the additive identity
- `math.mul` of an empty array is `1`, the multiplicative identity

No short-circuit: `math.mul` evaluates every element even with a `0` among them. Since
every node is pure the only observable difference is speed, but the rule is stated as full
evaluation.

### Example (Reversi's `inputs.pass.effects`)

```jsonc
{ "op": "state.set", "path": "passes",
  "value": { "op": "math.add", "of": ["$passes", 1] } }
```

`$passes` reads **the value the input arrived at** by the snapshot semantics of
[value model §5](../value-model.md). Even though this effect writes `passes`, any other
expression in the same `effects` reading `$passes` still sees the original.

---

## Binary — `math.sub` / `math.div` / `math.mod`

### Form

```jsonc
{ "op": "math.sub", "left": <expression:Number>, "right": <expression:Number> }
{ "op": "math.div", "left": <expression:Number>, "right": <expression:Number> }
{ "op": "math.mod", "left": <expression:Number>, "right": <expression:Number> }
```

Not variadic, because for a non-commutative operation `of: [a, b, c]` does not say which
way the fold goes. `left` and `right` leave nothing to guess.

### How it evaluates

| Node | Result |
| --- | --- |
| `math.sub` | `left - right` |
| `math.div` | `left / right`. **Not integer division** (`7 / 2` is `3.5`) |
| `math.mod` | the remainder of `left` divided by `right` |

`math.div` is not integer division because `Number` has no integer type. Truncation would
need something like `math.floor`, which is not provided — see below.

The sign of `math.mod` on negatives **follows the dividend**, as `%` does in .NET, so
`-7 mod 3` is `-1`. A rule set needing the mathematical remainder, always non-negative,
corrects for it.

### Errors

| Condition | When |
| --- | --- |
| `right` of `math.div` or `math.mod` is `0` | evaluation |

---

## `math.min` / `math.max`

### Form

```jsonc
{ "op": "math.min", "of": [<expression:Number>, ...] }
```

### How it evaluates

Evaluates every element and returns the smallest or largest.

**An empty array is an evaluation fault.** The value model has no infinities to serve as
identities, so there is nothing to return — which is why this differs from `math.add`,
where the empty array is `0`.

The minimum or maximum *of a sequence* would belong to `seq.*`, and is not provided there
either. `math.min` is for the case where the number of operands is fixed by the document.

---

## `math.abs`

### Form

```jsonc
{ "op": "math.abs", "value": <expression:Number> }
```

Returns the absolute value.

---

## Null and kinds

**Every arithmetic node faults when an operand is not a `Number`**, and `Null` is no
exception.

The contrast with [Comparison](Comparison.md)'s null-safe `cmp.eq` is deliberate. "Equal to
an absent value" has an obvious answer, `false`; "added to an absent value" has none.
Silently treating `Null` as `0` is exactly how an empty square ends up counted as zero
points.

A value that might be `Null` gets flattened explicitly with `cmp.coalesce` before it
reaches arithmetic.

There is no implicit conversion from `Text` either: `"1" + 1` is a fault.

---

## Decided

- **No `math.floor` / `math.ceil` / `math.round`.** This was expected to be needed soon —
  `math.div` gives real division, so there is no way to land on an integer, and rules doing
  coordinate arithmetic looked like they would want one. Five rule sets later, including
  three board games, none has. The reason is that the questions that looked like they
  needed division turned out to be reachable another way: chess measures distance to the
  edge with `seq.count` over a `grid.ray` rather than by dividing coordinates. When one is
  finally needed, the rounding mode has to be decided with it, and this is where that
  discussion starts.
- **No `math.pow` / `math.sqrt`.** `sqrt` returns irrationals, which decimal fixed point
  represents badly. A rule needing distances should compare squared distances.
- **No `math.rem`** (the always-non-negative remainder). Recorded as the fix if the sign of
  `math.mod` ever bites on cyclic coordinates such as a toroidal board. No rule set has a
  toroidal board.
- **Overflow stays a fault**, rather than saturating or wrapping. Nothing has wanted
  either, and both turn a mistake into a plausible wrong answer.
- **Aggregates over a sequence belong in [Sequence](Sequence.md), not here.** This was
  filed in both places as unresolved; it is resolved by the value model. An operation
  taking a sequence is a sequence operation, and `math.min` taking a fixed list of operands
  is a different node with a similar name. Sequence records what it would take to want one.
