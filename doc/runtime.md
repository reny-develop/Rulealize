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
can settle on its own: unknown operations, missing keys, an expression where a literal
belongs, a local nothing declared, an undefined definition, an argument list that does not
match a definition's parameters, a cycle between definitions, a state path that is not in
the schema.

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
with nothing to draw from, and a set of effects that builds a state the schema forbids.
Reading past the end of a sequence
and reading a square off the board are not on that list — they produce null, and rule sets
are built on their doing so.

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
