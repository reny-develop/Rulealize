# Rulealize.Plugin.Logic

| | |
| --- | --- |
| Identifier | `Rulealize.Plugin.Logic` |
| Namespace | `logic` |
| Version | `1.0.0` |
| Reserved prefix | none |
| Depends on | [the value model](../value-model.md), and nothing else |

Boolean operations. Every node here takes `Bool` and returns `Bool`, and none of them is
anything but an expression.

Separate from [Branch](Branch.md) on the grounds that `logic.and` is an expression that
assembles a condition, not a node that changes what gets evaluated.

## Nodes

| Node | Kind | Used in Reversi |
| --- | --- | --- |
| `logic.and` | expression | ○ `canPlace` |
| `logic.or` | expression | ○ `terminal.when` |
| `logic.not` | expression | ○ `inputs.pass.when`, `terminal.when` |
| `logic.xor` | expression | — |

---

## `logic.and`

### Form

```jsonc
{ "op": "logic.and", "all": [<expression:Bool>, ...] }
```

### How it evaluates

Each element of `all` is evaluated **in order**, and the first `false` is the answer
(short-circuit). All `true` gives `true`.

**An empty array is `true`** — no constraints means nothing to fail.

### The evaluation order is guaranteed

So that short-circuiting means something, `all` is evaluated **in array order**. Reordering
or parallelizing it is not allowed.

This is not for speed. It is so that whoever writes the rule set can put the cheap check
first and the expensive one after, and have that hold. Reversi's `canPlace` depends on it.

```jsonc
{
  "op": "logic.and",
  "all": [
    { "op": "cmp.isNull", "value": { "op": "grid.at", "grid": "$board", "coord": "@at" } },
    { "op": "seq.any", "source": { "op": "def.call", "def": "flips", "args": { "at": "@at" } } }
  ]
}
```

The first element reads one square; the second walks eight rays. `GetValidInputs` evaluates
`canPlace` for all sixty-four squares, so dropping the occupied ones on the first element
matters more the fuller the board gets — by the endgame most squares never reach a ray
walk at all.

**Without that guarantee the rule set has no way to control its own cost.**

### Errors

| Condition | When |
| --- | --- |
| an element is not `Bool` | evaluation. Short-circuiting means unevaluated elements are not checked |
| `all` is not an array | static |

There is no implicit conversion of `Null` or `0` to false, the same policy
[Branch](Branch.md)'s `branch.if` follows.

---

## `logic.or`

### Form

```jsonc
{ "op": "logic.or", "any": [<expression:Bool>, ...] }
```

### How it evaluates

Each element of `any` in order, and the first `true` is the answer. All `false` gives
`false`.

**An empty array is `false`.**

The order guarantee is `logic.and`'s.

### Example (Reversi's `terminal.when`)

```jsonc
{
  "op": "logic.or",
  "any": [
    { "op": "cmp.gte", "left": "$passes", "right": 2 },
    { "op": "logic.not", "value": { "op": "seq.any", ... } }   // no empty square left
  ]
}
```

Two consecutive passes is tested first: one integer comparison, ahead of an element that
scans the whole board.

---

## `logic.not`

### Form

```jsonc
{ "op": "logic.not", "value": <expression:Bool> }
```

### How it evaluates

Evaluates `value` and returns its negation.

### Example (Reversi's `inputs.pass.when`)

```jsonc
{ "op": "logic.not", "value": "#hasAnyMove" }
```

Passing is legal exactly when there is no move.

---

## `logic.xor`

### Form

```jsonc
{ "op": "logic.xor", "of": [<expression:Bool>, ...] }
```

### How it evaluates

Evaluates every element and returns `true` when an odd number of them are `true`.

**No short-circuit**, because exclusive or is not decided until every element has been
seen. That is the one place this differs from `and` and `or`, so put expensive expressions
here knowing they will all run.

An empty array is `false`. A single element is itself.

Not used in Reversi. Provided as general vocabulary.

---

## Design notes

### Why variadic arrays rather than binary nesting

`and(and(a, b), c)` was rejected because the main use is enumerating constraints.
`canPlace`'s shape — "all of these hold" — falls out of an array and does not fall out of
nesting.

The key is named differently per operation (`all` / `any` / `of`) so that reading the node
says the operation twice: `"op": "logic.and", "all": [...]`.

### Why there is no `logic.implies`

`a → b` is `logic.or(logic.not(a), b)`. Useful in constraint work, but its short-circuit
behaviour reads wrong — nothing about `a → b` suggests that `b` goes unevaluated when `a`
is false — so it waits until something demonstrates the need.

---

## Decided

- **No three-valued logic.** Treating `Null` as "unknown" is not introduced, and anything
  that is not `Bool` stays an evaluation fault. The scenario that would argue for it is an
  aggregate whose predicate can return `Null`, and across five rule sets that has never
  arisen: writing `cmp.isNull` where the question is actually being asked has been both
  possible and clearer every time.
- **No limit on how many elements `logic.and` takes.** The reason to want one would be
  estimating what a `GetValidInputs` sweep costs before running it, and expression size is
  the wrong handle for that — sequence length is the one that governs, which
  [Sequence](Sequence.md) records under the same heading.
