# The JSON DSL — working the design out on Reversi

The design memo that came before `Rulealize.Abstraction` had an API, written to make
"what would be enough JSON to write" concrete by writing it. The interfaces on the
Abstraction side were then worked backwards out of the shape settled here.

- Subject: the three documents — RuleSet, State, InputRule
- Written when `Rulealize.Abstraction` did not yet exist. It does now, and §7 records what
  it ended up needing.


## 1. The structural principles

The constraints in CLAUDE.md narrow the shape of the DSL a long way.

| Constraint | What follows for the DSL |
| --- | --- |
| the core knows no plugin-specific type | **all the core knows about a node is that it is a JSON object with an `op`**. The value of `op` selects from a registry, and the rest goes to the plugin's factory |
| plugins do not reference Rulealize | a factory depends on `Rulealize.Abstraction` and nothing else |
| `GetValidInputs(state, limit)` has to work | **an input is a name plus parameters, and each parameter needs an enumerable domain.** This is the sharpest constraint on the design |

The keys the core reserves structurally are only these:

```
$schema / id / version / requires / state / definitions / inputs / terminal
```

plus `op`, which tells a node from anything else. **Everything else is plugin vocabulary.**

### The reference sugar

```jsonc
"$board"    // = { "op": "state.get",  "path": "board" }
"@at"       // = { "op": "bind.local", "name": "at" }
"#opponent" // = { "op": "def.ref",    "name": "opponent" }
```

The core does not do this desugaring either. **A plugin reserves a leading character and
registers the expansion**, so the core need not know sugar exists — `$` belongs to the
State plugin, `@` and `#` to Binding and Definition. Detecting a collision between prefixes
is the runtime's job, at load time.


## 2. Decomposing the plugins

### 2.1 What the cuts are made on

To avoid a catch-all name like "Core", the cuts follow three criteria.

- **A — can it be loaded on its own?** Does the vocabulary mean anything by itself?
  Branching without bindings is a coherent configuration, so the two are separate plugins.
- **B — is there a reason to replace it?** There are real reasons to want `seq` lazy, or
  parallel, so it is worth being able to swap.
- **C — do they interoperate through the value model alone?** No plugin may depend on
  another's CLR types. `seq.any` consumes what `grid.coords` produced because Abstraction
  holds a shared value model, not because Grid references Sequence (→ 6.1).

And the most practical reason of all: **`requires` should tell you what vocabulary a rule
set uses.** A `Rulealize.Plugin.Core` would destroy that discoverability outright.

### 2.2 The ten

Reversi needed ten. Chess later added [Tuple](plugin/Tuple.md) and shogi added
[Record](plugin/Record.md), for the twelve that exist now; the specifications are in
[plugin/](plugin/README.md).

| Plugin | Namespace | Provides |
| --- | --- | --- |
| [`Rulealize.Plugin.Binding`](plugin/Binding.md) | `bind` | `let` / `local` (sugar `@`) — scoped bindings |
| [`Rulealize.Plugin.Branch`](plugin/Branch.md) | `branch` | `if` / `match` |
| [`Rulealize.Plugin.Definition`](plugin/Definition.md) | `def` | `ref` (sugar `#`) / `call` |
| [`Rulealize.Plugin.Logic`](plugin/Logic.md) | `logic` | `and` / `or` / `not` / `xor` |
| [`Rulealize.Plugin.Comparison`](plugin/Comparison.md) | `cmp` | `eq` / `ne` / `lt` / `lte` / `gt` / `gte` / `compare` / `isNull` / `coalesce` |
| [`Rulealize.Plugin.Arithmetic`](plugin/Arithmetic.md) | `math` | `add` / `sub` / `mul` / `div` / `mod` / `min` / `max` / `abs` |
| [`Rulealize.Plugin.TypeSchema`](plugin/TypeSchema.md) | `type` | `enum` / `int` / `bool` / `string` |
| [`Rulealize.Plugin.Sequence`](plugin/Sequence.md) | `seq` | `any` / `count` / `empty` / `takeWhile` / `elementAt` / `select` / `selectMany` / `where` |
| [`Rulealize.Plugin.State`](plugin/State.md) | `state` | `get` (sugar `$`) / `set` / `update` |
| [`Rulealize.Plugin.Grid`](plugin/Grid.md) | `grid` | `board` / `at` / `set` / `setMany` / `coords` / `cells` / `ray` / `directions` |

