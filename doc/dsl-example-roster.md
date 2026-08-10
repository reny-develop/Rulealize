# The JSON DSL under test — something that is not a game

Reversi, chess and shogi are three board games. **Three samples from the same corner, which
is not evidence of generality.** CLAUDE.md puts simulations and rule-based applications
inside the scope, and none of that had ever been tried.

The subject is a shift roster — assigning resources. **The `grid` plugin is deliberately
not used at all**, to find out whether the DSL is board-shaped.

- Subject: [ruleset/roster.json](../ruleset/roster.json)
- Checked by: [test/RosterTests.cs](../test/RosterTests.cs), 17 cases
- Conclusion: **it works.** `grid.` appears zero times.


## 1. What is different

| | Board game | Shift roster |
| --- | --- | --- |
| turn | there is one | **none** — no `actor` is declared |
| opponent | yes | no |
| order | matters | assignments commute |
| ending | who won | **were the constraints met** (`complete` / `stuck`) |
| the main question | what is the next move | **what can still be assigned** |

The last row is the essential one. `GetValidInputs` is both "the list a rostering screen
wants to display" and "the candidates a solver wants to branch on", and the runtime answers
it without knowing that one of those is a conversation with a person.

### The size of it

One week's worth, as placed by `state.initial`. **That is a property of the document rather
than of the rule set** (→ §4.1).

- four people (ann / bo / cy / di) with capacities 3 / 3 / 2 / 2, so ten in total
- five days × two slots = ten shifts, three of which require someone senior
- nobody can take both slots on one day

The capacities total exactly the number of shifts, so **a complete solution has no slack**.
The per-person assignment counts are checked, to show the tests are not passing by landing
in an easy corner.

The tests run a second week — three people, three days, six shifts, different names —
through **the same `RuleContext`**, and confirm both search their way to a complete
solution.


## 2. The state is four sequences

```jsonc
"staff":    { "op": "type.list", "element": { "op": "rec.of",
              "fields": { "name": …, "capacity": …, "senior": … } } },
"shifts":   { "op": "type.list", "element": { "op": "rec.of",
              "fields": { "id": …, "day": …, "senior": … } } },
"assigned": { "op": "type.list", "element": { "op": "rec.of",
              "fields": { "shift": …, "who": … } } },
"log":      { "op": "type.list", "maxLength": 5, "element": … }
```

**Without [collections](collections.md) there is nowhere to put a single one of these.** A
variable-length roll, shift table and assignment list do not fit in a flat map of scalars.
This is the evidence behind the claim that non-game uses need sequences.

`day` is a field of a shift for the reason in §4.2 — do not bury structure in a name.

The audit trail appends and truncates in one expression.

```jsonc
{ "op": "seq.skip",
  "source": { "op": "seq.concat", "of": ["$log", { "op": "seq.of", "of": ["@entry"] }] },
  "count": { "op": "math.max", "of": [0, { "op": "math.sub", "left": <length>, "right": 5 }] } }
```

The schema's `maxLength: 5` says the same number twice. Since a transition checks what its
effects built ([TypeSchema](plugin/TypeSchema.md)), getting the truncation wrong now fails
at the transition that overran, naming the input — rather than growing quietly and being
rejected the next time the state is read.


## 3. What went well

### 3.1 The domains come from the state

A parameter's domain is **an expression that reads the state**, not a list written into the
rule set.

```jsonc
"names": { "op": "seq.select", "source": "$staff", "as": "s",
           "select": { "op": "rec.at", "record": "@s", "key": "name" } },

"params": { "who":   { "domain": "#names" },
            "shift": { "domain": "#openShifts" } }
```

This is also the thing that makes the correction in §4.1 work. There is no second copy of
the roll anywhere, so changing the roll changes the candidates.

### 3.2 `GetValidInputs` works as a constraint engine

The most valuable of the 17 tests. The caller knows nothing about the seniority requirement
or the rest rule; it asks what can be assigned, tries one, and backtracks when the answer
comes back empty — depth-first search, written directly. It finds a complete solution in
128 ms.

**Everything that makes the search terminate is in the document** — finite domains, guards
that tighten as the table fills. The runtime knows none of it.

### 3.3 That `GetValidInputs` ignores `terminal` is a virtue here

The property that looked odd in a game is right here. `release` is still offered from a
table that has painted itself into a corner, so the caller can get out of it.


## 4. What the first version got wrong, and the corrections

**This is the most useful section in the document.** The first version listed five "holes in
the DSL". On review **four of them were problems with how the rule set was written**. The
reasons for withdrawing them are kept.

### 4.1 The instance was baked into `state.schema` (**a real problem, fixed**)

The first version declared the staff as a `type.enum` of four names and the shifts as
static keys of a `rec.map`. The consequence was that "next week's roster is a different
rule set document", and I reported that as a limitation of the DSL.

**That was wrong.** Instance data goes in the state document.

