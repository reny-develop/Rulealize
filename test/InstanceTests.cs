// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Store;

namespace Rulealize.Tests
{
    /// <summary>A rule set that keeps any number of instances of another, each as its own document.</summary>
    /// <remarks>
    /// <para>
    /// The month and its claims. A claim is one claim and knows nothing of months; the month
    /// holds any number of them, and the limit is a rule about all the claims of a month, so it
    /// is written in the month. Each claim is its own document, so creating one or entering an
    /// amount touches nothing else, and only submitting and rejecting — which move the month's
    /// <c>used</c> — write the month as well.
    /// </para>
    /// <para>
    /// The store is <see cref="MemoryStore"/>, a dictionary with a version per document, and the
    /// month names it in <c>requires</c>. The runtime reads and writes through it and knows no
    /// versions: it settles what a transition reads before it runs and hands over what it wrote
    /// afterwards, and the store checks the one against the other. The tests of what is read
    /// and written ask the pure overloads directly, which is what they stand on.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class InstanceTests(StandardRuntime standard)
    {
        private const string Requires = StandardRuntime.Requires;

        /// <summary>One claim: an amount entered while it is a draft, then submitted and decided.</summary>
        internal const string Claim = $$"""
            {
              "id": "claim", "version": "1.0.0",
            {{Requires}}
              "state": {
                "schema": {
                  "stage": { "op": "type.enum", "values": ["draft", "review", "granted", "denied"] },
                  "amount": { "op": "type.int", "min": 0, "max": 1000000 },
                  "approver": { "op": "type.enum", "values": ["ann", "bo"] }
                },
                "initial": { "stage": "draft", "amount": 0, "approver": "ann" }
              },
              "inputs": {
                "enterAmount": {
                  "params": { "amount": { "open": { "field": "amount" } } },
                  "when": { "op": "cmp.eq", "left": "$stage", "right": "draft" },
                  "effects": [ { "op": "state.set", "path": "amount", "value": "@amount" } ]
                },
                "submit": {
                  "params": { "by": { "domain": { "op": "seq.of", "of": ["ann", "bo", "cy"] } } },
                  "when": { "op": "logic.and", "all": [
                    { "op": "cmp.eq", "left": "$stage", "right": "draft" },
                    { "op": "cmp.gt", "left": "$amount", "right": 0 } ] },
                  "effects": [ { "op": "state.set", "path": "stage", "value": "review" } ]
                },
                "reject": {
                  "params": { "by": { "domain": { "op": "seq.of", "of": ["ann", "bo", "cy"] } } },
                  "when": { "op": "logic.and", "all": [
                    { "op": "cmp.eq", "left": "$stage", "right": "review" },
                    { "op": "cmp.eq", "left": "@by", "right": "$approver" } ] },
                  "effects": [ { "op": "state.set", "path": "stage", "value": "denied" } ]
                }
              }
            }
            """;

        /// <summary>Where every claim starts: who approves is said here, once, and in no rule set.</summary>
        internal const string ClaimStart = """
            { "$schema": "rulealize/state/v1", "ruleSet": "claim@1.0.0",
              "data": { "stage": "draft", "amount": 0, "approver": "bo" } }
            """;

        /// <summary>A month: its claims, and how much of its limit they have used.</summary>
        internal const string Month = $$"""
            {
              "id": "claim-month", "version": "0.1.0",
            {{MemoryStore.Requires}}
              "uses": [
                { "ruleSet": "claim", "version": "^1.0", "as": "claims",
                  "many": { "key": "generated", "start": "claim-start" } }
              ],
              "state": {
                "schema": {
                  "limit": { "op": "type.int", "min": 0, "max": 1000000 },
                  "used": { "op": "type.int", "min": 0, "max": 1000000 }
                },
                "initial": { "limit": 50000, "used": 0 }
              },
              "definitions": {
                "amountOf": {
                  "params": ["claim"],
                  "body": { "op": "rec.at", "key": "amount",
                            "record": { "op": "rec.at", "record": "@claim", "key": "state" } }
                }
              },
              "held": {
                "claims": {
                  "submit": { "when": false },
                  "reject": { "when": false }
                }
              },
              "inputs": {
                "create": { "creates": "claims" },
                "submit": {
                  "at": "claims",
                  "params": { "by": { "domain": { "op": "seq.of", "of": ["ann", "bo", "cy"] } } },
                  "when": {
                    "op": "cmp.lte",
                    "left": { "op": "math.add", "of": ["$used", { "op": "def.call", "def": "amountOf", "args": { "claim": "@claims" } }] },
                    "right": "$limit"
                  },
                  "fires": [ { "held": "claims", "input": "submit", "args": { "by": "@by" } } ],
                  "effects": [
                    { "op": "state.set", "path": "used",
                      "value": { "op": "math.add", "of": ["$used", { "op": "def.call", "def": "amountOf", "args": { "claim": "@claims" } }] } }
                  ]
                },
                "reject": {
                  "at": "claims",
                  "params": { "by": { "domain": { "op": "seq.of", "of": ["ann", "bo", "cy"] } } },
                  "fires": [ { "held": "claims", "input": "reject", "args": { "by": "@by" } } ],
                  "effects": [
                    { "op": "state.set", "path": "used",
                      "value": { "op": "math.sub", "left": "$used", "right": { "op": "def.call", "def": "amountOf", "args": { "claim": "@claims" } } } }
                  ]
                }
              },
              "projections": {
                "left": { "op": "math.sub", "left": "$limit", "right": "$used" },
                "claims": {
                  "op": "seq.select", "source": "$claims", "as": "c",
                  "select": {
                    "key": { "op": "rec.at", "record": "@c", "key": "key" },
                    "stage": { "op": "rec.at", "key": "stage", "record": { "op": "rec.at", "record": "@c", "key": "state" } },
                    "amount": { "op": "def.call", "def": "amountOf", "args": { "claim": "@c" } }
                  }
                },
                "consistent": {
                  "op": "cmp.eq", "left": "$used",
                  "right": {
                    "op": "seq.sum", "source": "$claims", "as": "c",
                    "select": {
                      "op": "branch.if",
                      "cond": { "op": "cmp.eq", "right": "review",
                                "left": { "op": "rec.at", "key": "stage", "record": { "op": "rec.at", "record": "@c", "key": "state" } } },
                      "then": { "op": "def.call", "def": "amountOf", "args": { "claim": "@c" } },
                      "else": 0
                    }
                  }
                }
              }
            }
            """;

        private static readonly StateAddress MonthAddress = StateAddress.Root;

        [Fact]
        public void CreatingAClaimWritesTheNewClaimAndNothingElse()
        {
            MemoryStore store = new();
            RuleContext month = Context(store);

            TransitionResult result = month.ApplyToState(Input("create"), store.Read(month, Input("create")));

            StateWrite write = Assert.Single(result.Writes);
            Assert.True(write.IsInsert);
            Assert.Equal(StateAddress.Of("claims", "~1"), write.Address);
            Assert.Equal("bo", Data(write.State).GetProperty("approver").GetString());
        }

        [Fact]
        public async Task TheStoreAssignsTheKeyAndTheCommitRecordsIt()
        {
            MemoryStore store = new();
            RuleContext month = Context(store);

            CommittedTransition created = await month.ApplyAsync(Input("create"));

            using JsonDocument recorded = JsonDocument.Parse(created.Commit);
            Assert.Equal("rulealize/commit/v1", recorded.RootElement.GetProperty("$schema").GetString());
            Assert.Equal("create", recorded.RootElement.GetProperty("input").GetString());
            Assert.Equal("c-1", recorded.RootElement.GetProperty("created").GetProperty("~1").GetString());
            Assert.True(store.Holds(StateAddress.Of("claims", "c-1")));
        }

        [Fact]
        public async Task ATransitionReadsAndCommitsThroughOneSession()
        {
            MemoryStore store = new();
            RuleContext month = Context(store);

            await month.ApplyAsync(Input("create"));

            Assert.Equal(1, store.Sessions);
        }

        [Fact]
        public async Task TheMonthsOwnDocumentIsItsInitialStateUntilSomethingWritesIt()
        {
            MemoryStore store = new();
            RuleContext month = Context(store);

            await month.ApplyAsync(Input("create"));

            Assert.False(store.Holds(MonthAddress));
            Assert.Equal("50000", await month.ProjectAsync("left"));
        }

        [Fact]
        public async Task ReplayingACreationWithItsCommitWritesTheSameKey()
        {
            MemoryStore store = new();
            RuleContext month = Context(store);
            CommittedTransition created = await month.ApplyAsync(Input("create"));

            TransitionResult replayed = month.ApplyToState(Input("create"), store.Read(month, Input("create")), created.Commit);

            Assert.Equal(StateAddress.Of("claims", "c-1"), Assert.Single(replayed.Writes).Address);
        }

        [Fact]
        public async Task AHistoryReplayedIntoAnEmptyStoreReproducesIt()
        {
            MemoryStore store = new();
            RuleContext month = Context(store);
            List<(string Input, string Commit)> history = [];
            foreach (string input in new[]
            {
                Input("create"),
                Input("create"),
                Input("claims.enterAmount", "c-2", """{ "amount": 32000 }"""),
                Input("submit", "c-2", """{ "by": "ann" }""")
            })
            {
                history.Add((input, (await month.ApplyAsync(input)).Commit));
            }

            MemoryStore rebuilt = new();
            RuleContext again = Context(rebuilt);
            foreach ((string input, string commit) in history)
            {
                await again.ApplyAsync(input, commit);
            }

            Assert.Equal(await month.ProjectAsync("claims"), await again.ProjectAsync("claims"));
            Assert.Equal(await month.ProjectAsync("left"), await again.ProjectAsync("left"));
        }

        [Fact]
        public async Task ReplayingACreationOverAnInstanceAlreadyThereCollides()
        {
            MemoryStore store = new();
            RuleContext month = Context(store);
            CommittedTransition created = await month.ApplyAsync(Input("create"));

            StateConflictException conflict = await Assert.ThrowsAsync<StateConflictException>(
                () => month.ApplyAsync(Input("create"), created.Commit));

            Assert.Equal(StateAddress.Of("claims", "c-1"), conflict.Address);
        }

        [Fact]
        public void AnInputAtAClaimReadsTheMonthAndThatClaim()
        {
            RuleContext month = Context(new MemoryStore());

            ImmutableArray<StateAddress> reads = month.GetReads(Input("claims.enterAmount", "c-3", """{ "amount": 1 }"""));

            Assert.Equal([MonthAddress, StateAddress.Of("claims", "c-3")], reads.AsEnumerable());
        }

        [Fact]
        public async Task EnteringAnAmountWritesOnlyTheClaim()
        {
            MemoryStore store = new();
            RuleContext month = Context(store);
            await month.ApplyAsync(Input("create"));

            string input = Input("claims.enterAmount", "c-1", """{ "amount": 32000 }""");
            TransitionResult result = month.ApplyToState(input, store.Read(month, input));

            StateWrite write = Assert.Single(result.Writes);
            Assert.Equal(StateAddress.Of("claims", "c-1"), write.Address);
            Assert.Equal(32000, Data(write.State).GetProperty("amount").GetInt32());
        }

        [Fact]
        public async Task SubmittingWritesTheMonthAndTheClaim()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 32000);