Identifier and namespace correspond one to one, declared by the plugin's own manifest.
Namespace collisions are detected at load.

### 2.3 What is distributed is not what is decomposed

A ten-line `requires` is verbose, but that is **a distribution problem, not a DSL problem**.
A NuGet metapackage — `Rulealize.Plugin.StandardLibrary`, holding nothing and referencing
these — makes installation one line. Letting `requires` name a profile instead was
considered and rejected, since it destroys the discoverability of 2.1 all over again.


## 3. The rule set (Reversi)

```jsonc
{
  "$schema": "rulealize/ruleset/v1",
  "id": "reversi",
  "version": "1.0.0",

  "requires": [
    { "plugin": "Rulealize.Plugin.Binding",    "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Branch",     "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Definition", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Logic",      "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Comparison", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Arithmetic", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.TypeSchema", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Sequence",   "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.State",      "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Grid",       "version": "^1.0" }
  ],

  // ── the shape of the state, and where it starts ───────────────
  "state": {
    "schema": {
      "board": {
        "op": "grid.board",
        "width": 8, "height": 8, "coord": "algebraic",
        "cell": { "op": "type.enum", "values": ["black", "white"], "nullable": true }
      },
      "turn":   { "op": "type.enum", "values": ["black", "white"] },
      "passes": { "op": "type.int", "min": 0, "max": 2 }
    },
    "initial": {
      "board": { "d4": "white", "e4": "black", "d5": "black", "e5": "white" },
      "turn": "black",
      "passes": 0
    }
  },

  // ── reusable expressions (non-recursive, pure) ────────────────
  "definitions": {
    "me":       { "op": "state.get", "path": "turn" },
    "opponent": {
      "op": "branch.match",
      "value": "#me",
      "cases": { "black": "white", "white": "black" }
    },

    // the cells to flip in one direction
    "flips1": {
      "params": ["at", "dir"],
      "body": {
        "op": "bind.let",
        "bind": {
          "ray": { "op": "grid.ray", "grid": "$board", "from": "@at", "dir": "@dir" },
          "run": {
            "op": "seq.takeWhile", "source": "@ray", "as": "c",
            "predicate": {
              "op": "cmp.eq",
              "left":  { "op": "grid.at", "grid": "$board", "coord": "@c" },
              "right": "#opponent"
            }
          }
        },
        "in": {
          "op": "branch.if",
          // if what follows the run of opponent stones is mine, that run is settled
          "cond": {
            "op": "cmp.eq",
            "left": {
              "op": "grid.at", "grid": "$board",
              "coord": {
                "op": "seq.elementAt", "source": "@ray",
                "index": { "op": "seq.count", "source": "@run" }
              }
            },
            "right": "#me"
          },
          "then": "@run",
          "else": { "op": "seq.empty" }
        }
      }
    },

    // all eight directions together
    "flips": {
      "params": ["at"],
      "body": {
        "op": "seq.selectMany",
        "source": { "op": "grid.directions", "of": "$board", "kind": "eight" },
        "as": "d",
        "select": { "op": "def.call", "def": "flips1", "args": { "at": "@at", "dir": "@d" } }
      }
    },

    "canPlace": {
      "params": ["at"],
      "body": {
        "op": "logic.and",
        "all": [
          { "op": "cmp.isNull", "value": { "op": "grid.at", "grid": "$board", "coord": "@at" } },
          { "op": "seq.any", "source": { "op": "def.call", "def": "flips", "args": { "at": "@at" } } }
        ]
      }
    },

    "hasAnyMove": {
      "body": {
        "op": "seq.any", "source": { "op": "grid.coords", "of": "$board" }, "as": "c",
        "predicate": { "op": "def.call", "def": "canPlace", "args": { "at": "@c" } }
      }
    }
  },

  // ── inputs, which are where transitions get in ────────────────
  "inputs": {
    "place": {
      "actor": "#me",
      "params": {
        "at": { "domain": { "op": "grid.coords", "of": "$board" } }   // ← where enumerability comes from
      },
      "when": { "op": "def.call", "def": "canPlace", "args": { "at": "@at" } },
      "effects": [
        { "op": "grid.set", "target": "$board", "coord": "@at", "value": "#me" },
        { "op": "grid.setMany", "target": "$board",
          "coords": { "op": "def.call", "def": "flips", "args": { "at": "@at" } },
          "value": "#me" },
        { "op": "state.set", "path": "passes", "value": 0 },
        { "op": "state.set", "path": "turn", "value": "#opponent" }
      ]
    },

    "pass": {
      "actor": "#me",
      "params": {},
      "when": { "op": "logic.not", "value": "#hasAnyMove" },
      "effects": [
        { "op": "state.set", "path": "passes",
          "value": { "op": "math.add", "of": ["$passes", 1] } },
        { "op": "state.set", "path": "turn", "value": "#opponent" }
      ]
    }
  },

  // ── when it ends, and how it came out ─────────────────────────
  "terminal": {
    "when": {
      "op": "logic.or",
      "any": [
        { "op": "cmp.gte", "left": "$passes", "right": 2 },
        { "op": "logic.not", "value": {
            "op": "seq.any", "source": { "op": "grid.coords", "of": "$board" }, "as": "c",
            "predicate": { "op": "cmp.isNull",
                           "value": { "op": "grid.at", "grid": "$board", "coord": "@c" } } } }
      ]
    },
    "result": {
      "op": "bind.let",
      "bind": {
        "b": { "op": "seq.count", "source": { "op": "grid.cells", "of": "$board" },
               "as": "c", "where": { "op": "cmp.eq", "left": "@c", "right": "black" } },
        "w": { "op": "seq.count", "source": { "op": "grid.cells", "of": "$board" },
               "as": "c", "where": { "op": "cmp.eq", "left": "@c", "right": "white" } }
      },
      "in": {
        "op": "branch.match",
        "value": { "op": "cmp.compare", "left": "@b", "right": "@w" },
        "cases": { "gt": "black", "lt": "white", "eq": "draw" }
      }
    }
  }
}
```

