# Vocabulary

The core provides no operations at all, so what a rule set may write is exactly what the
plugins loaded into the runtime provide. Which plugins those are is the rule set's own
business: `requires` names them, and `rulealize restore` fetches what it names.

**Nothing comes with anything.** There is no bundle and no metapackage — a plugin is an
ordinary package, and a folder holds the ones a document asked for. Which is why there is no
list of them here. A list would be a second account of something already recorded, kept in
step by hand, and the two would differ; the first symptom of that is a reader writing an `op`
against a table rather than against a runtime.

Where it is recorded is [Rulealize.Registry](https://github.com/reny-develop/Rulealize.Registry):
which plugin provides an operation, which namespaces are spoken for, and which shorthand
characters are in use by whom. It is not transcribed either. The ledger names packages, and
the catalogue is built by fetching each one, loading it and reading the operations back off
the assembly — the same folder scan a deployed application performs — so what it says cannot
disagree with what a runtime will do.

A specification says what one version of one vocabulary provides, and it is released by that
vocabulary rather than from here, as `doc/specification.md` in its own repository. The
versions move independently of each other, which is the whole reason `requires` carries a
constraint per plugin.

Every specification assumes two documents, and both are in `Rulealize.Abstraction` because
both describe types that package defines:
[the value model](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/value-model.md)
and [the notation the specifications are written in](https://github.com/reny-develop/Rulealize.Abstraction/blob/main/doc/specification-notation.md).

## Writing one

Writing a plugin is not this repository's subject: a plugin is written against
`Rulealize.Abstraction` and never references Rulealize. The starting point is
[`Rulealize.Templates`](https://github.com/reny-develop/Rulealize.Templates), and
[**writing a vocabulary**](https://github.com/reny-develop/Rulealize.Templates/blob/main/doc/writing-a-vocabulary.md)
is the guide it carries.

```sh
dotnet new install Rulealize.Templates
dotnet new rulealize-plugin -n Rulealize.Plugin.Example
```

## A vocabulary that is not distributed

A published vocabulary is found as a DLL in a folder. But `RuleRuntime.AddPlugin` takes an
**instance**, so a vocabulary does not have to be an assembly on disk to be one.

```csharp
RuleRuntime runtime = new RuleRuntime()
    .LoadPluginsFrom("plugin")
    .AddPlugin(new DeployVocabulary(freezeCalendar, ownershipMap));
```

A project using this library for its own business rules will always turn up operations
worth writing and not worth publishing. Rather than dressing one up as a plugin and
shipping it, implement `IRulealizePlugin` in your own assembly and hand it over this way.
The contract is identical to a published plugin's — it has a manifest, it claims a
namespace, and its name appears in the rule set's `requires`.

The worked example is [sample/Deploy/](../sample/Deploy/).

A rule set requiring `Acme.Deploy.Rules` is refused by a runtime without it **exactly as it
would be for a plugin that was missing from the feed**. The difference between the two
routes stays down to `new` versus a folder scan.

### What only an in-process vocabulary can do

A plugin discovered by scanning is required to be public with a parameterless constructor,
which makes it structurally stateless.

Passing an instance lifts that restriction, so a vocabulary **can be handed an immutable
snapshot loaded at start-up**. A holiday calendar, a price list, an org chart, an ownership
map — data someone else owns, updated on its own schedule, with no business being carried
in each individual state document.

### The identifier and the namespace are still claimed

The runtime refuses two plugins claiming one namespace when they are loaded, and `requires`
names the identifier — neither check knows or cares that one of them was never published.
Vendor-qualify both, `Acme.Deploy.Rules` and `acme` rather than `Rules` and `deploy`, and a
plugin released later cannot take the name out from under rule sets already in production.

### What an operation that reaches outside its arguments costs

Two facts about evaluation, stated here because they are the runtime's behaviour rather
than advice about how to write one:

- `GetValidInputs` evaluates a guard once per candidate in a parameter's domain. Whatever
  an operation reaches for, it reaches for once per candidate, so a domain of a thousand is
  a thousand of them.
- Snapshot semantics cover the state document — expressions read it as the input found it.
  Anything else an operation reads sits outside that guarantee, and one question can be
  answered two ways inside a single call.

Neither is a prohibition. An operation may read a clock or a database, and the current date
may be something it finds out rather than something a state field hands it; the runtime
does not stop it, and nothing about the difference reaches the rule set. The cost is the
two points above, and it is the author's to accept or to design around.

[`sample/Deploy`](../sample/Deploy/) designs around it: an immutable snapshot in the
constructor and `date` as an argument to `acme.frozen`, so a guard asked a thousand times
answers a thousand times at no cost and gives the same answer each time.
