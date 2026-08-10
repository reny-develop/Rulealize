# The JSON DSL under test — where the vocabulary is not all plugins

Reversi, chess, shogi and the shift roster all take **every bit of their vocabulary from the
twelve standard plugins**. That is what a deployed application correctly looks like, and
**it is not what a user of the library looks like.** A project putting this DSL to work on
its own business rules will always turn up operations worth writing and not worth putting
on nuget.org.

The subject is a deployment pipeline. Three services × three stages get promoted, and
`GetValidInputs` answers "what can ship right now".

- Subject: [ruleset/deploy.json](../ruleset/deploy.json)
- Vocabulary: [sample/Deploy/DeployVocabulary.cs](../sample/Deploy/DeployVocabulary.cs) —
  the `acme` namespace, four ops
- Checked by: [test/DeployTests.cs](../test/DeployTests.cs), 12 cases
- Conclusion: **the core runtime needed no change.** `AddPlugin` was public from the start;
  what was needed was conventions and documentation.


## 1. What is different

| | The four before | The deployment pipeline |
| --- | --- | --- |
| where vocabulary comes from | a folder scan | twelve from a scan **plus one class in the host** |
| naming a plugin type | never | **yes** (`new DeployVocabulary(policy)`) |
| what an operation reads | its arguments | its arguments **plus an injected immutable snapshot** |
| `requires` | standard plugin names | the same, including `Acme.Deploy.Rules` |

The last row is the point. **Only the delivery route differs; not one thing about the
contract does.**

### The size of it

One day's worth, as placed by `state.initial` — 2026-08-12, a Wednesday.

- three services (billing / search / web), three stages (dev / staging / prod)
- three versions in `building`. Only prod requires two approvals and is subject to freezes
- search has two approvals and sits in staging, so it is **one step from prod**

The candidate space is deploy 3×3 + promote 3×3 + approve 3×3×4 = **54**, of which 19 are
legal.


## 2. The four ops — why the standard vocabulary cannot write them

The hardest thing about a sample for an in-process vocabulary is answering the reader's
"couldn't that just be a state field?". There are two kinds of answer here.

### 2.1 `acme.newer` — because it is an algorithm

Semantic version precedence. **A procedure rather than data**, so there is nothing to put
in the state. And the standard vocabulary gets it **wrong**:

```
                          which is newer, 2.4.0 or 2.4.0-rc.1
cmp.gt                    2.4.0-rc.1    ← string order; "2.4.0" is a shorter prefix, so it sorts first
acme.newer                2.4.0         ← correct; a release candidate precedes its release
```

Use `cmp` as it comes and **a release candidate overwrites the release and goes to
production.** This is the archetype of "worth adding a vocabulary for": **a lookup table can
usually be pushed into the state, and an algorithm cannot be pushed anywhere.**

In the initial state billing's dev holds `2.4.0-rc.1`, and `building` holds `2.3.5`,
`2.4.0-rc.1` and `2.4.0`. Exactly one `deploy` is offered, for `2.4.0` — pinned by
`APrereleaseDoesNotSupersedeTheReleaseItLeadsTo`.

### 2.2 `acme.frozen` / `acme.approvers` / `acme.people` — because they are injected tables

A freeze calendar and an ownership map. Both are

- owned by the organisation, and not the state of any individual deployment
- updated on their own schedule
- too large to embed in a state document, with no business being there

`acme.people` returns everyone in the ownership map. That is not a guard but **the domain of
`approve`'s `by` parameter**. A domain cannot depend on another parameter, so it comes in
two stages: the vocabulary supplies "everyone", and the guard narrows to the owners of that
service with `acme.approvers`.

### 2.3 Why only an in-process vocabulary can do this

`PluginProbe` requires a plugin found by scanning to be **public with a parameterless
constructor**. A distributed plugin is therefore structurally stateless and has nowhere to
keep the three above.

```csharp
// unwritable by scanning. Writable only on the instance route.
registry.AddExpression("frozen", context => new FrozenNode(_policy, context.RequireExpression("date")));
```

`acme.newer`, needing nothing but its arguments, is registered from a method group exactly
as a standard plugin would be. **Having both in one class is closer to how this actually
goes.**


## 3. Purity — the one real danger here

**`acme.frozen` takes the date as an argument.** It does not read the host's clock.

This is not a matter of style. `GetValidInputs` evaluates a guard once per candidate in a
domain — 54 times for this rule set. Let an operation reach outside and