**Not one Reversi-specific plugin appears in this document.** That general vocabulary alone
describes Reversi is the main evidence that the design holds together.


## 4. State and InputRule

### The state document

```jsonc
{
  "$schema": "rulealize/state/v1",
  "ruleSet": "reversi@1.0.0",
  "data": {
    "board": { "d4": "white", "e4": "black", "d5": "black", "e5": "white" },
    "turn": "black",
    "passes": 0
  }
}
```

`board` is sparse. Whether it is instead dense — an 8×8 array — belongs to the `grid.board`
plugin, and the core is not involved.

### The input document

```jsonc
{
  "$schema": "rulealize/input/v1",
  "ruleSet": "reversi@1.0.0",
  "input": "place",
  "args": { "at": "d3" }
}
```

A pass is `{ "input": "pass", "args": {} }`.

### What `ApplyToState` returns

```jsonc
{
  "data": {
    "board": { "d3": "black", "d4": "black", "e4": "black", "d5": "black", "e5": "white" },
    "turn": "white",
    "passes": 0
  },
  "terminal": false
}
```

### What `GetValidInputs` returns from the opening position

```jsonc
[
  { "input": "place", "args": { "at": "d3" }, "actor": "black" },
  { "input": "place", "args": { "at": "c4" }, "actor": "black" },
  { "input": "place", "args": { "at": "f5" }, "actor": "black" },
  { "input": "place", "args": { "at": "e6" }, "actor": "black" }
]
```


## 5. What `validationLimit` means

Candidates are the product of the parameter domains, sifted by `when`. For Reversi that is
64 for `place` and 1 for `pass`, so 65.

Both the domain and `when` are rules, and `ApplyToState` enforces both
([value model §4.1](value-model.md)). Reversi puts all of its rules in `when`, so for this
rule set alone the domain does behave like a hint. Chess is the opposite — most of its
rules are in the domain — and that is what made enforcing them necessary.

The limit is defined as **the most candidates whose `when` may be evaluated**, with
`Truncated = true` when it is hit. Shogi's `move(from, to, promote)` would be 81 × 81 × 2,
about 13k, which is what made narrowing before the guard the deciding question — and the
answer turned out to be a compound parameter rather than a narrowing node
([Tuple](plugin/Tuple.md), [Grid](plugin/Grid.md)).


## 6. The design decisions this raised, and how they went

