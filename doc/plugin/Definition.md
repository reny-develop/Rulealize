# Rulealize.Plugin.Definition

| | |
| --- | --- |
| Identifier | `Rulealize.Plugin.Definition` |
| Namespace | `def` |
| Version | `1.0.0` |
| Reserved prefix | `#` |
| Depends on | [the value model](../value-model.md), and nothing else |

Refers to and applies the expressions a rule set writes in its `definitions` section.

**The `definitions` section itself is a reserved key of the core**, which holds each entry
as a name, a parameter list and a body — and never evaluates the body. Abstraction exposes
a way to resolve a definition through the evaluation context, and this plugin is what uses
it.

That split puts "a rule set has definitions" in the core and "a rule set calls one" in a
plugin, so a rule set that defines nothing need not load this at all.

## Nodes

| Node | Kind | Used in Reversi |
| --- | --- | --- |
| `def.ref` | expression | ○ as the sugar `#`, in `#me`, `#opponent`, `#hasAnyMove` |
| `def.call` | expression | ○ calling `flips1`, `flips`, `canPlace` |

---

## The form of the `definitions` section

Core structure, but inseparable from this plugin's meaning, so it is written down here.

```jsonc
"definitions": {
  "<name>": {
    "params": ["<name>", ...],   // optional, static
    "body": <expression>
  }
}
```

An entry with no `body` key is a **short form**, read as `{ "params": [], "body": <the
whole entry> }`. Reversi's `me` and `opponent` are written this way.

```jsonc
"definitions": {
  "me": { "op": "state.get", "path": "turn" },   // a node
  "maxPasses": 2                                  // a named constant
}
```

The short form is detected by the absence of a `body` key rather than by the presence of an
`op`, because **a body is any expression and need not be a node at all**. Naming a constant,
as `maxPasses` does, is genuinely useful where the same number would otherwise appear in
both `terminal.when` and a `type.int`'s `max` — see the note on state-space constraints in
[TypeSchema](TypeSchema.md).

Only an entry with a `body` is the long form, and only there may `params` be written. An
entry with `params` and no `body` is a static error, since there is no body for the
parameters to be visible in.

Because the short form allows a body that is not an object, **a record literal with a key
called `body` cannot be a body**. Wrap it in the long form. Nothing in the five rule sets
runs into this.

## The heart of it — definitions are hygienic

**The body of a definition cannot see any local binding of its caller.**

The scope a body is evaluated in contains only

- the state (`$…`), always visible
- the other definitions (`#…`), always visible
- the names its own `params` declares (`@…`)

A `bind.let` or a `seq.*` `as` at the call site is invisible. The only way in is
`def.call`'s `args`.

This is what fixes a definition's meaning independently of where it is called from.
Reversi's `flips1` takes `at` and `dir` as arguments and is unaffected by `flips` happening
to have named its own binding `d`.

## How often a body runs, and what gets cached

A body is evaluated on every reference. Since every node is pure, the runtime is free to
memoise, and it does: results are cached per evaluation session, keyed by **the definition
and its argument values**.

A session covers one whole call — a single transition, or an entire `GetValidInputs` sweep
over hundreds of candidates — and the snapshot does not move underneath it. That is what
keeps the cache valid for the whole sweep, and it is also why the key needs nothing to
identify the snapshot: a session *is* one snapshot, so there is never a second one to tell
apart.

Reversi is where this pays. `flips` is reached from `inputs.place`'s `when` (through
`canPlace`) and again from its `effects`, with the same `at`; and `GetValidInputs`
evaluates `canPlace` for all sixty-four candidates. Memoisation roughly halves the number
of eight-direction ray walks.

One caveat worth knowing: hashing an argument walks it, so a definition **called with large
sequences as arguments pays for the cache rather than gaining from it**. The rule sets here
pass coordinates, directions and boards, which are cheap to hash.

## Recursion

**Recursion is refused.** The reference graph between definitions has to be acyclic, and a
cycle is a static error at `CreateContext`.

