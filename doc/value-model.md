# The value model, and the three kinds of node

What every plugin assumes. Each plugin specification refers back to this document, so read
this one first.

`Rulealize.Abstraction` defines it, and it is the only thing that holds plugins together.
**No plugin may reference another plugin**, so the reason `seq.any` can consume what
`grid.coords` produced is that both are speaking in terms of the model described here and
neither has heard of the other.


## 1. The kinds of value

| Kind | What it is | As a JSON literal |
| --- | --- | --- |
| `Null` | the absence of a value | `null` |
| `Bool` | a truth value | `true` / `false` |
| `Number` | a number; integers and fractions are not distinguished | `42`, `1.5` |
| `Text` | a string | `"black"` |
| `Sequence` | a finite ordered run of values | (none) |
| `Record` | a map keyed by strings | `{ "a": 1 }`, when it carries no `op` |
| `Opaque` | a plugin's own value | (none — travels as canonical text) |

`Sequence` and `Opaque` have no JSON literal of their own and can only arrive as the
result of evaluating a node.

### 1.1 Opaque

Holds what belongs to one plugin and means nothing to the core — a coordinate, a
direction. An opaque value carries a **type tag** such as `grid/coord`, and two opaque
values with different tags are never equal.

**An opaque value that can appear in `inputs.*.params` must have a canonical text form.**
Two reasons, and they are the two directions of one trip.

- The `args` in what `GetValidInputs` returns leave as a JSON document (`{ "at": "d3" }`).
  A coordinate that cannot be written as text cannot be serialized.
- The `args` in an input document arrive as text. A coordinate that cannot be recovered
  from text cannot be used as a parameter.

So a node that accepts an opaque value **accepts both the opaque value itself and the text
that is its canonical form** — see "what a coordinate may be written as" in each plugin's
specification.

**A value that only ever lives inside the state carries no such requirement.** A board is
the example: serializing it belongs to the schema node (`grid.board`), so the value itself
never has to become text. This was a requirement of every opaque value to begin with, and
turned out to be more than the design needed. `RuleValue.GetCanonicalText()` returns
`null` by default, and only the types that need one override it.

### 1.2 A sequence has to survive being enumerated twice

Laziness is allowed, but **enumerating one sequence value more than once must produce the
same run of values each time**. Reversi's `flips1` enumerates `@ray` twice — once through
`seq.takeWhile` and once through `seq.elementAt` — so this is a rule rather than something
an implementation should try to achieve.

Every node is pure, so either strategy satisfies it: re-run the computation on each
enumeration, or buffer on the first. What is ruled out is a one-shot iterator.


## 2. Equality

What `cmp.eq` and everything like it rests on.

- Values of different kinds are never equal (`1` and `"1"` are not).
- `Null` equals `Null`.
- `Number` compares numerically (`1` equals `1.0`).
- `Sequence` compares element by element, including length and order.
- `Record` compares by key set, and then value by value.
- `Opaque` compares by type tag, and then by whatever the defining plugin says.


## 3. How null travels

**There is no blanket rule.** Each node states its own, and each plugin specification
writes it down. The policy behind those choices:

| Situation | What happens | Examples |
| --- | --- | --- |
| a lookup where "not there" is a normal answer | returns `Null` | `grid.at` off the board, `seq.elementAt` out of range |
| equality | `Null` is just another value | `cmp.eq(null, "black")` is `false` |
| ordering and arithmetic | an evaluation fault | `cmp.lt(null, 1)`, `math.add(null, 1)` |

Together these let Reversi's `flips1` drop the "the ray is opponent stones all the way to
the edge" case into its `else` branch without a boundary check anywhere in the rule set.

```
seq.elementAt(out of range) → null
  → grid.at(coord: null) → null
    → cmp.eq(null, "black") → false
      → the else branch of branch.if
```

That chain is fixed, and rule sets are written on the strength of it.


## 4. The three kinds of node

A plugin provides three kinds of node. One plugin may provide more than one kind
(`Rulealize.Plugin.Grid` provides all three).

