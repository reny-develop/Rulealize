# Rulealize.Plugin.Binding

| | |
| --- | --- |
| Identifier | `Rulealize.Plugin.Binding` |
| Namespace | `bind` |
| Version | `1.0.0` |
| Reserved prefix | `@` |
| Depends on | [the value model](../value-model.md), and nothing else |

Binds a value to a local name, so the same expression need not be written twice. It also
makes a common subexpression explicit: bind a name with `bind.let` and refer to it twice,
and the evaluator knows from the syntax that both are the same value.

Loads independently of [Branch](Branch.md). A configuration with bindings and no branching
makes sense, and so does the reverse.

## Nodes

| Node | Kind | Used in Reversi |
| --- | --- | --- |
| `bind.let` | expression | ○ `flips1`, `terminal.result` |
| `bind.local` | expression | ○ everywhere, as the sugar `@` |

---

## `bind.let`

Binds names in sequence and evaluates a body under them.

### Form

```jsonc
{
  "op": "bind.let",
  "bind": { "<name>": <expression>, ... },   // the key set is static
  "in": <expression>
}
```

| Key | Required | |
| --- | --- | --- |
| `bind` | ○ | names to expressions. The keys are static — a name cannot be computed |
| `in` | ○ | the body, evaluated under the bindings. Its value is the value of the `bind.let` |

### How it evaluates

1. Each entry of `bind` is evaluated **in declaration order** and added to the scope.
2. `in` is evaluated in the scope holding all of them.
3. The value of `in` is returned.

**Bindings are sequential** — the equivalent of `let*`, so a later binding expression may
refer to an earlier one. Reversi's `flips1` depends on this.

```jsonc
"bind": {
  "ray": { "op": "grid.ray", "grid": "$board", "from": "@at", "dir": "@dir" },
  "run": { "op": "seq.takeWhile", "source": "@ray", ... }   // ← refers to ray
}
```

Simultaneous binding would make that unwritable and force a nested `bind.let`. Sequential
it is.

JSON says nothing about the order of an object's keys, but here **the order they appear in
the document is the declaration order**, and a parser has to preserve it. `JsonDocument` in
`System.Text.Json` does.

### Scope

- A binding is visible to the later entries of `bind` and inside `in`.
- It shadows an outer binding of the same name.
- It is invisible from the body of a definition ([value model §6](../value-model.md)).

### How often a binding is evaluated

A binding expression is evaluated **at most once**, however many times it is referred to.
Whether it is evaluated at all when nothing refers to it is left to the implementation —
every node is pure, so the only observable difference is speed.

### Errors

| Condition | When |
| --- | --- |
| `bind` is empty | static (`bind.let` with only an `in` says nothing) |
| a name is bound twice in one `bind` | static, as a duplicate JSON key |
| `in` is missing | static |
| a binding refers to itself | static — see below |

### Example (Reversi's `terminal.result`)

```jsonc
{
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
```

---

## `bind.local`

Refers to a local binding.

### Form

```jsonc
{ "op": "bind.local", "name": "<name>" }   // name is static
```

Sugar: `"@<name>"`

### How it evaluates

Resolves `name` in the current scope and returns its value. The innermost binding wins.

### Where bindings come from

The names `bind.local` can reach are introduced by one of these.

| Introduced by | Key | Visible in |
| --- | --- | --- |
| `bind.let` | each key of `bind` | the later binding expressions and `in` |
| the iterating `seq.*` nodes | `as` | that node's predicate or projection |
| `def.call` | the callee's `params` | the definition's body, and nowhere else |

The Binding plugin provides only **the vocabulary for referring** to a binding; introducing
one through `as` or `params` belongs to whichever plugin owns that node, and the scope
machinery itself belongs to the evaluation context in Abstraction.

### Errors

| Condition | When |
| --- | --- |
| referring to an unbound name | **static.** Scope follows from the syntax, so it resolves at `CreateContext` |
| `name` is an expression | static |

That the unbound case is static matters: it is what stops a typo from turning into a run
time fault once per candidate in a `GetValidInputs` sweep.

---

## Decided

- **A binding is not a recursive scope.** A binding expression cannot refer to the name
  being bound; that is the static error listed above, and it is the same decision as
  [Definition](Definition.md)'s refusal of recursion, for the same reason — without it
  there is no termination argument, and `GetValidInputs` evaluates a guard thousands of
  times per call.
- **No type annotations on bindings.** Writing a schema on a binding the way `state.schema`
  writes one on a field would only be worth anything with an inference pass to check it
  against, and that pass is not being built yet
  ([TypeSchema](TypeSchema.md) records why).
- **No `bind.letSeq` or other sequence-shaped binding form.** The `as` of the `seq.*` nodes
  already binds an element where an element is what is wanted, and a second way to do it
  would be a second thing to learn.