            string input = Input("submit", "c-1", """{ "by": "ann" }""");
            TransitionResult result = month.ApplyToState(input, store.Read(month, input));

            Assert.Equal(
                [MonthAddress, StateAddress.Of("claims", "c-1")],
                result.Writes.Select(static write => write.Address));
            Assert.Equal(32000, Data(result.Writes[0].State).GetProperty("used").GetInt32());
            Assert.Equal("review", Data(result.Writes[1].State).GetProperty("stage").GetString());
        }

        [Fact]
        public async Task ASubmissionThatDoesNotFitInTheMonthIsRefused()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 60000);

            await Assert.ThrowsAsync<IllegalInputException>(
                () => month.ApplyAsync(Input("submit", "c-1", """{ "by": "ann" }""")));
            Assert.False(store.Holds(MonthAddress));
        }

        [Fact]
        public async Task RejectingAClaimGivesItsAmountBack()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 32000);
            await month.ApplyAsync(Input("submit", "c-1", """{ "by": "ann" }"""));

            await month.ApplyAsync(Input("reject", "c-1", """{ "by": "bo" }"""));

            Assert.Equal(0, Data(store[MonthAddress]).GetProperty("used").GetInt32());
            Assert.Equal("denied", Data(store[StateAddress.Of("claims", "c-1")]).GetProperty("stage").GetString());
            Assert.Equal("50000", await month.ProjectAsync("left"));
        }

        [Fact]
        public async Task AProjectionReadsEveryClaimInTheOrderTheyWereCreated()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 32000);
            for (int i = 0; i < 10; i++)
            {
                await month.ApplyAsync(Input("create"));
            }

            // Ordinal order would put c-10 and c-11 straight after c-1.
            using JsonDocument answer = JsonDocument.Parse(await month.ProjectAsync("claims"));
            Assert.Equal(
                Enumerable.Range(1, 11).Select(static n => $"c-{n}"),
                answer.RootElement.EnumerateArray().Select(static row => row.GetProperty("key").GetString()));
            Assert.Equal(32000, answer.RootElement[0].GetProperty("amount").GetInt32());
        }

        [Fact]
        public async Task AMonthWithNoClaimsProjectsNone()
        {
            RuleContext month = Context(new MemoryStore());

            Assert.Equal("[]", await month.ProjectAsync("claims"));
        }

        [Fact]
        public async Task TheMonthCanSayWhetherItsUseIsWhatItsClaimsSay()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 32000);
            await month.ApplyAsync(Input("submit", "c-1", """{ "by": "ann" }"""));

            Assert.Equal("true", await month.ProjectAsync("consistent"));

            await month.ApplyAsync(Input("reject", "c-1", """{ "by": "bo" }"""));

            Assert.Equal("true", await month.ProjectAsync("consistent"));
        }

        [Fact]
        public void AProjectionOverEveryClaimRefusesOneDocument()
        {
            RuleContext month = Context(new MemoryStore());

            Assert.Throws<InvalidOperationException>(() => month.Project("claims", month.InitialState));
        }

        [Fact]
        public void AProjectionIsNotAskedAboutAnUncommittedClaim()
        {
            RuleContext month = Context(new MemoryStore());
            Dictionary<string, IReadOnlyList<KeyValuePair<string, string>>> collections = new(StringComparer.Ordinal)
            {
                ["claims"] = [KeyValuePair.Create("~1", ClaimStart)]
            };

            Assert.Throws<RuleDocumentException>(() => month.Project("claims", month.InitialState, collections));
        }

        [Fact]
        public void AGuardMayNotReadEveryClaim()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(new MemoryStore(), Month.Replace(
                """
                "create": { "creates": "claims" },
                """,
                """
                "create": { "creates": "claims", "when": { "op": "cmp.lt", "left": { "op": "seq.count", "source": "$claims" }, "right": 3 } },
                """,
                StringComparison.Ordinal)));

            Assert.Contains("/inputs/create/when", refused.Message, StringComparison.Ordinal);
            Assert.Contains("only a projection may read that", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ADefinitionMayNotReadEveryClaim()
        {
            // A definition may be called from a guard, so a body reading every claim would be one.
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(new MemoryStore(), Month.Replace(
                """
                "definitions": {
                """,
                """
                "definitions": { "all": "$claims",
                """,
                StringComparison.Ordinal)));

            Assert.Contains("only a projection may read that", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ACollectionIsNotAFieldToWrite()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(new MemoryStore(), Month.Replace(
                """
                "create": { "creates": "claims" },
                """,
                """
                "create": { "creates": "claims", "effects": [ { "op": "state.set", "path": "claims", "value": 0 } ] },
                """,
                StringComparison.Ordinal)));

            Assert.Contains("only a projection may read that", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheClaimsOwnRejectIsDrivenOnlyThroughTheMonth()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 32000);
            await month.ApplyAsync(Input("submit", "c-1", """{ "by": "ann" }"""));

            await Assert.ThrowsAsync<IllegalInputException>(
                () => month.ApplyAsync(Input("claims.reject", "c-1", """{ "by": "bo" }""")));
        }

        [Fact]
        public async Task TheMonthOffersCreationAtItsOwnAddress()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 32000);

            ValidInputSet offered = await month.GetValidInputsAsync(100);

            Assert.Equal(["create"], offered.Select(static input => input.ToString()));
        }

        [Fact]
        public async Task AClaimIsOfferedItsMovesAtItsOwnAddress()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 32000);

            ValidInputSet offered = await month.GetValidInputsAsync(StateAddress.Of("claims", "c-1"), 100);

            Assert.Equal(
                ["submit(by: ann)", "submit(by: bo)", "submit(by: cy)", "claims.enterAmount(amount: <type.int>)"],
                offered.Select(static input => input.ToString()));
            Assert.All(offered, static input => Assert.Equal(StateAddress.Of("claims", "c-1"), input.At));
        }

        [Fact]
        public async Task AMoveFoundAtAClaimIsWrittenWithItsAt()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 32000);

            ValidInput submit = (await month.GetValidInputsAsync(StateAddress.Of("claims", "c-1"), 100))[0];
            await month.ApplyAsync(submit.ToInputDocument(month.RuleSet));

            Assert.Equal(32000, Data(store[MonthAddress]).GetProperty("used").GetInt32());
        }

        [Fact]
        public async Task AnAddressNoInstanceCanHaveIsRefusedBeforeAnythingIsRead()
        {
            MemoryStore store = new();
            RuleContext month = Context(store);

            await Assert.ThrowsAsync<ArgumentException>(() => month.GetValidInputsAsync(StateAddress.Of("months", "c-1"), 100));
            Assert.Equal(0, store.Sessions);
        }

        [Fact]
        public async Task TwoSubmissionsInOneMonthCollideOnTheMonth()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 10000);
            await month.ApplyAsync(Input("create"));
            await month.ApplyAsync(Input("claims.enterAmount", "c-2", """{ "amount": 10000 }"""));

            store.BeforeNextCommit = () => month.ApplyAsync(Input("submit", "c-1", """{ "by": "ann" }"""));
            StateConflictException stale = await Assert.ThrowsAsync<StateConflictException>(
                () => month.ApplyAsync(Input("submit", "c-2", """{ "by": "ann" }""")));

            Assert.Equal(MonthAddress, stale.Address);
            Assert.Equal("draft", Data(store[StateAddress.Of("claims", "c-2")]).GetProperty("stage").GetString());
        }

        [Fact]
        public async Task AnAmountEnteredMeanwhileDoesNotCollideWithASubmission()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 10000);
            await month.ApplyAsync(Input("create"));

            store.BeforeNextCommit = () => month.ApplyAsync(Input("claims.enterAmount", "c-2", """{ "amount": 5 }"""));
            await month.ApplyAsync(Input("submit", "c-1", """{ "by": "ann" }"""));

            Assert.Equal(5, Data(store[StateAddress.Of("claims", "c-2")]).GetProperty("amount").GetInt32());
            Assert.Equal("review", Data(store[StateAddress.Of("claims", "c-1")]).GetProperty("stage").GetString());
        }

        [Fact]
        public void AnInputAtAClaimNeedsTheInputDocumentToSayWhichClaim()
        {
            RuleContext month = Context(new MemoryStore());

            RuleDocumentException refused = Assert.Throws<RuleDocumentException>(
                () => month.GetReads(Input("submit", null, """{ "by": "ann" }""")));
            Assert.Contains("does not say which", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ATemporaryKeyCannotBeWhereAnInputIs()
        {
            RuleContext month = Context(new MemoryStore());

            RuleDocumentException refused = Assert.Throws<RuleDocumentException>(
                () => month.GetReads(Input("submit", "~1", """{ "by": "ann" }""")));
            Assert.Contains("temporary key", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ARuleSetThatKeepsInstancesRefusesOneDocument()
        {
            RuleContext month = Context(new MemoryStore());

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => month.GetValidInputs(month.InitialState, 10));
            Assert.Contains("GetValidInputsAsync", refused.Message, StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() => month.ApplyToState(Input("create"), month.InitialState));
        }

        [Fact]
        public async Task ARuleSetThatKeepsNoInstancesIsInNoStore()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => standard.Countdown.ApplyAsync("""{ "input": "add", "args": { "n": 1 } }"""));
            await Assert.ThrowsAsync<InvalidOperationException>(() => standard.Countdown.GetValidInputsAsync(10));
        }

        [Fact]
        public void ARuleSetThatKeepsInstancesNamesTheStoreTheyAreKeptIn()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(
                new MemoryStore(),
                Month.Replace(MemoryStore.Requires, Requires, StringComparison.Ordinal)));

            Assert.Contains("/uses[0]/many", refused.Message, StringComparison.Ordinal);
            Assert.Contains("names no store", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ARuleSetKeepsItsInstancesInOneStore()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(
                new MemoryStore(),
                Month.Replace(
                    $$"""{ "plugin": "{{MemoryStore.Plugin}}" }""",
                    $$"""{ "plugin": "{{MemoryStore.Plugin}}" }, { "plugin": "{{MemoryStore.OtherPlugin}}" }""",
                    StringComparison.Ordinal)));

            Assert.Contains("/requires", refused.Message, StringComparison.Ordinal);
            Assert.Contains("in one", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AStoreTheRuntimeWasNotGivenIsNamed()
        {
            RuleContext month = new RuleRuntime()
                .LoadPluginsFrom(StandardRuntime.PluginFolder)
                .AddPlugin(new MemoryStorePlugin(MemoryStore.Plugin, "memory"))
                .CreateContext(Month, Held, Starts());

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => month.ApplyAsync(Input("create")));
            Assert.Contains(MemoryStore.Plugin, refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void OnlyAStorePluginIsGivenAStore()
        {
            RuleRuntime runtime = MemoryStore.Runtime(new MemoryStore());

            Assert.Throws<InvalidOperationException>(() => runtime.UseStore("Rulealize.Plugin.Logic", new MemoryStore()));
            Assert.Throws<InvalidOperationException>(() => runtime.UseStore("Rulealize.Tests.Nothing", new MemoryStore()));
        }

        [Fact]
        public void AStoreIsTheKindItsPluginDeclared()
        {
            RuleRuntime runtime = MemoryStore.Runtime(new MemoryStore());

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
                () => runtime.UseStore(MemoryStore.Plugin, new ForeignStore()));
            Assert.Contains(nameof(ForeignStore), refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AnInputAtAClaimThatDrivesNoneOfItsInputsIsRefused()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(new MemoryStore(), Month.Replace(
                """
                "fires": [ { "held": "claims", "input": "reject", "args": { "by": "@by" } } ],
                """,
                string.Empty,
                StringComparison.Ordinal)));

            Assert.Contains("a document it does not write", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void DrivingAClaimTakesAnAt()
        {
            const string At = "\"at\": \"claims\",";
            string unaddressed = Month.Remove(Month.LastIndexOf(At, StringComparison.Ordinal), At.Length);

            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(new MemoryStore(), unaddressed));

            Assert.Contains("Write \"at\": \"claims\"", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AGuardOverAClaimsOwnInputMayNotReadTheMonth()
        {
            // claims.enterAmount writes the claim and never the month, so a store checking the
            // claim's version would not see the month move between reading and committing.
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(new MemoryStore(), Month.Replace(
                """
                "submit": { "when": false },
                """,
                """
                "submit": { "when": false }, "enterAmount": { "when": { "op": "cmp.lt", "left": "$used", "right": "$limit" } },
                """,
                StringComparison.Ordinal)));

            Assert.Contains("/held/claims/enterAmount/when", refused.Message, StringComparison.Ordinal);
            Assert.Contains("never this rule set's document", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AGuardOverAClaimsOwnInputMayReadTheClaim()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 0, Month.Replace(
                """
                "submit": { "when": false },
                """,
                """
                "submit": { "when": false }, "enterAmount": { "when": { "op": "cmp.eq", "left": { "op": "def.call", "def": "amountOf", "args": { "claim": "@claims" } }, "right": 0 } },
                """,
                StringComparison.Ordinal));

            await month.ApplyAsync(Input("claims.enterAmount", "c-1", """{ "amount": 5 }"""));

            await Assert.ThrowsAsync<IllegalInputException>(
                () => month.ApplyAsync(Input("claims.enterAmount", "c-1", """{ "amount": 6 }""")));
        }

        [Fact]
        public void CreatingMayNotBeDecidedOnTheMonthItDoesNotWrite()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(new MemoryStore(), Month.Replace(
                """
                "create": { "creates": "claims" },
                """,
                """
                "create": { "creates": "claims", "when": { "op": "cmp.lt", "left": "$used", "right": "$limit" } },
                """,
                StringComparison.Ordinal)));

            Assert.Contains("/inputs/create/when", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AReadThroughADefinitionIsARead()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(new MemoryStore(), Month
                .Replace(
                    """
                    "definitions": {
                    """,
                    """
                    "definitions": { "open": { "op": "cmp.lt", "left": "$used", "right": "$limit" }, "stillOpen": "#open",
                    """,
                    StringComparison.Ordinal)
                .Replace(
                    """
                    "create": { "creates": "claims" },
                    """,
                    """
                    "create": { "creates": "claims", "when": "#stillOpen" },
                    """,
                    StringComparison.Ordinal)));

            Assert.Contains("/inputs/create/when", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void DrivingAClaimWithoutWritingTheMonthMayNotReadTheMonth()
        {
            // reject without its effect writes the claim alone, and its argument would be
            // worked out from a month nothing checks.
            string month = Month.Replace(
                """
                "fires": [ { "held": "claims", "input": "reject", "args": { "by": "@by" } } ],
                """,
                """
                "fires": [ { "held": "claims", "input": "reject", "args": { "by": { "op": "branch.if", "cond": { "op": "cmp.gt", "left": "$used", "right": 0 }, "then": "@by", "else": "ann" } } } ],
                """,
                StringComparison.Ordinal);
            int effects = month.LastIndexOf("\"effects\"", StringComparison.Ordinal);
            int projections = month.IndexOf("\"projections\"", StringComparison.Ordinal);
            month = month[..effects] + "\"effects\": [] } }," + month[projections..];

            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(new MemoryStore(), month));

            Assert.Contains("/inputs/reject/fires", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AStartWrittenForAnotherMajorVersionIsRefused()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(
                new MemoryStore(),
                Month,
                ClaimStart.Replace("claim@1.0.0", "claim@2.0.0", StringComparison.Ordinal)));

            Assert.Contains("claim@2.0.0", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AStartThatIsNotAStateOfTheClaimIsRefused()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(
                new MemoryStore(),
                Month,
                ClaimStart.Replace("\"bo\"", "\"di\"", StringComparison.Ordinal)));

            Assert.Contains("approver", refused.Message, StringComparison.Ordinal);
        }

        /// <summary>The month, with an input that creates a claim and enters its amount in one transition.</summary>
        internal static readonly string Filing = Month.Replace(
            "\"create\": { \"creates\": \"claims\" },",
            """
            "create": { "creates": "claims" },
                            "file": {
                              "creates": "claims",
                              "params": { "amount": { "domain": { "op": "seq.of", "of": [100, 32000] } } },
                              "fires": [ { "held": "claims", "input": "enterAmount", "args": { "amount": "@amount" } } ]
                            },
                            "fileEmpty": {
                              "creates": "claims",
                              "fires": [ { "held": "claims", "input": "submit", "args": { "by": "ann" } } ]
                            },
            """,
            StringComparison.Ordinal);

        [Fact]
        public void CreatingAClaimAndEnteringItsAmountWritesTheNewClaimOnce()
        {
            MemoryStore store = new();
            RuleContext month = Context(store, Filing);
            string input = Input("file", args: """{ "amount": 32000 }""");

            TransitionResult result = month.ApplyToState(input, store.Read(month, input));

            StateWrite write = Assert.Single(result.Writes);
            Assert.True(write.IsInsert);
            Assert.Equal(StateAddress.Of("claims", "~1"), write.Address);
            Assert.Equal(32000, Data(write.State).GetProperty("amount").GetInt32());
            Assert.Equal("bo", Data(write.State).GetProperty("approver").GetString());
        }

        [Fact]
        public async Task AClaimCreatedAndDrivenIsKeptUnderTheKeyTheStoreAssigns()
        {
            MemoryStore store = new();
            RuleContext month = Context(store, Filing);

            CommittedTransition filed = await month.ApplyAsync(Input("file", args: """{ "amount": 32000 }"""));

            Assert.Equal(StateAddress.Of("claims", "c-1"), filed.At);
            Assert.Equal(32000, Data(store[StateAddress.Of("claims", "c-1")]).GetProperty("amount").GetInt32());
            Assert.False(store.Holds(MonthAddress));
        }

        [Fact]
        public async Task ACreationThatDrovePlaysBackFromItsCommit()
        {
            MemoryStore store = new();
            RuleContext month = Context(store, Filing);
            string input = Input("file", args: """{ "amount": 32000 }""");
            CommittedTransition filed = await month.ApplyAsync(input);

            MemoryStore rebuilt = new();
            RuleContext again = Context(rebuilt, Filing);
            await again.ApplyAsync(input, filed.Commit);

            Assert.Equal(store[StateAddress.Of("claims", "c-1")], rebuilt[StateAddress.Of("claims", "c-1")]);
        }

        [Fact]
        public async Task WhatACreationDrivesIsAskedOfTheInstanceAsItStarts()
        {
            MemoryStore store = new();
            RuleContext month = Context(store, Filing);

            ValidInputSet offered = await month.GetValidInputsAsync(100);

            Assert.Contains("file(amount: 32000)", offered.Select(static input => input.ToString()));
            Assert.DoesNotContain("fileEmpty", offered.Select(static input => input.Input));
            await Assert.ThrowsAsync<IllegalInputException>(() => month.ApplyAsync(Input("fileEmpty")));
            Assert.False(store.Holds(StateAddress.Of("claims", "c-1")));
        }

        [Fact]
        public void AnInstanceBeingCreatedHoldsNothingToBeAt()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => MemoryStore.Runtime(new MemoryStore()).CreateContext(
                $$"""
                {
                  "id": "claim-year", "version": "0.1.0",
                {{MemoryStore.Requires}}
                  "uses": [
                    { "ruleSet": "claim-month", "version": "^0.1", "as": "months", "many": { "key": "generated" } }
                  ],
                  "inputs": {
                    "open": { "creates": "months", "fires": [ { "held": "months", "input": "create" } ] }
                  }
                }
                """,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["claim-month"] = Month, ["claim"] = Claim },
                Starts()));

            Assert.Contains("/inputs/open/fires[0]/input", refused.Message, StringComparison.Ordinal);
            Assert.Contains("holds no instances yet", refused.Message, StringComparison.Ordinal);
        }

        private static readonly Dictionary<string, string> Held = new(StringComparer.Ordinal) { ["claim"] = Claim };

        private static Dictionary<string, string> Starts(string start = ClaimStart) =>
            new(StringComparer.Ordinal) { ["claim-start"] = start };

        /// <summary>The month, kept in a store of its own.</summary>
        private static RuleContext Context(MemoryStore store, string month = Month, string start = ClaimStart) =>
            MemoryStore.Runtime(store).CreateContext(month, Held, Starts(start));

        /// <summary>A month with one claim, c-1, drafted at an amount.</summary>
        private static async Task<RuleContext> Drafted(MemoryStore store, int amount, string month = Month)
        {
            RuleContext context = Context(store, month);
            await context.ApplyAsync(Input("create"));
            await context.ApplyAsync(Input("claims.enterAmount", "c-1", $$"""{ "amount": {{amount}} }"""));
            return context;
        }

        private static string Input(string input, string? claim = null, string args = "{}") =>
            claim is null
                ? $$"""{ "input": "{{input}}", "args": {{args}} }"""
                : $$"""{ "input": "{{input}}", "at": { "claims": "{{claim}}" }, "args": {{args}} }""";

        private static JsonElement Data(string state) =>
            JsonDocument.Parse(state).RootElement.GetProperty("data").Clone();

        /// <summary>A store, but not the kind <see cref="MemoryStorePlugin"/> declared.</summary>
        private sealed class ForeignStore : IStateStore
        {
            public Task<IStateStoreSession> OpenSessionAsync(CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();
        }
    }
}
