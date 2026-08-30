# The runtime's surface

What the runtime does with a rule set once it has one, and why it does it that way. The
[README](../README.md) says enough to get a rule set running; this is the rest of it, and it
is where to look when something the README stated flatly needs its reason.

Normative, like the specification.

## The three kinds of node, and when things fail

| Kind | Produces | Appears in |
| --- | --- | --- |
| expression | a value | guards, effect arguments, definition bodies, parameter domains, the actor an input names, `terminal` |
| effect | a write to the state | elements of an input's `effects` |
| schema | the type of a state field | `state.schema` |

**Three kinds of node, four kinds of operation.** A plugin may also register a **draw**, and
a draw builds an expression node like anything else that produces a value — so the table
does not gain a row. What its kind settles is where it may be written: inside an input's
`effects`, at any depth, and nowhere else. `OperationKind` is therefore no longer synonymous
with the .NET type of the node, and it is the placement it records rather than the shape.
[What may happen](#what-may-happen-draws-and-getoutcomes) is the rest of it.

Placement is enforced while the rule set is compiled. So is everything else the document
can settle on its own: unknown operations, [an operation belonging to a vocabulary the
document did not declare](#requires-is-the-scope), missing keys, [a key that is not one the
core reads](#the-keys-the-core-reads), an expression where a literal belongs, a local
nothing declared, an undefined definition, an argument list that does not match a
definition's parameters, a cycle between definitions, a state path that is not in the
schema.

A rule set needs both `state.schema` and `state.initial`, and a schema declaring no fields
is a build error. A state document is a public interface, so there has to be something to
check one against.

A path names a whole field, and it is written out literally rather than computed. That is
what buys the check above: every path in the document is settled once, up front, and which
field an input writes can be read off the document without running it. The inside of a
field holding a board, a record or a sequence is not reachable by path at all — it is read
and written with the vocabulary of the plugin that declared it.

The reason for pushing so much into `CreateContext` is `GetValidInputs`. It evaluates a
guard against every candidate in a parameter's domain, and a fault that first appears on
the forty-first candidate is a fault that reaches production.

What is left to fail at run time is short: a value of the wrong kind, an ordering
comparison against null, division by zero, a `branch.match` with no matching case, a draw
with nothing to draw from or a weight below zero, a value with no text form where one has to
be written down, and a set of effects that builds a state the schema forbids. Reading past
the end of a sequence and reading a square off the board are not on that list — they produce
null, and rule sets are built on their doing so.

## The keys the core reads

Ten in the document and one in every node — `$schema`, `id`, `version`, `requires`, `uses`,
`state`, `definitions`, `held`, `inputs`, `terminal`, and `op`. Everything else is
vocabulary. What follows is the whole of the rest: the objects the core opens itself, and
what it takes from each.

| Where | Keys | |
| --- | --- | --- |
| the document | `$schema` `id` `version` `requires` `uses` `state` `definitions` `held` `inputs` `terminal` | `id` and `version` are required; so are `state` and `inputs` unless `uses` declares something. `$schema` is reserved and not read |
| `requires[]` | `plugin` `version` | a constraint omitted means any version will do |
| `uses[]` | `ruleSet` `version` `as` | `ruleSet` is required; `as` defaults to it and may not contain `.` |
| `state` | `schema` `initial` | both required, and a schema declaring no fields is refused |
| `state.initial` | one value per declared field | every field, and no field the schema did not declare |
| `definitions.<name>` | `body` `params` | only where `body` is written. Otherwise the value **is** the body, so a definition can be a node, a record, or a plain named constant |
| `held.<alias>.<input>` | `when` | required. The alias has to be one `uses` declares and the input one that rule set has |
| `inputs.<name>` | `params` `actor` `when` `effects` `fires` | `effects` is required unless `fires` is written; the rest are not. A name may not contain `.` |
| `inputs.<name>.fires[]` | `held` `input` `args` | `held` and `input` are required; `args` gives one value per parameter of the input named, and no other |
| `inputs.<name>.params.<name>` | `domain` | required |
| `terminal` | `when` `result` | the section is optional; `when` is required once it is written and `result` is not |

Everything below one of those — a schema node, a domain, a guard, an effect — is a node,
and its keys are read by whichever plugin claimed the `op`.

**A key the core does not know is refused**, at every position in that table, with a JSON
pointer to it. That is worth stating on its own because most of the keys above are optional,
and an optional key misspelled is not a document that fails: `whn` is an input with no
guard, which is an input that is always legal, and nothing downstream can tell that from a
rule set that meant it. Inside a node the rule reverses, and has to — the keys there belong
to the plugin, and a core with an opinion about them would make adding an argument to an
operation a change to the runtime.

**An input's name may not contain `.`.** Nothing in the runtime spends the character yet; it
is reserved against the day a rule set may hold another and offer its inputs under a
qualified name. Taking a name away once documents are written with it is the one version of
this that cannot be done.

### `actor`

An expression, evaluated per candidate beside the guard, whose canonical text becomes
`ValidInput.Actor`. It says whose move a candidate is, for a rule set where that is a
question: chess and Reversi name the side to move, blackjack names the seat. A rule set with
no turn leaves it out, and every `Actor` is then null — which is what the roster does, and
why a schedule is not a smaller kind of game.

Nothing consults it. The runtime does not check that an actor is entitled to move, because
whether it is entitled is what `when` is for. `actor` is what a caller filters and displays
by, and stating it in the document rather than in each host is what keeps two hosts over one
rule set agreeing about whose turn it is.

### `terminal`

`when` decides whether a state is final and `result` says what the outcome was. `result` is
evaluated only once `when` holds, so a rule set is entitled to leave it undefined — or
faulting — mid-game, and `TerminalStatus.Result` is null where a rule set declares none.

A rule set with no `terminal` section never reports one, which is an ordinary shape rather
than an omission: a process with no end has nothing to write there, and a caller that stops
when nothing is legal never asks.

## `uses` and `held`: a rule set that holds others

An ordinary business process — *to put somebody on a shift, a request has to be raised and
granted* — has two halves a person naturally writes as two documents, and the guard that
matters lives in neither. The request half cannot see the roster, so nothing stops a request
being raised for a shift the roster will refuse; the roster half has never heard of a
request, so nothing in it is wrong either. `definitions` does not close it: a definition
shares an expression between documents, and this needs a guard over *both states at once*.

`uses` names the rule sets a document holds. `held` is what it may say about them.

```jsonc
"uses": [
  { "ruleSet": "request", "version": "^1.0", "as": "req" },
  { "ruleSet": "shift",   "version": "^1.0", "as": "roster" }
],

"held": {
  // The guard that could not be written as two documents.
  "roster": {
    "assign": { "when": { "op": "cmp.eq", "right": "granted",
                          "left": { "op": "rec.at", "record": "$req", "key": "stage" } } }
  }
}
```

**A held rule set's state is a field.** `uses` declares it — it is not written in
`state.schema` and takes no value in `state.initial`, because it opens where the component
itself opens. A composite's case is therefore still one state document, still a string,
still storable in a column and resumable on another machine. The field holds a record whose
keys are the component's fields, so reading into it is `rec.at` and composition costs the
core no vocabulary at all.

**A held rule set's inputs are offered as `alias.input`.** In `RuleContext.Inputs`, in what
`GetValidInputs` hands back, and in the `input` of an input document — the same string, the
same document, so replay and recording need nothing new. That is why an input's own name may
not contain `.`.

**`held` may only refuse.** Its `when` is evaluated in the composite — over the whole
composed state, against the composite's definitions — with the component input's parameters
in scope under the names the component gave them, and it is asked *after* the component's
own guard. So writing `false` there hides an input and nothing written there can grant one.

**A composite has no other way into a component's state.** The field is in the composite's
schema — it has to be, for `$req` to read it — so an effect can be *written* against it, and
it is refused when it runs, at every depth. That is the restriction the rest of composition
rests on: a composite that could set a component's state to whatever it liked would make the
component's own reachable set say nothing about what the composite does to it.

### `fires`: one input, several of a component's

Narrowing alone models a longer process than the one being run. *A request is granted* and
*somebody goes on the shift* are one decision; as two inputs the composite has a state
between them — granted, not yet assigned — that the process never occupies. The merged
document composition replaces has a `grant` that also performs the assignment, and this is
how a composite has one:

```jsonc
"held": {
  "req":    { "grant":  { "when": false } },   // hidden: the only way to it is what drives it
  "roster": { "assign": { "when": false } }
},

"inputs": {
  "grant": {
    "fires": [
      { "held": "req", "input": "grant" },
      { "held": "roster", "input": "assign", "args": { "slot": "#reqShift", "who": "#reqWho" } }
    ]
  }
}
```

Measured against the document it replaces, that reaches **the same fifteen states and the
same twenty-eight transitions**, element for element, where narrowing alone reaches
thirty-nine.

**A static list, not an effect.** It may not sit inside a branch, so which component inputs
an input drives can be read off the document without running it — the property a literal
`path` buys for a write. It is also what lets `GetValidInputs` decide a firing candidate by
asking each fired input, rather than the author writing that guard a second time and writing
it differently.

**Every fired input goes through its own rule set's two questions**: each argument has to be
a value that component's domain produces, and then that component's guard has to accept it.
An input is offered only where all of them are, so `ApplyToState` still refuses exactly what
`GetValidInputs` would not have listed, and driving an input is never a way past a rule the
component wrote. It writes nothing itself — what runs is the component's own effects.

**A set of inputs all legal now, not a script of steps.** The arguments and every guard read
the state the transition found, so none of them can depend on another's writes and none has
to be guarded against them. Two that name one component share one draft, so their writes
accumulate and land together — snapshot semantics, on the terms they hold everywhere else. A
component that needs two of its own steps in one composite transition is a component that
should offer one input for them.

**`held` is not asked here.** The two say different things: `held` is when the composite
*offers* a component's input as a move, and `fires` is the composite taking it having
already decided. That is what makes `"when": false` the way to hide an input so the only
route to it is the input that drives it.

**An input that drives one that draws is one that draws**, so it goes through `GetOutcomes`
like any other.

### Why the restriction is worth what it costs

**A composite must never be the thing that gets walked.** Its reachable set is the product of
its parts, and two components of two hundred states are forty thousand together. It does not
have to be: which inputs were ever legal, which guards were seen both ways, which endings
were reached, and every property over a single component's state are questions about *a
component*, and answering them costs the sum. Only a property spanning components needs the
composite's own reachable set, and those are exactly the ones `held` and `fires` bear on,
which is a small surface by construction.

That decomposition is sound only because a component's state moves by the component's own
inputs under the component's own rules. **Whatever a walk of a component alone found is an
upper bound on what it does inside any composite that holds it** — so a rule set can be
published on its own and true things said about it without knowing who will hold it. Read
that over the component's *transition relation* and not over a walk that stops at its
`terminal`: `GetValidInputs` does not consult `terminal`, and neither does a composite, so a
component's own ending is the component's business and says nothing about the composite's.

**A cycle is refused when the document is compiled**, naming the documents in it.

**What a composite does not get.** It cannot start a case — *when A finishes, start a B* is
I/O, and `GetValidInputs` answerable with no I/O is the property everything else rests on.
It cannot hold *a list of* instances; one per declaration. And a component meant to be held
and used more than once has to offer the transition that returns it to its start, because
nothing else can: a composite may not write into a component's state, and the runtime will
not invent an input the component did not declare.

### Where the documents come from

`CreateContext(document, held)` takes the document of every rule set reachable through
`uses`, by identifier — the one thing composition adds to the runtime's surface.
`ApplyToState`, `GetValidInputs`, `GetOutcomes` and `GetTerminalStatus` are unchanged.

A dictionary rather than a callback, for the reason `PluginResolution` is pure: fetching a
document is somebody else's business. And a caller has to know which documents to hand it
*before* it hands them over, so `uses` is readable on its own, on the same terms `requires`
is — no runtime, no plugin, and none of the documents it is about to go and get:

```csharp
ImmutableArray<RuleSetRequirement> held = RuleSetRequirement.ReadFrom(document);
```

A document a document holds may hold documents of its own, so a tool assembling a set walks
the graph by calling this again on each one it fetches. Both readers parse a version
constraint through the same code, for the reason [below](#requires-read-before-there-is-a-runtime)
gives: two implementations of three constraint forms would disagree eventually, and the way
they would disagree is a set assembled that the runtime then rejects.

### Which version of each, and what arrived

A constraint says which versions will do; an index says which exist. Choosing between them
is `RuleSetRequirement.Choose`, and **the lowest satisfying version wins** — the same rule
`PluginResolution` follows, through the same code, for the same reason. It is not the
obvious rule: most resolvers take the newest. A restore that was reproducible for `requires`
and not for `uses` would be one command that is half reproducible, and nothing about it
would look wrong.

There is no `RuleSetResolution` answering for a whole document, because `uses` is not flat.
Which version is taken decides which document arrives, which decides what else is named, so
the set is discovered by fetching and is never complete in one pass. The loop, the cycle and
what to do when a late constraint contradicts an early choice are the fetcher's. `Choose` is
the question it asks at each step, one identifier at a time.

```csharp
Version? version = RuleSetRequirement.Choose(entriesNamingOne, publishedVersions);

// ... fetch it, and then read what actually arrived
RuleSetIdentity identity = RuleSetIdentity.ReadFrom(fetched);
bool asked = identity.Satisfies(entry);
```

The second half matters because the two halves are about different things. An index answers
about a package; a `uses` entry is met by the `version` written **inside** the document, and
that is what `CreateContext` checks — it refuses a document whose `id` is not the one named
or whose version the constraint excludes, naming both. `RuleSetIdentity.ReadFrom` is that
check made at the point of the fetch, for a parse rather than a compilation, which is what a
fetcher can afford before the rest of the set is in hand. Nothing but `id` and `version` is
examined, for the reason `requires` is readable on its own: a document worth fetching is
usually one that does not compile yet, because what it holds has not been fetched.

### What a stored composite state carries

Each held field is written as its own frame — the component's `ruleSet` beside its `data` —
and the identity is checked on the way in, on identifier and major version, exactly as a
state document's own is.

The composite's identity cannot do that job. A component may be revised across a major
version without the composite being touched at all, so a stored state that named only
`process@1.0.0` would go on being read after the thing it holds had stopped meaning what it
said. Migration compounds under composition, and the runtime still declines to guess at it;
what it will not do is fail to notice.

## Snapshot semantics

Every expression an input's effects evaluate reads the state as it was when the input
arrived. Writes accumulate in a draft and are committed together.

This is what lets Reversi's placement be written in the order a person would describe it —
put the stone down, then flip what it captured — instead of hoisting the capture set into a
binding to keep the second effect from rescanning a board that already has the new stone on
it.

An effect that does need to build on what an earlier effect wrote reads the field back from
the draft. Both halves are in play at once when two effects edit one board: the second
starts from a board that already has the new stone, while the expressions inside it still
compute captures from the position as it stood before the move.

## Definitions

Held by the core as a name, a parameter list and a body; never evaluated by it. A plugin
supplies the vocabulary for referring to one and calling one, so a rule set that defines
nothing need not load it.

Bodies are hygienic — a body sees the state, the other definitions, and its own parameters,
and nothing from wherever it was called. Arguments are the only way in.

Bodies are also pure, so the runtime caches a result against the definition, its arguments,
and the snapshot, for as long as that snapshot lasts. Over a whole `GetValidInputs` sweep
that matters: Reversi's capture computation is reached from a guard and again from the
effect that follows it, with the same coordinate, for each of sixty-four candidates, and
each evaluation walks eight rays.

Recursion is refused. Termination could not be guaranteed otherwise, and with the call
graph fixed the cost of an evaluation has an upper bound that can be estimated.

## `GetValidInputs` and the limit

Candidates are the product of an input's parameter domains, sifted by its guard. Reversi
produces sixty-five — sixty-four squares and a pass — which is nothing. A shogi move
written as `from`, `to` and a promotion flag produces thirteen thousand, which is not.

`validationLimit` bounds how many guards are evaluated. `Truncated` says whether it stopped
the search early; a truncated result is a subset of what is legal, never a wrong entry. The
real fix for a large domain is to narrow it in the rule set before the guard runs — usually
by carrying as one compound value what would otherwise be several parameters, so that the
domain enumerates the combinations that mean something rather than the product of
everything.

A domain is not a search hint. It is where a rule set says what a parameter may be, so
`ApplyToState` resolves every argument against it too, and the two methods answer the same
question by construction. A rule set is free to put a rule in a domain or in a guard,
whichever keeps the candidate count down.

Whether a state is terminal is a separate question, asked with `GetTerminalStatus`. This
method does not consult it, so a rule set whose guards stay satisfiable after the game ends
will still list moves. That is a property of the rule set, and the runtime does not
second-guess it — the roster depends on it, because a caller that has painted itself into a
corner has to be able to release a shift and back out.

The method is synchronous on purpose. It performs thousands of node evaluations per call,
and an asynchronous signature over that path would cost more than it could buy.

## What may happen: draws and `GetOutcomes`

Not every rule set settles its next state from the move alone. A card comes off a deck, a
die lands: something happens that nobody chose, and the runtime has to be able to say what
that could have been rather than inventing one of the answers.

`GetValidInputs` says **who may do what**. `GetOutcomes` says **what may then happen**, and
where each of those leads. A traversal is the two of them in that order:

```csharp
foreach (ValidInput move in rules.GetValidInputs(state, validationLimit))
foreach (Outcome outcome in rules.GetOutcomes(move.ToInputDocument(rules.RuleSet), state, outcomeLimit))
{
    Walk(outcome.Result.State);   // weighted by outcome.Probability
}
```

**And that is the traversal for every rule set, whether it has chance in it or not.** An
input that draws nothing has exactly one outcome, of probability one and with nothing drawn,
so the inner loop runs once and there is no branch for a caller to write. Chess's `--perft`
walks its tree through exactly this and still agrees with the published numbers; blackjack's
turns thirteen times.

An `Outcome` carries where it leads, so nothing else has to be applied. Enumerating the
alternatives and applying one is the same work, and asking twice would do it twice.

### Where a draw may be written

Inside an input's `effects`, at any depth. Refused in `when`, `actor`, `params[].domain`,
`terminal`, and the body of a `definitions` entry — checked when the rule set is compiled,
with a JSON pointer to the node. Each refusal is a position the runtime evaluates while it
is sifting candidates or while it is memoizing a result:

| | |
| --- | --- |
| a guard | evaluated once per candidate in a domain, with no outcome to be drawing for |
| a domain | enumerated to form those candidates, and walked again to resolve an argument — a domain that drew would refuse the move it had just offered |
| `terminal` | asked about a state, and whether a game is over is not a coin toss |
| a definition body | memoized against its arguments and the snapshot, so a body that drew would answer its first caller and repeat itself to every other one |

A draw does not choose. It works out what could come out and how likely each of those is and
asks the runtime for one, which is why an operation that read a clock or a random number
generator would not be implementing this — it would break every guarantee in the table above
and one more besides, that a recorded transition replays to the state it was recorded
against.

### A drawn value is used once, where it is drawn

There is no way to write "the card that was just drawn" in a second effect. Effects read the
state as the input found it, so a second one naming the same draw evaluates it again and
gets an unrelated value; a draw happens each time control reaches the node, which is also
what makes a draw inside a projection over three seats three draws rather than one.

**An input that draws is therefore an input with one effect that uses what it drew**, and a
rule set that seems to need two is usually a rule set with a redundant field. Blackjack is
the worked case: taking a card out of the deck and putting it in a hand cannot be written as
two effects, and it does not have to be, because the deck is fifty-two cards minus what has
been dealt and the hands already say what has been dealt. Deriving it is not a way around
the constraint; it is what the constraint was pointing at.

### The outcome document

An input document says what somebody decided. An outcome document says what the world did
about it, as the values that were drawn in the order they were drawn.

```jsonc
// rulealize/outcome/v1
{ "$schema": "rulealize/outcome/v1", "ruleSet": "blackjack@1.0.0",
  "input": "hit", "draws": ["9"] }
```

The two together determine the transition exactly, so `ApplyToState(input, state, outcome)`
produces the state the outcome described, however long afterwards. That is what an audit
trail over a rule set with chance in it is made of, and it is why the runtime never
generates anything: a transition nobody enumerated could not be recorded, and a recorded one
that re-rolled would not be a record.

An outcome with no draws in it means the same thing as not passing one, so the three-document
overload subsumes the two-document one and a caller logging every transition writes the same
pair whether the rule set draws anything or not. The two-document overload refuses an input
that draws — from the document, before anything is evaluated, because `CreateContext` already
settled which inputs those are.

Drawn values survive the round trip on the same terms as arguments
([below](#arguments-have-to-survive-the-round-trip)): a number goes out as a number, only a
value JSON has no form for travels as text, and what gets bound coming back is the value the
draw produced rather than its spelling. A value with no text form at all cannot be drawn.

### How the branches are found

Where the draws are is not known before the effects are evaluated. One may sit inside a
branch an earlier draw decided, and its candidates may be what that draw left behind — so a
branch is found by running the effects with a script of choices and seeing where they stop,
then extending the script and running them again from the start. Expressions are pure and
the draft is thrown away, so re-running costs nothing but time.

The search is best-first on the probability of the branch so far. Extending a branch can only
make it less likely, so whatever is popped is at least as likely as anything still queued:
**outcomes come back in descending order of probability**, without a sort, and ties break by
arrival so that two runs of one search agree.

`OutcomeSet.Evaluated` is how many times the effects were run, which exceeds `Count` whenever
there is a draw. Blackjack's `cascade`-shaped case — draw one of three, then one of that many
— is six outcomes and ten runs.

### The limit counts outcomes, and `Coverage` is why

`outcomeLimit` bounds how many outcomes come back, not how much work is done. That is a
different quantity from `validationLimit` despite the similar name, and the difference is not
cosmetic:

| | |
| --- | --- |
| a truncated `ValidInputSet` | a subset of the legal moves. Still a set of legal moves, and every entry in it is right |
| a truncated `OutcomeSet` | a probability distribution that no longer sums to one |

`Truncated` cannot say the second thing on its own, so `Coverage` reports how much of the
probability the outcomes account for. A search deciding whether to trust a result needs the
number and not the flag, and the descending order is what makes the surviving part the part
worth having.

Counting outcomes rather than evaluations is also what makes the guarantee below hold for
every limit rather than for large enough ones.

### Two invariants a caller may rely on

- **An input the rules allow has at least one outcome.** There is no empty answer to check
  for. `GetOutcomes` either returns outcomes or throws.
- **A draw with nothing to draw from is a fault.** Not an absence of outcomes: it means a
  guard did not say the source could be empty, and reporting it where it happened is what
  makes the first invariant worth relying on. A candidate whose weight is zero is a different
  matter and simply does not appear — none of that rank left is an ordinary state of affairs.

A branch whose effects build a state the schema forbids is a fault too, and the whole call
fails rather than the branch being dropped. Dropping it would turn the schema into something
that silently reweights a distribution, and a caller could not tell that from a draw that
genuinely could not happen.

`GetOutcomes` is synchronous for the reason `GetValidInputs` is: it runs an input's effects
once per branch of its outcome tree, and nothing on that path is I/O.

## Where asynchrony belongs

At the boundary, and nowhere else. Evaluation is pure computation over documents that are
already in memory, so the methods that take a `string` are synchronous — `CreateContext`,
`ApplyToState`, `GetValidInputs`, `GetOutcomes`, `GetTerminalStatus`. An `Async` suffix over
a body that can only ever return an already-completed task tells the caller something untrue
about where it may yield.

Reading a document off a stream genuinely is I/O, and that is what the asynchronous
overloads are for: `CreateContextAsync(Stream)`, `ApplyToStateAsync(Stream, Stream)` and
`ApplyToStateAsync(Stream, Stream, Stream)`.
They await the read and then run the same synchronous evaluation.

## `requires` is the scope

A name is resolved only among the plugins the document declared. An `op` whose namespace
belongs to a loaded plugin the document did not name is refused, naming that plugin; so is a
shorthand character, whether it is written bare or qualified.

That is what makes `requires` the whole of what a rule set draws on rather than an
approximation of it, and the reason to enforce it is what happens otherwise: a document
reaching an undeclared vocabulary compiles wherever that plugin happens to be loaded and
fails wherever it is not, which is a fault with no symptom until the document is moved — or
published, and compiled by somebody else against a folder assembled from the `requires` it
was too small to describe.

One rule does two jobs where a character has more than one claimant. Narrowing to the
declared vocabularies is what decides `$` between State and a second plugin that reserved
it, so there is no separate tie-break: a character none of the declared vocabularies reserve
is refused, and one that two of them reserve is ambiguous and has to be qualified.

## `requires`, read before there is a runtime

`CreateContext` refuses a document naming a plugin that is not loaded, or loaded at a version
the constraint excludes. That is the last word, and it comes too late to be the only one: a
tool assembling a plugin folder has to know what to fetch *before* anything is loaded.

So `requires` is also readable on its own, with no runtime and no plugin present.

```csharp
ImmutableArray<PluginRequirement> required = PluginRequirement.ReadFrom(document);
PluginResolution resolution = PluginResolution.Resolve(required, publishedVersions);
```

Nothing about the document but `requires` is examined — a document worth fetching plugins for
is often one that does not compile yet, and deciding whether it compiles is `CreateContext`'s
job, done against a full vocabulary.

**Both read `^1.0` through the same code, and that is the point.** Two implementations of
three constraint forms would be easy to write and would disagree eventually, and the way they
would disagree is a tool assembling a folder the runtime then rejects — a failure with no
symptom until the moment it is too late to be useful.

`Resolve` takes the published versions of each plugin and is pure; fetching them is not this
library's business, for the reason the section above gives. Two rules decide what comes out:

| | |
| --- | --- |
| **the lowest satisfying version wins** | a constraint says what the document needs, so honouring it exactly is what makes the same document resolve to the same folder after three more releases. Moving to a newer one means changing what the document asks for, which is not something restoring it should do |
| **constraints on one plugin are met together** | two entries naming one plugin resolve to a single version, or to neither. A folder cannot hold two versions of one assembly |

What `^1.0` reads is the plugin's major version, so a plugin that removes an operation or
changes what one means releases a new major. A constraint means something only because that
holds.

An unmet requirement is reported rather than thrown, with the versions that do exist, because
a document whose vocabulary is partly unpublished is a real and reasonable document — one
naming a vocabulary the host registers with `AddPlugin` is exactly that — and what resolved
is the useful half of the answer.

`uses` is read on the same terms and chooses by the same rule, in the smaller shape a graph
allows: [above](#which-version-of-each-and-what-arrived).

## Arguments have to survive the round trip

`GetValidInputs` hands back moves; feeding one straight back to `ApplyToState` must
produce the move it described. So an argument is written in its own JSON form — a number
stays a number, a boolean stays a boolean.

Only a value with no JSON form of its own is written as text: a coordinate, a direction,
anything opaque. That is what the value model's canonical text is for, and why a plugin
whose values can be input arguments has to accept both its own type and that text on the
way back in.

Coming back, the text is matched against the domain and the domain's value is what gets
bound, so a rule reading the argument sees the coordinate and not its spelling. One
concession, and no more: `"2"` still does not match `2`, because different kinds are
unequal in the value model and a boundary that quietly disagreed with that would be a bad
place to disagree.

A value with neither a JSON form nor a canonical text form therefore cannot be an input
argument at all. A record is the case that comes up: a domain returning one fails when the
argument is resolved, and a compound argument is a tuple instead.

The readable view is ordered, and that is a guarantee rather than an accident of storage.
`ValidInput.Arguments` was an `ImmutableDictionary` once, which answers by name and enumerates
in hash order — and .NET reseeds string hashing per process, so the same move came out
`deploy(service: web, version: 2)` in one run and `deploy(version: 2, service: web)` in the
next. Stable within a run, different between runs, invisible with one argument, a coin flip
with two. A text form that varies between runs is not a text form: anything that writes a move
down and matches its own writing back — which is what a command line does — is right about
half the time. So declared parameter order is what is kept, the same order
`ToInputDocument` writes, and looking an argument up by name is a scan of the handful of
parameters an input has.

**A drawn value makes the same trip on the same terms.** `GetOutcomes` hands back what
happened and `ApplyToState` has to be able to replay it, so the rules above hold word for
word with "the domain" read as "what could have come out of that draw" — including the last
one, which is why a draw producing a record fails where it is drawn rather than where it
would have been written down. Both directions go through one implementation, because two
would eventually disagree about a coordinate and the disagreement would surface as a replay
that did not replay.

## Versioning of a state document

A state document is read when the `ruleSet` it names matches on identifier and major
version, so `reversi@1.0.0` and `reversi@1.4.2` are interchangeable and `reversi@2.0.0` is
not. Anything a revision did to the shape of the state is the schema's business rather than
the version's.