- one round of I/O per candidate, so combinatorial blow-up becomes I/O blow-up
- **the same question gets different answers inside one call**
- snapshot semantics breaks, since "expressions read the state as the input found it" rests
  on the state being all they read

So "today" arrives as the field `state.today`.

> The person who does this is not the author of a distributed plugin. It is **the person
> writing a quick business rule inside their application**. Documenting the route officially
> means writing the contract down in both
> [the plugin specifications' "a vocabulary that is not distributed"](plugin/README.md) and
> the XML doc on `AddPlugin`.

`DeployPolicy` is immutable for the same reason. A node captures the instance at build
time, so a mutable one breaks concurrent evaluation of a `RuleContext`.


## 4. What worked

### 4.1 A state where the freeze is the only obstacle

In the initial state search has two approvals, sits in staging, and prod holds 2.3.4, so
`promote search prod` is **legal**.

```
$ dotnet run --project sample/Deploy -- --state friday
 2026-08-14   target 2.4.0
 search   2.4.0    2.4.0    2.3.4    sign prod/ann, sign prod/di      ← →prod is gone
 frozen: nothing ships on a Friday
 18 legal of 54 candidates evaluated
```

**One field of the state document differs** and `promote search prod` disappears. The reason
is written neither in `deploy.json` nor in the state. It is in a table inside the host.

With `--auto` every service climbs to staging, every signature is collected, and it stops
at `blocked`. **Everything in order and the date the only thing wrong** is a terminal state
worth being able to produce, and it is what made this subject worth choosing.

### 4.2 Replacing the injected table changes what is legal

```
                     legal / evaluated
default              19 / 54
--state friday       18 / 54     the freeze (from the state)
--policy lockdown    10 / 27     the freeze plus a single approver (from injected data)
```

lockdown reduces **even the number evaluated** (54 → 27). `by`'s domain comes from
`acme.people`, so fewer approvers shrinks the candidate space itself. Neither the rule set
nor the state changed by one character.

### 4.3 `requires` kept its meaning

```csharp
// compiling deploy.json on a runtime with only the twelve
RuleSetBuildException: ... Acme.Deploy.Rules ...
```

This is the whole of the case for not building a lighter registration API. A vocabulary
that was never published **remains a declared dependency**, and fails the same way a plugin
missing from the feed does.


## 5. The conventions this settled

| | |
| --- | --- |
| identifier and namespace | vendor-qualified (`Acme.Deploy.Rules` / `acme`). A plain name collides with a later published plugin |
| reserved prefix | **claim none.** One character per plugin, very few usable, not a resource for a vocabulary with one user |
| operations | pure functions of their arguments and an immutable snapshot. External data is injected through the constructor or carried in the state |
| version | removing an op or changing what one means means a new major |

`DeployVocabulary` satisfies all four. That its `ReservedPrefix` is `null` is pinned by a
test.


## 6. A by-product — the duplicated rule set documents were removed

Clearing the way for this subject meant dealing with the samples and the tests **holding
the same rule set as manual copies**.

- 4 documents × 2 places, about 2,400 lines kept in step by hand. The line endings had
  already drifted, so **nothing had compared them mechanically since the copy was made**
- now one copy in [ruleset/](../ruleset/), linked into both the tests and the samples by
  [RuleSets.props](../RuleSets.props)

The rule itself was restated as what it was actually for:

> ~~a sample's rule set is the same one the tests exercise~~
> **every rule set in this repository is actually run by a test**

Restated that way, testing `deploy.json` needs one `ProjectReference` from the test project
to `sample/Deploy`. An in-process vocabulary is **specifically about the host naming the
type**, so this does not conflict with the property `StandardPlugins.props` protects with
`ReferenceOutputAssembly="false"` — that a host cannot name a plugin type.


## 7. The gap this closed

[PluginLoadingTests](../test/PluginLoadingTests.cs) already used `AddPlugin`, but **only on
the failure paths** of the collision checks: duplicate id, duplicate namespace, duplicate
reserved character. The path where an in-process vocabulary actually registers ops, and a
rule set using them compiles and evaluates, was tested nowhere.

The 12 cases in `DeployTests` cover it.


## 8. Conclusion

- **No change to the core.** `AddPlugin(IRulealizePlugin)` was public from the start, and
  what was needed was conventions, documentation and a sample
- **No second registration mechanism.** `requires` is worth reading only because every
  vocabulary has a manifest
- The danger reduces to one thing. **Purity** — and it is a danger because an in-process
  vocabulary sits exactly where reaching outside is tempting. Documentation is the only
  defence
