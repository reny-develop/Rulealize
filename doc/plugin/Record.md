# Rulealize.Plugin.Record

| | |
| --- | --- |
| Identifier | `Rulealize.Plugin.Record` |
| Namespace | `rec` |
| Version | `1.0.0` |
| Reserved prefix | none |
| Depends on | [the value model](../value-model.md), and nothing else |

Puts a record in the state and reads and writes it by a **computed key**. How the design
was arrived at is in [collections](../collections.md); what asked for it is
[shogi](../dsl-example-shogi.md).

## Paths stay literal

Extending `state.set`'s `path` to dotted notation was considered and rejected. It gives up
the three things [the State plugin](State.md) lists — checking every path up front, reading
which field an input writes straight off the document, and not carrying schema validation
into run time.

Instead it uses **the same seam as `grid.board`**: the path names the whole field, and the
inside is touched with the vocabulary of whichever plugin defined it. `"hand.black.P"` is
not writable, for the same reason `"board.d3"` is not.

## Nodes

| Node | Kind | Form |
| --- | --- | --- |
| `rec.of` | schema | `{ "op": "rec.of", "fields": { "<key>": <schema>, … } }` |
| `rec.map` | schema | `{ "op": "rec.map", "keys": ["<key>", …], "value": <schema> }` |
| `rec.at` | expression | `{ "op": "rec.at", "record": <expression>, "key": <expression:Text> }` |
| `rec.has` | expression | `{ "op": "rec.has", "record": <expression>, "key": <expression:Text> }` |
| `rec.with` | expression | `{ "op": "rec.with", "record": <expression>, "key": <expression>, "value": <expression> }` |
| `rec.keys` | expression | `{ "op": "rec.keys", "of": <expression> }` |
| `rec.set` | effect | `{ "op": "rec.set", "target": "$<field>", "key": <expression>, "value": <expression> }` |
| `rec.update` | effect | `{ "op": "rec.update", "target": "$<field>", "key": <expression>, "as": "<name>", "value": <expression> }` |

## The key set is closed

`rec.of` and `rec.map` both declare their keys. Not an open map, on the same judgement that
put `type.enum` ahead of `type.string` plus a regular expression: **what is declared can be
checked**.

`rec.map` is separate because **it says something different**. "Every value has the same
type" cannot be expressed with `rec.of`, and shogi's hand is exactly that.

```jsonc
// shogi's hand. Fourteen fields, once
"hand": {
  "op": "rec.map", "keys": ["black", "white"],
  "value": { "op": "rec.map", "keys": ["P", "L", "N", "S", "G", "B", "R"],
             "value": { "op": "type.int", "min": 0 } }
}
```

## Reading an absent key is a fault

**The leniency of `grid.at` is not inherited.** A board legitimately has squares that do
not exist — off the board — and Reversi's rules depend on reading one and getting `null`.
Every key of a record is declared, so asking for another one is a mistake, and no rule
depends on it being quiet.

The way to ask first is `rec.has`. Shogi, adding a captured piece to a hand, has to check
that the piece is a kind that can be held; without `rec.has` the rule set carries a list of
the seven kinds separately from the schema, and the two eventually disagree.

`rec.at` on a `Null` record gives `Null` — that is a different question, namely that there
is no record to look in. It matches `tuple.at`.

## A record cannot gain a key

`rec.with`, `rec.set` and `rec.update` all refuse a key the record does not have.
Therefore **a record that started out satisfying its schema still satisfies it however the
rule set rewrites it**, and nothing downstream has to check.

## Effects read from the draft

The same reason `grid.set` does. Two effects writing different keys of one record pile up,
and the second does not erase the first. The expressions inside an effect still read the
state as the input found it; snapshot semantics is unchanged.

```jsonc
{ "op": "rec.update", "target": "$hand", "key": "#me", "as": "h",
  "value": { "op": "rec.with", "record": "@h", "key": "@piece",
             "value": { "op": "math.sub",
                        "left": { "op": "rec.at", "record": "@h", "key": "@piece" }, "right": 1 } } }
```

## `rec.keys` is in ordinal order

Not declaration order. A record is a value, and it may have been built from a literal that
never saw a schema, so **declaration order does not always exist**. Sort order always does.
It is also what keeps `GetValidInputs` from reordering its output between runs.

## Sequences are different

A sequence field holds a `Sequence`, which is a kind of the value model, and `seq.count`,
`seq.any` and `seq.where` already read one. That is why [`type.list`](TypeSchema.md) is a
single schema node with no operations at all. **A record had nothing**, and that is the
entire reason this plugin exists.

---

## Decided

- **The key set stays closed.** The case for opening it is copying external data, where the
  keys may not be known in advance. That case then arrived — roster assigns real people to
  real shifts — and did not want an open record: the people belong in a
  [`type.list`](TypeSchema.md) of `rec.of`, because **a key set fixed by the instance is not
  a key set at all, it is a list**. Roster's [notes §4.1](../dsl-example-roster.md) work
  through the distinction, and it is the same one that decides between `rec.of` and
  `rec.map`: `rec.map`'s keys are right when the domain fixes them, as shogi's seven piece
  kinds are fixed by the rules of shogi.
- **A record cannot be an input argument, and that is correct.** It has no canonical text,
  so a domain returning records fails when an argument is resolved. A compound input uses
  [Tuple](Tuple.md), which exists for exactly that.
- **Invariants across fields still cannot be written.** "The pawns on the board and in both
  hands total eighteen" has no home: `rec.of` and `rec.map` constrain one field, and there
  is no predicate language for `state.schema`. Since a transition now checks what its
  effects built ([TypeSchema](TypeSchema.md)), there is at last a *place* such a check would
  run — which is the part that used to be missing — but the language to write one in is
  not there, and adding it means a new reserved key in a document whose reserved keys are
  deliberately eight. Both shogi and roster record wanting it, neither is blocked by not
  having it, and it stays unbuilt until one is.
- **`rec.of` is where inference would start.** A record of heterogeneous fields is one of
  the few places where the type of what `rec.at` returns is statically determined, so it is
  the natural foothold. It waits on inference generally
  ([TypeSchema](TypeSchema.md) records the condition).