```jsonc
// first version — this document describes "this week"
"roster": { "op": "rec.map", "keys": ["mon-am", …], "value": { "op": "type.enum", "values": ["ann", …] } }

// now — this document describes "what a roster is"
"staff":  { "op": "type.list", "element": { "op": "rec.of", "fields": { "name": …, "capacity": …, "senior": … } } }
"shifts": { "op": "type.list", "element": { "op": "rec.of", "fields": { "id": …, "day": …, "senior": … } } }
```

**The rule set is the domain; the state is the instance.** Board games hide the
distinction — a chess board really is always 8×8, so baking the instance into the schema
costs nothing there and everything here. Write this after three board games and the habit
comes with you.

[The tests](../test/RosterTests.cs) run two weeks — four people over five days, and three
over three, with different names — through **one `RuleContext`**, and confirm both search
to a complete solution. That no person's name and no shift's name appears anywhere after
`definitions` is confirmed by reading the document.

#### A by-product — when to use `rec.of` and when `rec.map`

The first version used `rec.map` (like values, looked up by key) and it now uses `rec.of`
(named, heterogeneous fields). **That difference is exactly the difference between an
instance and a domain.**

- `rec.map` is right when **the domain fixes the key set.** Shogi's seven kinds in hand are
  a rule of shogi, not a property of the game being played.
- If the instance fixes the key set, it is not a key set — it is a sequence element.

### 4.2 There is no string manipulation (**withdrawn**)

I reported that `"mon-am"` could not be taken apart to get the day, so I declared a lookup
table. But **declaring the relationship is the better data definition** — it is now a `day`
field on a shift — and burying structure in a name to parse it back out later is the worse
design.

Whether string operations are needed is a question for after a few more subjects. It is not
a hole today.

### 4.3 There is no way to say better or worse (**withdrawn**)

I reported that there was nowhere to put an objective function. But **if you want to talk
about better and worse, write a rule set that says so** — put a score in the state and use
it in `terminal` or in a guard. Not a problem with the runtime API.

### 4.4 There is no notion of progress (**withdrawn**)

I reported that `release` makes the state space cyclic. Deciding what counts as progress is
the caller's responsibility, and does not belong in the rules.

### 4.5 `Arguments[key]` throws (**withdrawn**)

`ImmutableDictionary` raising `KeyNotFoundException` for a key it does not have is a
dictionary behaving correctly. A rule set with several inputs returns mixed results and the
caller has to narrow with `.Input` first, which is an ordinary responsibility.
[Shogi §5](dsl-example-shogi.md) had recorded the same thing as a rough edge; this is the
conclusion that stands.


## 5. Conclusion

**The DSL is not board-shaped.** Zero references to `grid`, no turn, no winner, and a shift
assignment problem that can be written and searched. [Collections](collections.md) are what
made it possible — without `type.list` and `rec.of` there is nowhere to put a
variable-length staff roll or shift table.

**And the writer bringing board-game habits was a larger danger than any limitation of the
DSL.** Four of the five holes reported in the first version were my design mistakes, and
the fifth was about how I had written it rather than about the DSL.

That pairs with [the check on plugins](dsl-example-reversi.md) — "a node has to be
specifiable without reference to any particular rule set". **The same question applies to a
rule set: is this document written without reference to this problem instance?**


## 6. What is still open, and what has closed

- **Dates and times** — there is neither a type nor any arithmetic for them, and the shape
  of the answer has since emerged from [deploy](dsl-example-deploy.md) rather than from
  here. **A date is a text field in the state**, and where a rule genuinely has to compute
  with one, that is a vocabulary the host supplies: `acme.frozen` takes `state.today` as an
  argument and consults an injected calendar. That covers a working day, a freeze window
  and an ordering, which is most of what rules actually ask of a date. What is not covered
  is arithmetic in the rule set itself — a roster spanning a week boundary, or shift
  durations — and putting a date type into the standard vocabulary means picking a
  calendar, a time zone policy and a formatting convention, none of which a rule engine
  should be deciding on everyone's behalf. It stays out until a subject needs date
  arithmetic that the host cannot reasonably supply.
- **Updating a sequence at an index** — doing to a position what `rec` does to a key. The
  present way, removing with `seq.where` and adding with `seq.concat`, does the job and
  reads as what it is: an assignment list is a set, and "replace the third element" is not
  a question anyone asks of it. It waits for a subject where position carries meaning.
- **The cost of scanning** — looking a person up became a linear `seq.where`. Fine at the
  size of a roster and it would tell on a large roll. There is no index, and adding one
  means either a keyed collection whose keys come from the instance — which §4.1 says is a
  sequence, not a record — or the runtime caching a projection, which breaks the rule that
  a domain is an expression evaluated against the state. Neither is worth doing for a
  problem that does not exist yet.
- **Validating the state document** — moving the instance into the state **made receiving a
  malformed state document the normal path**, and `type.list` and `rec.of` validation
  carries most of it. What still cannot be said is a constraint like "no name appears twice
  in the roll". That is the same missing thing as invariants across fields, tracked in
  [Record](plugin/Record.md): a predicate language for `state.schema`, and a new reserved
  key in a document whose reserved keys are deliberately eight.
