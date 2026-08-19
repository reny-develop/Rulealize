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
comparison against null, division by zero, a `branch.match` with no matching case, and a
set of effects that builds a state the schema forbids. Reading past the end of a sequence
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

## Where asynchrony belongs

At the boundary, and nowhere else. Evaluation is pure computation over documents that are
already in memory, so the methods that take a `string` are synchronous — `CreateContext`,
`ApplyToState`, `GetValidInputs`, `GetTerminalStatus`. An `Async` suffix over a body that
can only ever return an already-completed task tells the caller something untrue about
where it may yield.

Reading a document off a stream genuinely is I/O, and that is what the asynchronous
overloads are for: `CreateContextAsync(Stream)` and `ApplyToStateAsync(Stream, Stream)`.
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

## Versioning of a state document

A state document is read when the `ruleSet` it names matches on identifier and major
version, so `reversi@1.0.0` and `reversi@1.4.2` are interchangeable and `reversi@2.0.0` is
not. Anything a revision did to the shape of the state is the schema's business rather than
the version's.