Two reasons.

- Termination could not be guaranteed. `GetValidInputs` evaluates hundreds to thousands of
  candidates, so one runaway recursion stops everything.
- With the call graph fixed, the cost of an evaluation has an upper bound that can be
  estimated.

Reversi has no need of it. Neither did chess or shogi — shogi's drop-mate rule needed a
one-ply search into the opponent's reply, and got it by splitting `hasBoardMove` and
`hasAnyPlay` into separate definitions rather than by recursing.

---

## `def.ref`

Refers to a definition that takes no arguments.

### Form

```jsonc
{ "op": "def.ref", "name": "<name>" }   // name is static
```

Sugar: `"#<name>"`

### How it evaluates

Resolves `name`, evaluates that definition's body, and returns the value.

### Errors

| Condition | When |
| --- | --- |
| the name is not defined | static |
| the definition has `params` | static (use `def.call`) |
| `name` is an expression | static |

### Example (Reversi)

```jsonc
"me":       { "op": "state.get", "path": "turn" },
"opponent": { "op": "branch.match", "value": "#me",
              "cases": { "black": "white", "white": "black" } }
```

`#me` does no more than name the idea "the colour to move", which keeps `$turn` from being
scattered through the document. `#opponent` refers to `#me`, so one definition reaching
another works.

---

## `def.call`

Applies a definition that takes arguments.

### Form

```jsonc
{
  "op": "def.call",
  "def": "<name>",                       // static
  "args": { "<parameter>": <expression>, ... }
}
```

### How it evaluates

1. Each expression in `args` is evaluated **in the caller's scope**.
2. A fresh scope is built, matching them to the callee's `params`.
3. The body is evaluated in that scope, plus the state and the definitions, and its value
   returned.

Arguments are passed as evaluated values — by value, strictly. There are no lazy
arguments.

### Matching arguments

The key set of `args` has to match the callee's `params` **exactly**. Too few or too many
is a static error. There are no positional arguments; names only.

That is a readability judgement about what happens as parameter counts grow. `flips1(at,
dir)` would read fine positionally, but having the syntax rule out a transposition is worth
more than the brevity.

### Errors

| Condition | When |
| --- | --- |
| `def` is not defined | static |
| the keys of `args` do not match `params` | static |
| the definition has no `params` | static (use `def.ref`) |
| the call graph has a cycle | static |

### Example (Reversi's `flips`)

```jsonc
{
  "op": "seq.selectMany",
  "source": { "op": "grid.directions", "of": "$board", "kind": "eight" },
  "as": "d",
  "select": { "op": "def.call", "def": "flips1",
              "args": { "at": "@at", "dir": "@d" } }
}
```

`@at` is `flips`'s own parameter and `@d` is the binding `seq.selectMany` introduced. Both
are evaluated in the caller's scope before they reach `flips1`.

---

## Decided

- **Memoisation is no longer an open question.** It was recorded as unresolved because
  identifying a state snapshot in the cache key looked like something Abstraction would
  have to express. Scoping the cache to an evaluation session dissolved that: the session
  holds exactly one snapshot, so the key is the definition and its arguments and nothing
  else, and Abstraction gained no API for it.
- **No partial application.** Applying `flips1` to a `dir` and leaving `at` open would
  shorten `flips` a little, at the price of putting function values into the value model —
  which every plugin would then have to know about, for one shorter definition.
- **Definitions have no visibility modifier.** All of them are public. The distinction only
  earns its keep alongside importing definitions across rule sets, so it waits on that.
- **No importing definitions between rule sets.** The case for it is variants sharing a
  base — Reversi and its cousins, or a chess variant. It needs a dependency declaration of
  its own, roughly what `requires` is for plugins, and nothing here has two rule sets that
  overlap: the five in [`ruleset/`](../../ruleset/) are five separate games and processes.
  Worth building when a family of variants actually exists, and not before.
