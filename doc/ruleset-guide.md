# Writing a rule set

A rule set is a JSON document that says what a state is, what may be done to one, and when
the thing is over. This guide is one read from an empty file to a document you can defend,
and it is written to be read straight through once.

It is a guide and not a specification. Where it states a rule, the normative text is
[the runtime's surface](runtime.md) for what the core does and
[the value model](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/value-model.md)
for what a value is; where it uses an operation, the normative text is that vocabulary's own
specification. Both are linked at every point where the difference could matter.

**One idea decides how the rest of this reads.** The core provides no operations at all —
not arithmetic, not comparison, not booleans. Everything that computes anything comes from a
plugin the document named. So there are two things to learn and they are not the same size:
the **frame**, which is ten keys and never changes, and the **vocabulary**, which is whatever
you loaded and is documented where it lives. This guide teaches the frame completely, and
teaches how to read a vocabulary, so that the second half needs no catalogue here.

The worked documents are in [`ruleset/`](../ruleset/); every one of them is compiled by the
test suite, so nothing quoted below is aspirational.

### Six words, and where each is explained

§1 shows you the whole document before the parts have names, so a few words arrive there
before their section. Here is what each one means in a sentence, and where it is properly
explained. Nothing else in this guide is used before it is defined; where a term is, it
carries its section number at the point of use.

| Word | In one sentence | Explained in |
| --- | --- | --- |
| **node** | an object in the document with an `op` in it — the unit everything below the reserved keys is built from | [§3](#3-nodes) |
| **`op`** | the name of an operation, like `cmp.eq`; it says what a node does and which plugin provides it | [§3](#3-nodes) |
| **expression** | a node that produces a value | [§3](#3-nodes) |
| **effect** | a node that writes to the state; only an input's `effects` may hold one | [§3](#3-nodes), [§6](#6-inputs--what-may-be-done) |
| **schema node** | a node that declares the type of one state field | [§3](#3-nodes), [§5](#5-state--what-a-position-is) |
| **guard** | the `when` of an input — an expression answering whether that input is allowed right now | [§6](#6-inputs--what-may-be-done) |

If you would rather meet the parts before the whole, read §2 and §3 first and come back to
§1. The order here is deliberate but it is not load-bearing.

---

## Contents

1. [The shape of the whole document](#1-the-shape-of-the-whole-document)
2. [Values](#2-values)
3. [Nodes](#3-nodes)
4. [`requires` — what the document may say](#4-requires--what-the-document-may-say)
5. [`state` — what a position is](#5-state--what-a-position-is)
6. [`inputs` — what may be done](#6-inputs--what-may-be-done)
7. [`definitions` — naming an expression](#7-definitions--naming-an-expression)
8. [`terminal` — when it is over](#8-terminal--when-it-is-over)
9. [Chance](#9-chance)
10. [`uses` and `held` — a rule set made of rule sets](#10-uses-and-held--a-rule-set-made-of-rule-sets)
11. [The three documents that travel](#11-the-three-documents-that-travel)
12. [Reading an error](#12-reading-an-error)
13. [Shaping a rule set](#13-shaping-a-rule-set)
14. [Reading a vocabulary you have not met](#14-reading-a-vocabulary-you-have-not-met)

---

## 1. The shape of the whole document

Here is every key the core reads, in one document. Nothing else at this level is allowed: a
key the core does not know is refused when the document is compiled, with a path to the
offending node (§12).

```jsonc
{
  "$schema": "rulealize/ruleset/v1",   // reserved, and not read
  "id": "approval",                    // required
  "version": "1.0.0",                  // required

  "requires": [ /* … */ ],   // the vocabularies this document draws on
  "uses":     [ /* … */ ],   // the rule sets this document holds

  "state": {                 // required, unless `uses` declares something
    "schema":  { /* … */ },  //   what a state is
    "initial": { /* … */ }   //   where one starts
  },

  "definitions": { /* … */ },  // named expressions

  "held":     { /* … */ },   // what this document says about the rule sets it holds
  "inputs":   { /* … */ },   // required, unless `uses` declares something
  "terminal": { /* … */ }    // optional
}
```

Comments and trailing commas are accepted in every document this runtime reads, which is why
the blocks in this guide are `jsonc`. Ordinary JSON is still a rule set — `reversi.json` has
no comment in it at all — but the option is there, and the longer documents in `ruleset/` use
it for a header saying what the rule set is for and what it deliberately does not model.
Nowhere else can that be written down.

Three things are worth taking from that skeleton before anything else.

**Most of it is optional, and that is why a misspelled key is refused.** Suppose you write
`whn` where you meant `when`. Since `when` is optional, a lenient reader would see an input
with no `when` at all — and an input with no `when` is one that is allowed in every state.
The document would not fail; it would quietly mean something else, and nothing downstream
could tell it from a rule set that meant it. So the core fixes the key set at every position
it opens itself, and a key it does not recognise is a typo by definition. The table of those
positions is [the keys the core reads](runtime.md#the-keys-the-core-reads).

**One level down, the rule reverses.** Everything nested below those reserved keys is a
*node* — an object with an `op` in it ([§3](#3-nodes)) — and a node's keys belong to whichever
plugin the `op` came from, not to the core. There the core checks nothing, and it could not:
a core with opinions about a node's keys would make adding an argument to an operation a
change to the runtime itself.

So there are two régimes, and knowing which one you are in tells you whether a typo will be
caught:

| Where you are | Who owns the keys | A key nobody knows |
| --- | --- | --- |
| the ten reserved keys, and the objects the core opens inside them | the core | refused, with the path to it |
| anywhere inside a node | the plugin that provides that `op` | that plugin's business |

**`$schema` is a label, and the runtime never reads it.** It is not fetched, not validated
against, and not required to say anything in particular — you may leave it out. It is there
so that an editor has something to key off. What actually identifies the document is `id` and
`version`: those are what a state document names when it says which rule set it belongs to
([§11](#11-the-three-documents-that-travel)), and what a `uses` entry names when one rule set
holds another ([§10](#10-uses-and-held--a-rule-set-made-of-rule-sets)).

### The smallest complete document

```jsonc
{
  "$schema": "rulealize/ruleset/v1",
  "id": "light",
  "version": "1.0.0",

  // Four vocabularies, because this document uses four things: a type for the field,
  // reading and writing the state, `logic.not`, and the `@` that names a binding.
  "requires": [
    { "plugin": "Rulealize.Plugin.TypeSchema", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.State",      "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Logic",      "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Binding",    "version": "^1.0" }
  ],

  "state": {
    "schema":  { "on": { "op": "type.bool" } },   // one field, and it holds true or false
    "initial": { "on": false }                    // where a new one starts
  },

  "inputs": {
    "toggle": {                                   // no `when`, so it is always allowed
      "effects": [
        // Read `on`, call it `v`, and write back the opposite.
        { "op": "state.update", "path": "on", "as": "v",
          "value": { "op": "logic.not", "value": "@v" } }
      ]
    }
  }
}
```

That one runs:

```console
$ rulealize restore light.json
  …
4 plugins -> plugin
'light.json' compiles against it.
```

It is about as small as one gets. Take the single field out and it stops being a rule set at
all, because a schema declaring no fields is refused ([§5](#5-state--what-a-position-is)).

---

## 2. Values

Everything a rule set computes is a value, and there are seven kinds.

| Kind | What it is | As a JSON literal |
| --- | --- | --- |
| `Null` | the absence of a value | `null` |
| `Boolean` | a truth value | `true` / `false` |
| `Number` | a number; integers and fractions are not distinguished | `42`, `1.5` |
| `Text` | a string | `"black"` |
| `Sequence` | a finite ordered run of values | — |
| `Record` | a map keyed by strings | `{ "a": 1 }`, when it carries no `op` |
| `Opaque` | a plugin's own value — a coordinate, a direction | — |

`Sequence` and `Opaque` have no JSON literal. A sequence can only arrive as the result of
evaluating a node, which is why `{ "op": "seq.of", "of": [1, 2, 3] }` exists and why
`[1, 2, 3]` written where an expression belongs is not a sequence.

An `Opaque` value belongs to one plugin and means nothing to the core. It carries a type tag
such as `grid/coord`, and two opaque values with different tags are never equal. Some of them
have a **canonical text form** — `"d3"` for a coordinate — and that form is how they travel
in and out of documents. §11 is where that matters.

**Equality.** Values of different kinds are never equal, so `1` and `"1"` are not. `Null`
equals `Null`. Numbers compare numerically, so `1` equals `1.0`. Sequences compare element by
element, including length and order. Records compare by key set and then value by value.
Opaque values compare by type tag and then by whatever the defining plugin says.

**Null.** There is no blanket rule, and the policy is worth memorising, because rule sets are
built on it:

| Situation | What happens |
| --- | --- |
| a lookup where "not there" is a normal answer | `Null` — a square off the board, an index past the end of a sequence |
| equality | `Null` is just another value; `cmp.eq(null, "black")` is `false` |
| ordering and arithmetic | an evaluation fault |

That is what lets a rule be written without a boundary check. Reversi asks for the square
after a run of opponent stones, the run reaches the edge, and the chain

```
seq.elementAt(out of range) → null → grid.at(coord: null) → null → cmp.eq(null, "black") → false
```

drops it into the `else` branch with nothing written to catch it. The chain is fixed. Lean on
it. Each operation states its own null behaviour, in its own specification.

---

## 3. Nodes

Once you are below the ten reserved keys, the document is made of one thing repeated: a
**node**. A node is a JSON object with an `op` in it.

```jsonc
{ "op": "cmp.eq", "left": "$stage", "right": "draft" }
```

**`op` is short for *operation*, and its value is the operation's name.** It is the verb of
the node: `cmp.eq` means *are these two equal*, and the object's other keys — `left` and
`right` here — are what that operation takes. Read the node above as a call, written as an
object instead of as `eq($stage, "draft")`, because a rule set is data rather than code.

A name has two parts, separated by a dot:

```
cmp . eq
 │     └── the operation: one of the things that vocabulary can do
 └──────── the namespace: which vocabulary it belongs to
```

`cmp` is the namespace claimed by `Rulealize.Plugin.Comparison`, and that is the connection
between an `op` and the `requires` list — the namespace tells you which entry of `requires`
had to be there for this node to be legal. [§14](#14-reading-a-vocabulary-you-have-not-met)
turns that into a procedure for an `op` you have never seen.

Nodes nest. Wherever an operation takes a value, you may write another node instead of a
literal, as deep as the rule needs:

```jsonc
{ "op": "logic.and", "all": [
    { "op": "cmp.eq",     "left": "$stage", "right": "review" },
    { "op": "cmp.isNull", "value": "$reason" }
] }
```

**The core does not know what any of this means.** It reads `op`, finds which plugin claimed
that namespace, and hands the object over. It never learns what `cmp.eq` compares. That is why
the vocabulary can grow without the runtime changing, and why [§4](#4-requires--what-the-document-may-say)
matters as much as it does.

### Three kinds of node

Not every node may go anywhere. Each operation is one of three kinds, and the kind decides
where it is allowed to be written. The positions named in the third column are all introduced
later; the section is given for each so you can look ahead if one is unfamiliar.

| Kind | Produces | May appear in |
| --- | --- | --- |
| **expression** | a value | an input's `when` and `actor`, the arguments of an effect, a parameter's `domain` ([§6](#6-inputs--what-may-be-done)), a definition body ([§7](#7-definitions--naming-an-expression)), `terminal` ([§8](#8-terminal--when-it-is-over)) |
| **effect** | a write to the state | only the elements of an input's `effects` ([§6](#6-inputs--what-may-be-done)) |
| **schema** | the type of one state field | only inside `state.schema` ([§5](#5-state--what-a-position-is)) |

You can usually tell which kind an operation is from its name once you have seen a few:
`cmp.eq` computes something, so it is an expression; `state.set` writes, so it is an effect;
`type.bool` describes a field, so it is a schema node. When in doubt, the vocabulary's
specification states the kind for every operation it provides.

A node in a position its kind does not allow is refused when the document is compiled, never
at run time:

```
/inputs/submit/effects[0]: 'cmp.eq' is an expression and cannot appear where an effect is expected.
```

Three kinds of node, **four** kinds of operation. A plugin may also register a **draw**, which
builds an expression node like anything else that produces a value; what its kind settles is
where it may be written. §9 is the rest of it.

### A literal is an expression

Anywhere an expression belongs, a JSON literal stands for itself.

```jsonc
{ "op": "cmp.eq", "left": "$stage", "right": "draft" }   // "draft" is a Text expression
{ "op": "math.add", "of": ["$passes", 1] }               // 1 is a Number expression
"when": false                                            // a Boolean expression
```

An object carrying no `op` is a `Record` literal, and its values are expressions:

```jsonc
{ "shift": "@shift", "who": "@who" }   // a record of two fields, both computed
```

An array is never a value. Where an array appears it is because the node's key takes a list of
nodes — `logic.and`'s `all`, `math.add`'s `of` — and not because an array is how a sequence is
written.

### The three shorthands

A plugin may reserve a leading character and expand string literals that begin with it into
nodes of its own. Three are in use:

```jsonc
"$board"    // = { "op": "state.get",  "path": "board" }    Rulealize.Plugin.State
"@at"       // = { "op": "bind.local", "name": "at" }       Rulealize.Plugin.Binding
"#opponent" // = { "op": "def.ref",    "name": "opponent" } Rulealize.Plugin.Definition
```

They are ordinary vocabulary and are declared like anything else — a document that writes
`@at` and does not require `Rulealize.Plugin.Binding` is refused.

**A character is recorded, not owned.** Two vocabularies may reserve `$`, and where a rule set
requires both, the bare form is ambiguous and is refused with the candidates named. The
qualified form says which was meant:

```jsonc
"$state:board"   // Rulealize.Plugin.State, whoever else reserved `$`
```

The qualifier is read by its own grammar — a namespace and a colon — and never by what a
plugin folder happens to hold, so what a document means does not change when a plugin is
installed beside it.

There is no way to write a literal string that begins with a reserved character, and none is
provided. The hole is narrower than it looks: only a literal *in the rule set document* is
expanded, so text arriving in a state document, a key in `branch.match`'s `cases`, and
anything computed at run time are all unaffected.

### Reading a form

A specification writes an operation's shape as a **form** — the JSON that names the node, with
every value replaced by what may stand there.

```jsonc
{ "op": "seq.count", "source": <expression:Sequence>, "as": "<name>", "where": <expression:Boolean> }
```

`<expression>` is any expression node, or a literal evaluated as one.
`<expression:Sequence>` narrows it to a kind of the value model, capitalised.
`<expression:coord>` — lowercase — narrows it to an opaque value carrying that tag.
`"<name>"` is a literal string in the role the placeholder names. A trailing comment saying
`static` means the key takes a literal rather than an expression and is read when the document
is compiled; `optional` means it may be omitted, and the prose says what omitting it means.
The convention in full is
[how a plugin specification is written](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/specification-notation.md).

---

## 4. `requires` — what the document may say

```jsonc
"requires": [
  { "plugin": "Rulealize.Plugin.TypeSchema", "version": "^1.0" },
  { "plugin": "Rulealize.Plugin.State",      "version": "^1.0" },
  { "plugin": "Rulealize.Plugin.Comparison", "version": "^1.0" },
  { "plugin": "Rulealize.Plugin.Logic",      "version": "^1.0" },
  { "plugin": "Rulealize.Plugin.Sequence",   "version": "^1.0" },
  { "plugin": "Rulealize.Plugin.Binding",    "version": "^1.0" }
]
```

An entry takes `plugin` and `version`, and a constraint omitted means any version will do.

| Written | Means |
| --- | --- |
| `"^1.0"` | any `1.x` at or above `1.0` |
| `">=1.0"` | any version at or above `1.0` |
| `"1.0.1"` | exactly that version |

The `^` form reads the major version, and it is worth knowing what that rests on: a plugin
removing an operation, or changing what one means, is obliged to release a new major.
Constraints are only worth writing because the vocabularies keep that bargain.

**This list is the whole of what the rule set may draw on.** An `op` is looked up only among
the plugins the document named, and so is a shorthand character. Reaching a vocabulary that
happens to be loaded and was not declared is a build error naming the plugin it came from —
which is what makes `requires` worth reading rather than an approximation of what a document
needs. Without the rule, a document compiles wherever its undeclared vocabulary is loaded and
fails wherever it is not: a fault with no symptom until the document is moved.

Try it on the `light` document from [§1](#1-the-shape-of-the-whole-document). Delete the
`Rulealize.Plugin.Logic` line from its `requires` and leave everything else alone — the plugin
is still sitting in the `plugin` folder, and the runtime still refuses the document:

```console
$ rulealize check noLogic.json
'noLogic.json' does not compile against 'plugin':
  /inputs/toggle/effects[0]/value: 'logic.not' comes from Rulealize.Plugin.Logic, which this rule set does not name in 'requires'.
```

Put that line back and take out `Rulealize.Plugin.Binding` instead, and the same rule catches
the shorthand `@`, which is vocabulary just as much as an `op` is:

```console
$ rulealize check noBind.json
'noBind.json' does not compile against 'plugin':
  /inputs/toggle/effects[0]/value/value: '@' is a shorthand this rule set does not require. 'bind' is the one vocabulary that does.
```

Both messages name what you would have to add. Neither depends on what happens to be in the
`plugin` folder — the folder still has all four in it throughout.

**It is also the dependency list.** `rulealize restore light.json` reads exactly this, fetches
what it names into a `plugin` folder, and compiles the document against what it just wrote —
along with whatever `uses` names (§10), which it fetches the same way. Nothing is written out a
second time, because the runtime already had to read this in order to refuse a document it
cannot run.

**Cut your requirements finely and the list starts saying something.** A rule set that
requires `Rulealize.Plugin.Arithmetic` is one that counts something. A rule set with no
`Rulealize.Plugin.Chance` in it has no chance in it, and that is checkable rather than
believed.

A vocabulary an application keeps to itself is declared here identically. `Acme.Deploy.Rules`
sits in [`ruleset/deploy.json`](../ruleset/deploy.json) beside the published ones, and a
runtime without it refuses the document with that name in the message. Nothing in the document
says which route a plugin arrived by.

---

## 5. `state` — what a position is

```jsonc
"state": {
  "schema": {
    "stage":  { "op": "type.enum", "values": ["draft", "review", "approved", "rejected"] },
    "reason": { "op": "type.enum", "values": ["scope", "cost", "timing"], "nullable": true }
  },
  "initial": { "stage": "draft", "reason": null }
}
```

Both keys are required. A schema declaring no fields is refused: a state document is a public
interface, so there has to be something to check one against. `initial` gives one value per
declared field, every field, and no field the schema did not declare.

### A field is the unit

`state.schema` is a flat map of field name to **schema node**. There is no nesting at this
level and a path never reaches inside a field — `"board.d3"` is not a path, and no dotted
syntax is reserved.

That is not a limitation so much as the seam the whole decomposition turns on. A path names a
whole field and is written out literally rather than computed, and three things follow:

- every path in the document can be checked against the schema when it is compiled;
- which field an input writes can be read off the document without running it;
- schema validation does not have to wait until run time.

The inside of a field holding a board, a record or a list is reached with the vocabulary of
the plugin that declared it — `grid.at` and `grid.set` for a board, `rec.at` and `rec.with`
for a record, the `seq.*` family for a list. §14 is how to find which.

Nesting happens *inside* a field, and it is as deep as you like:

```jsonc
"seats": {
  "op": "type.list",
  "element": {
    "op": "rec.of",
    "fields": {
      "id":    { "op": "type.string", "minLength": 1 },
      "bet":   { "op": "type.int", "min": 1 },
      "cards": { "op": "type.list",
                 "element": { "op": "type.enum", "values": ["A", "2", "3", "T", "J", "Q", "K"] } },
      "stood": { "op": "type.bool" }
    }
  }
}
```

### A schema node decides the JSON too

How a field's value becomes JSON is the schema node's business and nobody else's. A board
declared by `grid.board` serializes as a sparse coordinate map because Grid says so:

```jsonc
"board": { "d4": "white", "e4": "black", "d5": "black", "e5": "white" }
```

Changing that to a dense array would touch one file in that plugin and nothing else — not the
core, not `state.get`, not any rule set that reads a square.

### When the schema is checked

| What | When |
| --- | --- |
| `state.initial` | when the document is compiled |
| a state document arriving from outside | on entry to `ApplyToState`, `GetValidInputs`, `GetOutcomes`, `GetTerminalStatus` |
| what an input's effects built | when the transition commits |

The third is worth knowing about before you write a guard. A rule set whose effects can
assemble a state the schema forbids is told so at the transition responsible, with the input
named, rather than handing that state back and failing on the next read. Only fields an input
actually wrote are rechecked; anything untouched has been through this once already.

A violation names the path, the value and the constraint it broke, and every violation is
reported rather than the first:

```
The state does not satisfy state.schema.
  stage: Expected one of draft, review, approved, rejected but got "shipped".
  reason: Expected one of scope, cost, timing but got "vibes".
```

### Declaring a constraint that the rules also enforce

Reversi's `passes` is `{ "op": "type.int", "min": 0, "max": 2 }`, and `terminal.when` already
ends the game at two. The redundancy is deliberate and the two say different things: a
`terminal` is a **rule about transitions** — reaching 2 ends it — while a schema bound is a
**constraint on the state space**: no state with 3 exists. The second is what catches
`passes: 5` arriving from outside. Write both.

---

## 6. `inputs` — what may be done

An input is a named thing somebody may do. It takes five keys and all but one are optional.

```jsonc
"inputs": {
  "reject": {
    "params":  { /* what it may be called with */ },
    "actor":   /* whose move this is */,
    "when":    /* whether it is allowed */,
    "effects": [ /* what it does */ ],
    "fires":   [ /* component inputs it drives — §10 */ ]
  }
}
```

`effects` is required unless `fires` is written. An input's name may not contain `.`; the
character is reserved for the qualified names a composite offers (§10).

### `params` — a domain, and what comes of it

```jsonc
"reject": {
  "params": { "reason": { "domain": { "op": "seq.of", "of": ["scope", "cost", "timing"] } } },
  "when": { "op": "cmp.eq", "left": "$stage", "right": "review" },
  "effects": [
    { "op": "state.set", "path": "stage",  "value": "rejected" },
    { "op": "state.set", "path": "reason", "value": "@reason" }
  ]
}
```

A parameter is a name and a `domain`, and the domain is an expression returning a `Sequence`.
`GetValidInputs` takes the product of an input's parameter domains and sifts it with the
guard, so this one input becomes three legal moves in the `review` stage:

```
draft      submit
review     approve, reject(reason: scope), reject(reason: cost), reject(reason: timing)
rejected   —   terminal, result: rejected
```

**A domain is part of the rules, not a hint for the search.** `ApplyToState` resolves each
argument against the domain too, and refuses a value the domain does not produce. So a rule
may be stated in a domain or in a guard, whichever you prefer, and moving work into the domain
does not owe a second copy of that rule in `when`.

A domain may read the state — roster's `who` domain is the names in `$staff`, so a different
week is a different state document and the same rule set — and it is walked once per call
rather than once per candidate. It may not contain a draw (§9).

The value bound to `@reason` is **the value the domain produced**, whichever route the move
arrived by. That matters when a domain produces opaque values: `GetValidInputs` writes a
coordinate out as `"d3"`, an input document brings `"d3"` back, and the text is matched against
the domain so that the expression downstream sees the coordinate and not its spelling.

`"params": {}` and no `params` key mean the same thing.

### `when` — the guard

An expression returning a `Boolean`, evaluated once per candidate. Omitted, the input is legal
in every state.

```jsonc
"when": { "op": "def.call", "def": "canPlace", "args": { "at": "@at" } }
```

The parameters are in scope, under the names `params` gave them. So is the state, and so are
the definitions.

`GetValidInputs` does not consult `terminal`. So if your guards stay satisfiable after the
thing is over, moves go on being listed, and nothing in the runtime will stop that — it is
yours to arrange either way. The roster wants it that way: a caller who has filled the week
into a corner still needs `release` offered, or there is no backing out.

### `actor` — whose move this is

An expression evaluated per candidate, beside the guard. Its canonical text is what a caller
reads as `ValidInput.Actor`.

```jsonc
"place": { "actor": "#me", /* … */ }
```

**Nothing consults it.** Entitlement to move is `when`'s question, and the runtime never
second-guesses it here. What `actor` buys is a place to filter and label by, declared once in
the document instead of separately in every host that reads it — which is why two hosts over
one rule set cannot disagree about whose turn it is.

Leave it out where there is no turn, and every `Actor` comes back null. The roster does exactly
that, and a schedule is not thereby a smaller kind of game.

### `effects` — what it does

A list of **effect** nodes, and the only place one may appear.

```jsonc
"effects": [
  { "op": "grid.set", "target": "$board", "coord": "@at", "value": "#me" },
  { "op": "grid.setMany", "target": "$board",
    "coords": { "op": "def.call", "def": "flips", "args": { "at": "@at" } },
    "value": "#me" },
  { "op": "state.set", "path": "passes", "value": 0 },
  { "op": "state.set", "path": "turn", "value": "#opponent" }
]
```

**Snapshot semantics is the one rule to internalise.** The state as the input found it is
fixed as a read-only snapshot; every expression in every element of `effects` is evaluated
against *that*; writes accumulate in a draft and are applied together when the last element has
run.

Read that list again against the Reversi effects above. `#opponent` derives from `$turn`, and
it is evaluated against the snapshot, so it is the opponent of the colour that moved even
though a later element writes `turn`. `flips` rescans the board *as it was*, so putting the
stone down first does not change what the second element computes. The effects can be written
in the order a person would describe the move, which is what they are for.

Two further clauses:

- **Where two writes land on one path, the last one wins.**
- **An effect that needs to build on what an earlier effect wrote reads the field back from the
  draft.** Both halves are in play at once when two effects edit one board: the second starts
  from a board that already has the new stone on it, while the expressions inside it still
  compute from the position as it stood before the move.

A path is written out literally. `state.set` takes `"path": "stage"`; `grid.set`, `rec.set` and
their kind take a **state field** written as `"$board"` and resolved when the document is
compiled. Neither can be assembled by an expression, and that is what buys the checks in §5.

---

## 7. `definitions` — naming an expression

```jsonc
"definitions": {
  "me":        { "op": "state.get", "path": "turn" },
  "maxPasses": 2,
  "opponent":  { "op": "branch.match", "value": "#me",
                 "cases": { "black": "white", "white": "black" } },

  "half": { "params": ["n"], "body": { "op": "math.div", "left": "@n", "right": 2 } }
}
```

An entry is read one of two ways. Write `body` and the core also reads `params`; write
anything else and **the value is the body**. So a definition can be a node, a record, or a
plain named constant, and only the ones taking parameters need the longer form.

Referring to one is `#name`; calling one is `def.call`:

```jsonc
{ "op": "def.call", "def": "flips1", "args": { "at": "@at", "dir": "@d" } }
```

An argument list that does not match the definition's parameters is a build error, as is an
undefined name and a cycle between definitions.

Three properties decide what definitions are good for.

**Bodies are hygienic.** A body sees the state, the other definitions, and its own parameters,
and nothing from wherever it was called. `args` is the only way in, and that is what fixes a
definition's meaning independently of its callers.

**Bodies are pure, and results are cached** against the definition, its arguments and the
snapshot, for as long as that snapshot lasts. Over a `GetValidInputs` sweep that is not a
micro-optimisation: Reversi's capture computation is reached from a guard and again from the
effect that follows it, with the same coordinate, for each of sixty-four candidates, and each
evaluation walks eight rays.

**Recursion is refused.** Termination could not be guaranteed otherwise, and with the call
graph fixed the cost of an evaluation has an upper bound that can be estimated.

The core holds a definition as a name, a parameter list and a body, and never evaluates one.
A plugin supplies the vocabulary for referring to one and calling one, which is why a rule set
that defines nothing need not load `Rulealize.Plugin.Definition`.

### Definitions versus local bindings

`bind.let` introduces names for the length of one expression, and the bindings may refer to
earlier ones in the same block:

```jsonc
{
  "op": "bind.let",
  "bind": {
    "ray": { "op": "grid.ray", "grid": "$board", "from": "@at", "dir": "@dir" },
    "run": { "op": "seq.takeWhile", "source": "@ray", "as": "c", "predicate": /* … */ }
  },
  "in": { /* … uses @ray and @run … */ }
}
```

Use a definition for something the document names in more than one place, or that wants
caching. Use a binding for something computed once and used twice inside one expression. The
iterating nodes — `seq.where`, `seq.any`, `seq.count` and the rest — introduce a binding of
their own with `as`, visible only inside that node's predicate or projection, and an inner
binding shadows an outer one of the same name.

Scope, in one table:

| Layer | Referred to as | Visible |
| --- | --- | --- |
| state | `$path` | everywhere |
| definitions | `#name` | everywhere |
| local bindings | `@name` | only in the part of the introducing node that says so |

---

## 8. `terminal` — when it is over

```jsonc
"terminal": {
  "when": {
    "op": "logic.or",
    "any": [
      { "op": "cmp.eq", "left": "$stage", "right": "approved" },
      { "op": "cmp.eq", "left": "$stage", "right": "rejected" }
    ]
  },
  "result": "$stage"
}
```

The section is optional. `when` is required once it is written; `result` is not, and
`TerminalStatus.Result` is null where a rule set declares none.

`result` is evaluated **only once `when` holds**, so a rule set is entitled to leave it
undefined — or faulting — mid-game. Reversi counts stones there and would divide a
half-finished board into a winner if it were asked early; it is not asked early.

A rule set with no `terminal` at all never reports one, which is an ordinary shape rather than
an omission. A process with no end has nothing to write there, and a caller that stops when
nothing is legal never asks.

The result is not a winner, either. The roster's is `complete` or `stuck`; the approval's is
the stage it settled in.

---

## 9. Chance

Not every rule set settles its next state from the input alone. A card comes off a deck, a die
lands: something happens that nobody chose. A rule set says so with a **draw**.

```jsonc
"hit": {
  "actor": "#acting",
  "when": { "op": "cmp.eq", "left": "#phase", "right": "play" },
  "effects": [
    { "op": "state.set", "path": "seats", "value": {
        "op": "def.call", "def": "seatsWith", "args": {
          "id": "#acting",
          "seat": { "op": "def.call", "def": "dealt", "args": {
              "seat": "#actingSeat",
              "card": { "op": "chance.pick", "of": "#ranks", "as": "r",
                        "weight": { "op": "def.call", "def": "left", "args": { "rank": "@r" } } } } } } } }
  ]
}
```

`hit` is one decision, so `GetValidInputs` lists it once. The thirteen ranks that could arrive
are not a decision, so they are not moves — `GetOutcomes` enumerates them, with a probability
on each, and each outcome carries the state it leads to.

A traversal is therefore the two calls in that order, and it is the same two calls for a rule
set with no chance in it: an input that draws nothing has exactly one outcome, of probability
one. Nothing in a caller's code says whether chance is involved.

### Where a draw may be written

**Inside an input's `effects`, at any depth, and nowhere else.** Refused in `when`, `actor`,
`params[].domain`, `terminal`, and a definition body — checked when the document is compiled,
with a pointer to the node. Every one of those is a position the runtime evaluates while it is
sifting candidates or memoizing a result:

| | |
| --- | --- |
| a guard | evaluated once per candidate, with no outcome to be drawing for |
| a domain | enumerated to form candidates and walked again to resolve an argument — a domain that drew would refuse the move it had just offered |
| `terminal` | asked about a state, and whether a game is over is not a coin toss |
| a definition body | memoized against its arguments and the snapshot, so a body that drew would answer its first caller and repeat itself to every other one |

### A drawn value is used once, where it is drawn

You cannot refer back to a drawn value from a later effect, and the reason is §6 rather than
anything about draws: every effect reads the snapshot, so a second one naming that draw
evaluates the node again and gets a different card. The node draws once per time control
reaches it — which is also why a draw inside a projection over three seats is three draws and
three entries in the outcome.

**So an input that draws is an input with one effect that uses what it drew.** When a rule set
seems to need two, look for a field that is storing something the state already says. That is
what blackjack found: "take a card out of the deck" and "put it in a hand" would have to be two
effects, so the deck is not a field at all — it is thirteen ranks, four of each, less whatever
is already face up, and the hands say what that is. Deriving it is not a workaround for the
one-effect rule; it is the shape the rule was pointing at.

### What the runtime guarantees

- **Nothing is generated.** A draw works out what could come out and how likely each of those
  is, and asks the runtime for one. Sampling belongs to the host, which is why a recorded
  transition replays to the state it was recorded against.
- **Outcomes come back in descending order of probability**, and ties break by arrival, so two
  runs of one search agree.
- **Anything the rules allow has at least one outcome.** `GetOutcomes` returns outcomes or
  throws, so a traversal never needs a branch for the empty answer.
- **A draw with nothing to draw from is a fault**, not an absence of outcomes: it means a guard
  did not say the source could be empty. A candidate whose weight is zero is a different matter
  and simply does not appear.
- **A truncated `OutcomeSet` is a distribution that no longer sums to one**, which is why
  `Coverage` reports how much of the probability the outcomes account for. That is a different
  quantity from a truncated `ValidInputSet`, which is still a set of legal moves with every
  entry right.

The rest — how the branches are found, what `Evaluated` counts, why the limit counts outcomes
rather than evaluations — is
[what may happen](runtime.md#what-may-happen-draws-and-getoutcomes).

---

## 10. `uses` and `held` — a rule set made of rule sets

Take *to put somebody on a shift, a request has to be raised and granted*. Anyone would write
that as two documents, and then discover that the rule they actually care about fits in
neither: the request document cannot see the roster, so it will happily raise a request for a
shift already filled, and the roster document has never heard of a request, so it is not wrong
about anything. Neither is a bad document. A definition does not rescue it either, since what
is needed is a guard reading *both states at the same moment*.

`uses` names the rule sets a document holds. `held` is what it may say about them.

```jsonc
"uses": [
  { "ruleSet": "Rulealize.RuleSet.Request",    "version": "^1.0", "as": "req"  },
  { "ruleSet": "Rulealize.RuleSet.Signatures", "version": "^1.0", "as": "sigs" }
],

"held": {
  "req": {
    // Nothing is granted unsigned. One sentence, because it is a sentence about two
    // states at once.
    "grant": {
      "when": { "op": "cmp.eq", "right": "signed",
                "left": { "op": "rec.at", "record": "$sigs", "key": "outcome" } }
    }
  }
}
```

`ruleSet` is required; `as` defaults to it and may not contain `.`. One `uses` entry is one
component holding one state, and a rule set may be named more than once under a different
alias each time.

What follows is the whole of what composition is, rule by rule.

**A held rule set's state is a field.** `uses` declares it — it is not written in
`state.schema` and takes no value in `state.initial`, because it opens where the component
opens. What sits in that field is a record keyed by the component's own field names, so you
read into it with `rec.at` — composition needed no new vocabulary in the core at all. A
composite's case is therefore still one state document: one string, one column, resumable
anywhere.

**A held rule set's inputs are offered as `alias.input`** — in `RuleContext.Inputs`, in what
`GetValidInputs` hands back, and in the `input` of an input document. Same string, same
document, so replay and recording need nothing new. That is why an input's own name may not
contain `.`.

**`held` may only refuse.** Its `when` is evaluated in the composite, over the whole composed
state, with the component input's parameters in scope under the names the component gave them,
and it is asked *after* the component's own guard. So `"when": false` hides an input, and
nothing written there can grant one. That is what lets a rule set be published on its own and
true things said about it without knowing who will hold it.

**A composite has no other way into a component's state.** The field has to be in the
composite's schema for `$req` to read it at all, which means an effect against it *is*
writable — and it is refused when it runs, at any depth. Do not plan on reaching in.

**`fires` drives several inputs as one decision.** Narrowing alone models a longer process than
the one being run: *a request is granted* and *somebody goes on the shift* are one decision,
and as two inputs the composite has a state between them that the process never occupies.

```jsonc
"held": {
  "req": { "grant": { "when": false } }    // hidden: the only route to it is what drives it
},

"inputs": {
  "fill": {
    "fires": [ { "held": "req", "input": "grant" } ],
    "effects": [ /* the composite's own writes */ ]
  }
}
```

`fires` is a static list and not an effect — it may not sit inside a branch — so which
component inputs an input drives is readable without running it. Each one it drives is then put
to its own rule set twice over — the argument has to be something that component's domain
produces, and that component's guard has to accept it — so firing is never a way round a rule
the component wrote, and `ApplyToState` still refuses exactly what `GetValidInputs` declined to
list. What runs is the component's own effects, and then the
composite's; all of them read the state the transition found, so none can depend on another's
writes. `held` is not asked here: `held` is when the composite *offers* a component's input,
and `fires` is the composite taking it having already decided.

**Do not walk a composite.** Its reachable set multiplies: hold two components of two hundred
states each and you have forty thousand. You almost never need to. Anything you want to know
about one component's state — which of its inputs were ever legal, which endings it reached —
is a question about that component, answerable at the sum rather than the product. Only a
property that spans components needs the composite walked, and those are exactly the few that
`held` and `fires` are about.

`CreateContext(document, held)` takes the document of every rule set reachable through `uses`,
by identifier. The rest — where those documents come from, how a version is chosen, what a
stored composite state carries, and what a composite deliberately does not get — is
[`uses` and `held`](runtime.md#uses-and-held-a-rule-set-that-holds-others).

---

## 11. The three documents that travel

Four documents in all: `rulealize/ruleset/v1`, which is everything above, and three that
travel per call. The core fixes only the frame.

```jsonc
// rulealize/state/v1 — where things stand
{ "$schema": "rulealize/state/v1", "ruleSet": "reversi@1.0.0",
  "data": { "board": { "d4": "white" /* … */ }, "turn": "black", "passes": 0 } }

// rulealize/input/v1 — what somebody decided
{ "$schema": "rulealize/input/v1", "ruleSet": "reversi@1.0.0",
  "input": "place", "args": { "at": "d3" } }

// rulealize/outcome/v1 — what the world did about it
{ "$schema": "rulealize/outcome/v1", "ruleSet": "blackjack@1.0.0",
  "input": "hit", "draws": ["9"] }
```

The keys directly under `data` are the fields of `state.schema`, and how each becomes JSON is
decided by the schema node that declared it.

The third is needed only by a rule set with chance in it, and an outcome with no draws means
the same thing as not passing one — so a caller logging every transition as an input and an
outcome writes the same pair either way.

**The frame is checked the way a rule set's own sections are**: a key that is not one of the
three or four above is refused. That every one of them but the payload is optional is the
reason. Misspell `ruleSet` as `ruleSt` and you do not get a failure; you get a document nobody
checked the identity of, carrying a position that now belongs to whichever rule set picked it
up.

**What `$schema` says is not read.** Which rule set a document is for is `ruleSet`'s to say,
and a document naming neither is read against the schema like any other: declining to claim an
identity is not the same as claiming the wrong one, and a state written by hand has no reason
to be forced into one.

**A `ruleSet` matches on identifier and major version.** `reversi@1.0.0` and `reversi@1.4.2`
are interchangeable; `reversi@2.0.0` is not. Anything a revision did to the shape of the state
is the schema's business rather than the version's — and the schema check names the field,
which is a better diagnostic than a version mismatch that names none of them.

### What an argument may be

A move that came out of `GetValidInputs` has to mean the same move when it goes straight back
into `ApplyToState`, and that round trip is what decides the spelling. Each argument keeps its
own JSON form — numbers stay numbers, booleans stay booleans. Text is reserved for values JSON
has no form for: a coordinate, a direction, anything opaque. On the way back the text is
matched against the domain, and what gets bound is the value the domain produced, not the
spelling of it.

The boundary makes one concession and stops there. `"2"` will not match `2`: kinds are unequal
in the value model (§2), and quietly relaxing that at the edge of the system is the worst place
to relax it.

**A value with neither a JSON form nor a canonical text form cannot be an input argument at
all.** A record is the case that comes up: a domain returning one fails when the argument is
resolved. A compound argument is a tuple instead, and `tuple.of` exists for exactly that — a
chess move travels as `"e2|e4|-"`.

A drawn value makes the same trip on the same terms.

---

## 12. Reading an error

**Everything the document can settle on its own is settled when it is compiled**, and the
message carries the path of the offending node:

```
/inputs/submit/when/left: 'stagee' is not a field of the state schema.
/inputs/reject/effects[0]/path: 'staeg' is not a field of the state schema.
/inputs/submit/effects[0]: 'cmp.eq' is an expression and cannot appear where an effect is expected.
/inputs/submit/whn: is not a key an input takes; those are 'params', 'actor', 'when', 'effects' and 'fires'.
```

**That path is the runtime's own, and it is not a JSON pointer.** A member is `/name` and an
array element is `[n]`, so `/inputs/reject/effects[0]/path` reaches the `path` member of the
first effect — where a JSON pointer would be reading a member called `effects[0]`, which
nothing has. Parsing one is splitting on `/` and peeling `[n]` suffixes off each segment. A
tool doing that also has to fall back to the nearest ancestor that exists, because a refusal
routinely names a member the document does not have: the last line above is a misspelt key,
and the misspelling is the whole complaint.

The list of what is caught there is long, and the reason it is long is `GetValidInputs`: it
evaluates a guard against every candidate in a domain, and a fault that first appears on the
forty-first candidate is a fault that reaches production.

Refused when the document is compiled:

- an unknown operation, or one belonging to a vocabulary the document did not declare
- a key the core does not read, at every position it opens itself
- a missing required key, or an expression where a static value belongs
- a node in a position its kind does not allow, and a draw outside an input's effects
- a local nothing declared; an undefined definition; a cycle between definitions
- an argument list that does not match a definition's parameters
- a state path that is not in the schema, and a `state.initial` that does not satisfy it
- a cycle between held rule sets, and a held document whose `id` or `version` is not the one
  `uses` named

What is left to fail during evaluation is short:

- a value of the wrong kind; an ordering comparison against null; division by zero
- a `branch.match` with no matching case
- a draw with nothing to draw from, or a weight below zero
- a value with no text form where one has to be written down
- a set of effects that builds a state the schema forbids

Reading past the end of a sequence and reading a square off the board are **not** on that list.
They produce null, and rule sets are built on their doing so (§2).

Each exception says which class of thing went wrong: `RuleSetBuildException` for a document
that is not a valid rule set, `RuleDocumentException` for a state, input or outcome document
this rule set cannot accept, `IllegalInputException` for a move the rules do not allow,
`RuleEvaluationException` for values that make an operation meaningless, and
`PluginLoadException` for a set of plugins that cannot be used together.

---

## 13. Shaping a rule set

The frame is small and the judgement is not. These are the decisions the worked documents came
out of, each one written down where it was learned.

**The rule set is the domain; the state is the instance.** The roster's first draft declared
the staff as a `type.enum` of four names and the shifts as the keys of a `rec.map` — which made
the document describe one particular week. The version in the repository declares nothing about
who or what: staff, shifts and assignments are all lists, and which people exist arrives in the
state document. A board game hides the difference, because a chess board is always the same
eight by eight. Ask of every literal in `state.schema` whether it is a rule or an instance.

**A key set fixed by the instance is not a key set; it is a list.** That is what decides
between `rec.of`, `rec.map` and `type.list`. Shogi's hand is a `rec.map` over seven piece kinds
because the *rules* fix those seven. A table's seats are a `type.list` of `rec.of` because who
is at the table is not something the rules know.

**Do not store what the state already says.** Blackjack has no deck field: the deck is
fifty-two cards minus what has been dealt, and the hands already say what has been dealt. No
hand total is stored either; `seq.sum` reads it back out of the cards. A derived field is a
second copy of a fact, and `state.schema` has no way to check the two against each other.

**Put a rule in the domain when it keeps the candidate count down.** Candidates are the product
of an input's parameter domains. A chess move written as `from` and `to` is 64 × 64 = 4,096
candidates in every position, of which twenty are legal. Written as one parameter whose domain
computes destinations from origins, it is twenty candidates — one `tuple.of` per move, and a
`tuple` is what a compound argument has to be anyway, since a record cannot survive the round
trip. A caller can cap the work — `GetValidInputs` takes a `validationLimit`, and a result cut
short by it is a subset of what is legal rather than a wrong answer — but that is a caller
protecting itself, not a fix. The fix is in the document: narrow the domain before the guard
ever runs.

**Write the effects in the order a person would describe the move.** Snapshot semantics is what
makes that safe. Reach for reading the draft back only when an effect genuinely has to build on
an earlier one.

**One effect per draw.** §9. If a rule set seems to need the drawn value twice, look for the
redundant field first.

**Let null do the boundary checks.** Three stopping conditions — an empty square, one of mine,
the edge of the board — are one predicate in Reversi, because `grid.at` off the board and
`grid.at` on an empty square both answer `Null` and `cmp.eq` against `Null` is `false`.
Conflating them is deliberate, and it is what keeps the document short.

**Bound a list that grows.** State is serialized on every transition, so an unbounded history
grows the document with the square of the transition count. The roster's log is
`"maxLength": 5` and a definition truncates it on every write. The bound is usually the rule
rather than a compromise — the history chess needs for threefold repetition only reaches back
to the last irreversible move.

**Comment the document.** It is `jsonc` and the reason a rule is the way it is has nowhere else
to live. Every document in `ruleset/` opens with a paragraph saying what it is for and what it
deliberately does not model.

---

## 14. Reading a vocabulary you have not met

This guide contains no list of operations, and that is not an omission. What a rule set may say
is decided entirely by which plugins are loaded; a list here would be a second account of
something already recorded, kept in step by hand, and the two would eventually differ. The
first symptom of that is a reader writing an `op` against a table rather than against a runtime.

So here is the procedure instead. You have a document in front of you and it writes something
you do not recognise:

```jsonc
{ "op": "secret.waiting", "of": "$bids" }
```

1. **Split the `op`.** `secret` is a namespace; `waiting` is an operation in it.
2. **Read the document's `requires`.** Exactly one entry provides that namespace, because the
   runtime refuses two plugins claiming one namespace when they load. Here it is
   `Rulealize.Plugin.Secret`.
3. **Open that plugin's `doc/specification.md`,** in its own repository. A specification is
   released by the vocabulary rather than from any central place, because the versions move
   independently — which is the whole reason `requires` carries a constraint per plugin. It
   opens with a table carrying the identifier, the version, the namespace and the reserved
   prefix, and then gives every node its kind, its form, how it evaluates, what is static, and
   which of its faults are caught when.
4. **If you do not know which plugin claims a namespace**, or which shorthand characters are in
   use by whom, that is
   [Rulealize.Registry](https://github.com/reny-develop/Rulealize.Registry). Its catalogue is
   built by fetching each package, loading it and reading the operations back off the assembly
   — the same folder scan a deployed application performs — so what it says cannot disagree
   with what a runtime will do.
5. **Or ask the runtime you have.** `RuleRuntime.Plugins` says which vocabularies are loaded and
   `RuleRuntime.Operations` gives every operation they provide.

Every specification assumes two documents, and both live in `Rulealize.Abstraction` because
both describe types that package defines:
[the value model](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/value-model.md),
which is §2 and §3 of this guide stated normatively, and
[how a plugin specification is written](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/specification-notation.md),
which is the notation in "Reading a form" above.

And if the operation you want does not exist, writing one is not this repository's subject: a
plugin is written against `Rulealize.Abstraction` and never references Rulealize. The starting
point is [`Rulealize.Templates`](https://github.com/reny-develop/Rulealize.Templates), and
[vocabulary](plugin.md) is what decides whether the thing you want is a published plugin or an
instance your own application hands to `AddPlugin`.

---

## Where to go next

| | |
| --- | --- |
| [`ruleset/countdown.json`](../ruleset/countdown.json) | the smallest document here — one field, one input — and what [the quick guide](ruleset-quickguide.md) is built around |
| [`ruleset/approval.json`](../ruleset/approval.json) | the next smallest, and the first with a reader choosing between inputs — six plugins, three inputs, two fields |
| [`ruleset/reversi.json`](../ruleset/reversi.json) | the shortest complete game, and where the null-propagation idiom comes from |
| [`ruleset/roster.json`](../ruleset/roster.json) | no turn, no opponent, no board, and not one `grid.` operation |
| [`ruleset/blackjack.json`](../ruleset/blackjack.json) | chance, and a deck that is not in the state |
| [`ruleset/deploy.json`](../ruleset/deploy.json) | a vocabulary an application keeps to itself |
| [`ruleset/kitchen-sink.json`](../ruleset/kitchen-sink.json) | one of nearly everything, in one document |
| [the runtime's surface](runtime.md) | normative: what the library does with a rule set once it has one |
| [the README](../README.md) | getting one running — the API, the exceptions, loading plugins |
