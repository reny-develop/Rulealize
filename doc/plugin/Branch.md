# Rulealize.Plugin.Branch

| | |
| --- | --- |
| Identifier | `Rulealize.Plugin.Branch` |
| Namespace | `branch` |
| Version | `1.0.0` |
| Reserved prefix | none |
| Depends on | [the value model](../value-model.md), and nothing else |

Branching: two ways on a boolean (`if`), and many ways on a value (`match`).

Separate from [Logic](Logic.md), because `logic.and` and the rest are **expressions** that
return a truth value rather than control structures. Splitting them lets a rule set that
does no branching at all — a pure constraint description, say — leave Branch out.

## Nodes

| Node | Kind | Used in Reversi |
| --- | --- | --- |
| `branch.if` | expression | ○ `flips1` |
| `branch.match` | expression | ○ `opponent`, `terminal.result` |

---

## `branch.if`

### Form

```jsonc
{
  "op": "branch.if",
  "cond": <expression:Bool>,
  "then": <expression>,
  "else": <expression>          // optional
}
```

### How it evaluates

1. `cond` is evaluated.
2. `true` evaluates `then` and returns its value.
3. `false` evaluates `else` and returns its value; with no `else`, returns `Null`.

**The branch not taken is not evaluated.** That is not only for speed — it is what lets a
condition guard an expression that would otherwise fault, such as protecting a
`seq.elementAt` behind a non-empty check.

### Types

`then` and `else` are not required to agree on the kind of value they produce. Whatever
consumes the result is where a kind gets required, and that is where it gets checked.

### Errors

| Condition | When |
| --- | --- |
| `cond` is not `Bool` | evaluation. `Null` and `0` are not converted to false |
| `then` is missing | static |

Refusing the implicit conversion is what stops the `Null` that `grid.at` returns for an
empty square from quietly passing as false. Asking whether a square is empty means writing
`cmp.isNull`.

### Example (Reversi's `flips1`)

```jsonc
{
  "op": "branch.if",
  "cond": {
    "op": "cmp.eq",
    "left": { "op": "grid.at", "grid": "$board",
              "coord": { "op": "seq.elementAt", "source": "@ray",
                         "index": { "op": "seq.count", "source": "@run" } } },
    "right": "#me"
  },
  "then": "@run",
  "else": { "op": "seq.empty" }
}
```

If the square just past the run of opponent stones holds one of mine, that run is what gets
flipped; otherwise nothing. The condition leans on the null propagation of
[value model §3](../value-model.md), so a ray that runs out at the board edge falls into
`else` on its own.

---

## `branch.match`

Branching on a value.

### Form

```jsonc
{
  "op": "branch.match",
  "value": <expression>,
  "cases": { "<key>": <expression>, ... },   // the keys are static
  "default": <expression>                    // optional
}
```

### How it evaluates

1. `value` is evaluated.
2. The result is converted to its **canonical text** and matched against the keys of
   `cases`, exactly.
3. The matching case is evaluated and returned.
4. With no match and a `default`, `default` is evaluated and returned.
5. With no match and no `default`, **an evaluation fault**.

No case but the matching one is evaluated.

### How keys are matched

The keys of `cases` are JSON object keys and therefore always strings, so the value is
turned into text to match them.

| Kind of `value` | Canonical text |
| --- | --- |
| `Text` | itself |
| `Bool` | `"true"` / `"false"` |
| `Number` | normalized decimal (`1.0` → `"1"`) |
| `Null` | `"null"` |
| `Opaque` | whatever the defining plugin says ([value model §1.1](../value-model.md)) |
| `Sequence` / `Record` | an evaluation fault; there is nothing to match against |

That `Opaque` matches is what makes branching on a coordinate or a direction possible.

### Exhaustiveness

Checking that `cases` covers everything, when there is no `default`, would need to know
statically that `value` comes from a `type.enum`. There is no inference over expressions,
so **exhaustiveness is not checked and a missing case is an evaluation fault**.

For Reversi's `opponent` that is safe in practice — `turn` is a `type.enum` of `black` and
`white`, and both are covered — but what guarantees it is the validation of
`state.schema`, not anything in the DSL.

### Errors

| Condition | When |
| --- | --- |
| `cases` is empty | static |
| duplicate keys | static, as duplicate JSON keys |
| no match and no `default` | evaluation |
| `value` is a `Sequence` or `Record` | evaluation |

### Example (Reversi's `opponent`)

```jsonc
{
  "op": "branch.match",
  "value": "#me",
  "cases": { "black": "white", "white": "black" }
}
```

`terminal.result` uses it on the return value of `cmp.compare` (`"lt"` / `"eq"` / `"gt"`).
That `cmp.compare` returns text rather than a number is what makes the two fit together.

---

## Decided

- **`cases` matches keys exactly, and gains no pattern language.** No ranges, no structural
  patterns. This was recorded as something more complicated rule sets — branching on a
  piece kind was the example — would likely force, and then chess and shogi were written.
  Neither forced it: shogi has fourteen piece kinds and dispatches all of them through
  exact matches on canonical text. The prediction was tested and did not hold.
- **No `branch.cond` for multi-way if-else.** The concern was nesting `branch.if` getting
  deep, and the rule sets say otherwise — chess uses `branch.match` 27 times against
  `branch.if` 12, shogi 15 against 6. Multi-way dispatch is already going through
  `branch.match`, which is what a `branch.cond` would have been competing with rather than
  the nesting it was meant to replace.
- **Exhaustiveness stays a run time fault**, because promoting it needs inference from
  `state.schema` into expression types, and [TypeSchema](TypeSchema.md) records why that is
  not being built yet. This is the one item here that is waiting on something rather than
  settled against.
