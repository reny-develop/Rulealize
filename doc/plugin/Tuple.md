# Rulealize.Plugin.Tuple

| | |
| --- | --- |
| Identifier | `Rulealize.Plugin.Tuple` |
| Namespace | `tuple` |
| Version | `1.0.0` |
| Reserved prefix | none |
| Depends on | [the value model](../value-model.md), and nothing else |

Packs several values into one and takes them apart again. **A compound value that has a
canonical text form** is the reason this plugin exists, and it does nothing else.

## Why it is needed

The domains of `inputs.*.params` are evaluated independently of one another
([`RuleContext.Collect`](../../src/RuleContext.cs)). Candidates are the product of the
domains, and no domain can refer to another parameter's value, because that value has not
been chosen yet.

So writing a chess move as two parameters, the obvious way,

```jsonc
"params": {
  "from": { "domain": { "op": "grid.coords", "of": "$board" } },
  "to":   { "domain": { "op": "grid.coords", "of": "$board" } }
}
```

gives 64 × 64 = 4,096 candidates in every position. The real number of legal moves is 20 to
40, and the guard has to reject the rest one at a time.

Make it one parameter and the domain becomes a single expression, free to compute
destinations from origins.

```jsonc
"params": {
  "m": {
    "domain": {
      "op": "seq.selectMany",
      "source": { "op": "grid.coords", "of": "$board" }, "as": "f",
      "select": { "op": "def.call", "def": "movesFrom", "args": { "f": "@f" } }
    }
  }
}
```

[The chess rule set](../../ruleset/chess.json) takes this shape: 20 candidates in the
opening position with `Evaluated` of 20 ([ChessTests](../../test/ChessTests.cs)).

## Nodes

| Node | Kind | Form |
| --- | --- | --- |
| `tuple.of` | expression | `{ "op": "tuple.of", "of": [ <expression>, … ] }` |
| `tuple.at` | expression | `{ "op": "tuple.at", "tuple": <expression>, "index": <integer> }` |

`index` is **static**. A tuple's length is fixed where the tuple was built, so a computed
index would be asking a question the shape of the value has already answered. Out of range
is an evaluation fault; reading from a `null` tuple gives `null`, the same treatment
`grid.at` gives a square off the board.

## The canonical text form

The canonical text of each element, joined with `|`. A chess move is written `"e2|e4|-"`.

This is the heart of the plugin. A `Sequence` carries the same values perfectly well, but
the value model gives `Sequence` no canonical text, so it can appear neither in what
`GetValidInputs` returns nor in an input document.

```jsonc
{ "input": "move", "args": { "m": "e2|e4|-" } }
```

`|` and `\` are escaped so the trip survives an element containing the separator.

**A tuple has no canonical text if any one of its elements has none.** `null` is one such
element, so an optional slot needs a sentinel — chess writes `"-"` for a move that is not a
promotion.

## Both routes produce the same value

The runtime resolves an input's arguments against the domain and **binds the value the
domain produced** ([`RuleContext.BindArguments`](../../src/RuleContext.cs)). A move applied
from a document is therefore the very tuple the domain built, and `tuple.at` returns what
was put in whichever route it arrived by — an opaque coordinate stays opaque.

This is worth writing down because **it did not use to be true**, and a compound parameter
is where the difference showed up first. Handing a coordinate straight back to the plugin
that made it hides the difference ([value model §1.1](../value-model.md)); handing it to
`cmp.eq` did not.

**What is not guaranteed is that the elements agree in kind.** An element is whatever the
expression that built the tuple returned, so a tuple built from `grid.coords` and one built
from a written-out square have differently-kinded contents. Chess is exactly that — only
the castling moves carry `Text` — and it gets away with it because `branch.match` matches
on canonical text, which both kinds have.

```jsonc
// answers either way
{ "op": "branch.match", "value": "@c",
  "cases": { "a1": "a1", "e1": "e1", "h1": "h1" }, "default": "-" }
```

---

## Decided

- **A tuple cannot be a state field, and does not need to be.** This was recorded as
  something a rule set with a history or a queue would need. Roster has both — a staff
  list, a shift list, an assignment list and a bounded audit log — and needed no tuple:
  the schema-able compound is [`rec.of`](Record.md), inside a
  [`type.list`](TypeSchema.md). The division that emerged is a clean one. **A tuple exists
  for the one property a state field never needs — a canonical text form — because that is
  what an input argument has to survive the round trip.** A record has no canonical text
  and no need of one, since serializing a state field is the schema node's job. Two
  compounds, two jobs.
- **A tuple cannot be an input argument's *value type* beyond that**, which is the same
  point from the other side: [Record](Record.md) records that a domain returning records
  fails for want of a text form, and that a compound input should use a tuple.
- **The length of a tuple is not declared, so `tuple.at` checks its index at evaluation.**
  Declaring that a domain's elements are all of some length would move the check to
  `CreateContext`, and that needs inference over expressions, which
  [TypeSchema](TypeSchema.md) records is not being built yet. This is one of the four
  things waiting on that.
- **No `tuple.text`** — a node returning the canonical text of any value. Its original
  motivation was normalizing arguments, and that disappeared when the runtime started
  binding the domain's value. What remains is matching values whose kinds are mixed, and
  `branch.match` already does that; the cost is having to write the cases out statically,
  which chess does and finds tolerable. If it is ever built it does not belong here — it is
  a question about any value, not about tuples — and the home would be
  [Comparison](Comparison.md), which already owns the questions asked across kinds
  (`cmp.eq` comparing unlike things, `cmp.isNull`) and is loaded by every rule set anyway.