### 6.1 Interoperation rests on a shared value model — settled

Split out into [the value model and the three kinds of node](value-model.md). In summary:
`seq.any` has to be able to consume what `grid.coords` returned, and Grid referencing
Sequence would defeat the decomposition, so **the shared value model lives in Abstraction**.
The minimum is `null / bool / number / text / sequence / record / opaque`, where `opaque`
holds plugin-specific values such as coordinates and directions and the core never looks
inside.

Null propagation belongs to the value model too, and Reversi's `flips1` depends on it.

- `seq.elementAt` out of range → `null`
- `grid.at` with a `null` coordinate → `null`
- `cmp.eq(null, "black")` → `false`

This is what drops "the ray is opponent stones to the edge" naturally into `else`, and it
is fixed in the specification because it keeps boundary checks out of the DSL.

### 6.2 Effects have snapshot semantics — settled

`place` puts a stone down and then flips, and applied one at a time the second effect's
`flips` would rescan a board that already has the new stone. **Every expression in every
effect evaluates against the state as the input found it; writes pile up in a draft and
land together.**

The sequential reading would force `flips` into a `bind.let` first, which is work pushed
onto whoever writes the rule set.

### 6.3 `flips` is evaluated twice, from `when` and from `effects` — settled

This is where memoisation earns its place, and it was built: results are cached per
evaluation session, keyed by the definition and its argument values. A session covers a
whole `GetValidInputs` sweep, over which 64 candidates × 8 directions of ray walking is
roughly halved. [Definition](plugin/Definition.md) records why the cache key needs nothing
to identify the snapshot.

### 6.4 Explicit pass, or automatic — settled on explicit

Real Reversi skips the turn automatically when there is no move. The model above is the
explicit one, where `GetValidInputs` returns nothing but `pass`.

**It stays explicit.** Making it automatic means adding something like an `"after"` phase
to the rule set — a hook that runs after a transition and may fire another — and that is a
real extension of what the DSL can express, not a convenience. The reason not to is that
it moves a rule out of `inputs` and into a mechanism: the automatic pass would no longer be
visible as a thing that happens, and `GetValidInputs` would stop being the whole account of
what may happen next, which is the property [roster](dsl-example-roster.md) turns out to
depend on most. The cost is one extra ply in the caller's loop, which the
[Reversi sample](../sample/Reversi/) pays without comment.

### 6.5 May definitions recurse — settled on no

Once they take `params` they are pure functions in all but name, and recursion forfeits any
termination argument. Non-recursive was judged sufficient for Reversi, and it has held for
four more rule sets — shogi needed a one-ply search into the opponent's reply and got it by
splitting the definition in two ([shogi §4](dsl-example-shogi.md)) rather than by
recursing. [Definition](plugin/Definition.md) has the reasoning.

### 6.6 Is `state.schema` required — settled on yes

Running from `initial` alone was conceivable, and it is not what was built: a rule set
needs both `schema` and `initial`, and a schema declaring no fields is a build error.
Validating states from outside is not optional once the state document is a public
interface, and [roster](dsl-example-roster.md) is the case that proves it — once instance
data moved into the state, **receiving a malformed state document became the normal path
rather than an edge case**. That schema descriptions are themselves plugin vocabulary
(`grid.board`, `type.enum`) is consistent with everything else here.


## 7. What Abstraction turned out to need

This section was written as a forecast, before Abstraction existed. It held up, and what
is listed here is what got built.

- **the value model** — the kinds and the null propagation of 6.1
- **`INodeFactory`** — declaring an `op` name, and building a node from a JSON object
- **`INodeBuilder`** — the recursive builder handed to a factory, which it delegates child
  construction to
- **`IRuleNode`** — takes an evaluation context, returns a value
- **the evaluation context** — read access to the state, the scope of local bindings,
  resolving `definitions`
- **the effect interface** — writing to the draft of 6.2
- **registering string sugar** — reserving a prefix (`$` / `@` / `#`)
- **the plugin manifest** — identifier, version, namespace

Two things were added later that this list did not foresee. `SchemaNode.Normalize`, so that
a lazy sequence written into the state settles instead of holding on to the snapshot it was
built from ([collections §7.2](collections.md)); and `SchemaNode.Validate`, which checks a
value rather than a document and is what lets a transition check what its effects built.