| Kind | What it does | Where it may appear |
| --- | --- | --- |
| **expression** | evaluates to a value; pure | `when`, the arguments of `effects`, the body of a `definitions` entry, `params[].domain`, `terminal` |
| **effect** | describes a write to the state draft | only as an element of `inputs.*.effects` |
| **schema** | describes the type of a state field | only inside `state.schema` |

A node in a position its kind does not allow is a build error, raised while the rule set is
compiled (`CreateContext`), never at run time.

### 4.1 A domain is an expression node

What sits in `params[].domain` is an expression node that returns a `Sequence`, not a
fourth kind of node. `GetValidInputs` enumerates it to form candidates.

**A domain is part of the rules, not a hint for the candidate search.** `ApplyToState`
resolves each argument against its domain too, and refuses a value the domain does not
produce. A rule set may therefore state a rule in a domain or in `when` as it prefers, and
one that moves work into a domain to keep the candidate count down does not owe a second
copy of that rule in the guard. (→ [what chess made of this, §3.2](dsl-example-chess.md))

What gets bound is **the value the domain produced**. An argument arrives from a document
as JSON, so an opaque value arrives as text, and it is replaced by the value it matched
before anything evaluates. The candidate `GetValidInputs` offered and the move applied
from a document put exactly the same value in front of every expression downstream.


## 5. What applying effects means

`effects` has **snapshot semantics**.

1. The state as the input found it is fixed as a read-only snapshot.
2. Every expression in every element of `effects` is evaluated **against that snapshot**.
3. Writes accumulate in a draft and are applied together once every element has run.

Applying them one at a time would mean that in Reversi's `place`, the `flips` evaluated
after the "put the stone down" effect would rescan a board that already has the new stone
on it. Snapshot semantics is what removes that kind of dependence on the order the effects
were written in.

Where two writes land on one path, **the last one wins**.

An effect that needs to build on what an earlier effect wrote reads the field back from the
draft — which is a different question from what its expressions see, and both are in play
at once when two effects edit one board.

When the transition commits, each written field is settled by its schema node
(`SchemaNode.Normalize`) and then **checked against that schema**. A rule set whose effects
can assemble a state the schema forbids is told so at the transition that did it, naming
the input; without that check the state would go back to the caller intact and the fault
would surface on the next read, one transition away from the effect responsible. Fields no
effect touched are not rechecked: they came out of a document or out of an earlier commit,
and either way they have been through this once already.


## 6. Scope

An evaluation context has three layers of scope.

| Layer | How it is referred to | Where it is visible |
| --- | --- | --- |
| state | `$path` (the State plugin) | everywhere |
| definitions | `#name` (the Definition plugin) | everywhere |
| local bindings | `@name` (the Binding plugin) | only the part of the introducing node that says so |

Local bindings **may shadow** — an inner binding hides an outer one of the same name.

**The body of a definition cannot see the caller's local bindings.** Bodies are hygienic,
and `def.call`'s `args` is the only way to pass a value in. This is what fixes a
definition's meaning independently of where it is called from.


## 7. String sugar

A plugin may reserve a leading character and expand string literals that begin with it into
nodes of its own. The core does not know sugar exists.

| Prefix | Reserved by | Expands to |
| --- | --- | --- |
| `$` | `Rulealize.Plugin.State` | `{ "op": "state.get", "path": "…" }` |
| `@` | `Rulealize.Plugin.Binding` | `{ "op": "bind.local", "name": "…" }` |
| `#` | `Rulealize.Plugin.Definition` | `{ "op": "def.ref", "name": "…" }` |

Colliding prefixes are detected when plugins are loaded.

**There is no way to write a literal string that begins with a reserved character**, and
none is provided. Nothing in five rule sets has needed one, and the hole is narrower than
it looks: only a literal in the rule set document is expanded, so text arriving in a state
document, a key in `branch.match`'s `cases`, and any value computed at run time are all
unaffected. If a rule set does need one, the answer is for the plugin that reserved the
character to unescape a doubled one (`$$x` meaning `$x`) rather than for a new node or a
new plugin to appear — the character is that plugin's to spend, and the core still gets to
know nothing about any of it.
