// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Store;

namespace Rulealize.Tests
{
    /// <summary>A rule set that keeps instances of one that keeps instances of its own, under keys written as a schema.</summary>
    /// <remarks>
    /// <para>
    /// The ledger holds every month, and each month holds its claims. A month's key is written
    /// as a schema, so every key the schema admits is a month already: one nobody has written
    /// to is at the month's initial state, and nothing creates one. A claim's key is still the
    /// store's to assign. Which month a claim belongs to is where it is kept —
    /// <c>months/2026-10/claims/c-1</c> — so the claim knows nothing of months.
    /// </para>
    /// <para>
    /// The month is <see cref="InstanceTests.Month"/> unchanged, and the store is the same
    /// <see cref="MemoryStore"/>: nesting adds addresses and nothing a store has to know. A month
    /// may name a different store from the ledger's, and then a transition that writes both is
    /// handed to a coordinator split per store.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class LedgerTests
    {
        private const string Requires = MemoryStore.Requires;

        /// <summary>Every month. It has no state and no inputs of its own: it offers what the months offer.</summary>
        private const string Ledger = $$"""
            {
              "id": "claim-ledger", "version": "0.1.0",
            {{Requires}}
              "uses": [
                { "ruleSet": "claim-month", "version": "^0.1", "as": "months",
                  "many": { "key": { "op": "type.string", "minLength": 7, "maxLength": 7 } } }
              ]
            }
            """;

        private const string October = "2026-10";

        private static readonly StateAddress OctoberAddress = StateAddress.Of("months", October);

        /// <summary>The month, keeping its claims in the other store rather than the ledger's.</summary>
        private static readonly string MonthElsewhere = InstanceTests.Month.Replace(
            $$"""{ "plugin": "{{MemoryStore.Plugin}}" }""",
            $$"""{ "plugin": "{{MemoryStore.OtherPlugin}}" }""",
            StringComparison.Ordinal);

        [Fact]
        public void CreatingAClaimWritesItUnderItsMonthAndNothingElse()
        {
            MemoryStore store = new();
            RuleContext ledger = Context(store);

            TransitionResult result = ledger.ApplyToState(Input("months.create"), store.Read(ledger, Input("months.create")));

            StateWrite write = Assert.Single(result.Writes);
            Assert.True(write.IsInsert);
            Assert.Equal(OctoberAddress.Child("claims", "~1"), write.Address);
        }

        [Fact]
        public void AMonthNobodyHasWrittenIsReadWithoutADocument()
        {
            MemoryStore store = new();
            RuleContext ledger = Context(store);

            Assert.Equal([StateAddress.Root, OctoberAddress], ledger.GetReads(Input("months.create")).AsEnumerable());
            Assert.False(store.Read(ledger, Input("months.create")).ContainsKey(OctoberAddress));
        }

        [Fact]
        public async Task TheStoreAssignsTheClaimsKeyUnderItsMonth()
        {
            MemoryStore store = new();
            RuleContext ledger = Context(store);

            await ledger.ApplyAsync(Input("months.create"));

            Assert.True(store.Holds(OctoberAddress.Child("claims", "c-1")));
            Assert.False(store.Holds(OctoberAddress));
            Assert.False(store.Holds(StateAddress.Root));
        }

        [Fact]
        public async Task ReplayingACreationInAMonthWritesTheSameKey()
        {
            MemoryStore store = new();
            RuleContext ledger = Context(store);
            CommittedTransition created = await ledger.ApplyAsync(Input("months.create"));

            TransitionResult replayed = ledger.ApplyToState(
                Input("months.create"), store.Read(ledger, Input("months.create")), created.Commit);

            Assert.Equal(OctoberAddress.Child("claims", "c-1"), Assert.Single(replayed.Writes).Address);
        }

        [Fact]
        public void AnInputOfAClaimReadsTheMonthAndTheClaim()
        {
            RuleContext ledger = Context(new MemoryStore());

            Assert.Equal(
                [StateAddress.Root, OctoberAddress, OctoberAddress.Child("claims", "c-1")],
                ledger.GetReads(Input("months.claims.enterAmount", "c-1", """{ "amount": 5 }""")).AsEnumerable());
        }

        [Fact]
        public async Task EnteringAnAmountWritesOnlyTheClaim()
        {
            MemoryStore store = new();
            RuleContext ledger = Context(store);
            await ledger.ApplyAsync(Input("months.create"));

            string input = Input("months.claims.enterAmount", "c-1", """{ "amount": 32000 }""");
            TransitionResult result = ledger.ApplyToState(input, store.Read(ledger, input));

            StateWrite write = Assert.Single(result.Writes);
            Assert.Equal(OctoberAddress.Child("claims", "c-1"), write.Address);
            Assert.Equal(32000, Data(write.State).GetProperty("amount").GetInt32());
        }

        [Fact]
        public async Task TheFirstSubmissionWritesTheMonthForTheFirstTime()
        {
            MemoryStore store = new();
            RuleContext ledger = await Drafted(Context(store), October, 32000);

            await ledger.ApplyAsync(Input("months.submit", "c-1", """{ "by": "ann" }"""));

            Assert.Equal(32000, Data(store[OctoberAddress]).GetProperty("used").GetInt32());
            Assert.Equal("review", Data(store[OctoberAddress.Child("claims", "c-1")]).GetProperty("stage").GetString());
        }

        [Fact]
        public async Task EachMonthHasALimitOfItsOwn()
        {
            MemoryStore store = new();
            RuleContext ledger = await Drafted(Context(store), October, 50000);
            await ledger.ApplyAsync(Input("months.submit", "c-1", """{ "by": "ann" }"""));

            await ledger.ApplyAsync(Input("months.create", month: "2026-11"));
            await ledger.ApplyAsync(Input("months.claims.enterAmount", "c-2", """{ "amount": 50000 }""", "2026-11"));
            await ledger.ApplyAsync(Input("months.submit", "c-2", """{ "by": "ann" }""", "2026-11"));

            Assert.Equal(50000, Data(store[StateAddress.Of("months", "2026-11")]).GetProperty("used").GetInt32());
        }

        [Fact]
        public async Task TwoFirstSubmissionsInOneMonthCollideOnTheMonth()
        {
            MemoryStore store = new();
            RuleContext ledger = await Drafted(Context(store), October, 10000);
            await ledger.ApplyAsync(Input("months.create"));
            await ledger.ApplyAsync(Input("months.claims.enterAmount", "c-2", """{ "amount": 10000 }"""));

            store.BeforeNextCommit = () => ledger.ApplyAsync(Input("months.submit", "c-1", """{ "by": "ann" }"""));
            StateConflictException stale = await Assert.ThrowsAsync<StateConflictException>(
                () => ledger.ApplyAsync(Input("months.submit", "c-2", """{ "by": "ann" }""")));

            Assert.Equal(OctoberAddress, stale.Address);
        }

        [Fact]
        public async Task AClaimIsReachedOnlyInTheMonthItWasCreatedIn()
        {
            MemoryStore store = new();
            RuleContext ledger = await Drafted(Context(store), October, 32000);

            RuleDocumentException missing = await Assert.ThrowsAsync<RuleDocumentException>(
                () => ledger.ApplyAsync(Input("months.claims.enterAmount", "c-1", """{ "amount": 5 }""", "2026-11")));

            Assert.Contains("months/2026-11/claims/c-1", missing.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AKeyTheSchemaRefusesIsNoMonth()
        {
            RuleContext ledger = Context(new MemoryStore());

            RuleDocumentException refused = Assert.Throws<RuleDocumentException>(
                () => ledger.GetReads(Input("months.create", month: "2026-1")));

            Assert.Contains("not a key of 'months'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AnInputOfAClaimNeedsBothKeys()
        {
            RuleContext ledger = Context(new MemoryStore());
            const string input = """{ "input": "months.claims.enterAmount", "at": { "claims": "c-1" }, "args": { "amount": 5 } }""";

            RuleDocumentException refused = Assert.Throws<RuleDocumentException>(() => ledger.GetReads(input));

            Assert.Contains("one instance of 'months'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AMonthIsOfferedCreationAtItsOwnAddress()
        {
            RuleContext ledger = Context(new MemoryStore());

            ValidInputSet offered = await ledger.GetValidInputsAsync(OctoberAddress, 100);

            Assert.Equal(["months.create"], offered.Select(static input => input.ToString()));
        }

        [Fact]
        public async Task TheLedgerOffersNothingAtItsOwnAddress()
        {
            RuleContext ledger = Context(new MemoryStore());

            Assert.Empty(await ledger.GetValidInputsAsync(100));
        }

        [Fact]
        public async Task AClaimOfAMonthIsOfferedItsMovesAtItsOwnAddress()
        {
            RuleContext ledger = await Drafted(Context(new MemoryStore()), October, 32000);

            ValidInputSet offered = await ledger.GetValidInputsAsync(OctoberAddress.Child("claims", "c-1"), 100);

            Assert.Equal(
                [
                    "months.submit(by: ann)", "months.submit(by: bo)", "months.submit(by: cy)",
                    "months.claims.enterAmount(amount: <type.int>)"
                ],
                offered.Select(static input => input.ToString()));
        }

        [Fact]
        public async Task AMoveFoundAtAClaimOfAMonthIsWrittenWithBothKeys()
        {
            MemoryStore store = new();
            RuleContext ledger = await Drafted(Context(store), October, 32000);

            ValidInput submit = (await ledger.GetValidInputsAsync(OctoberAddress.Child("claims", "c-1"), 100))[0];
            await ledger.ApplyAsync(submit.ToInputDocument(ledger.RuleSet));

            Assert.Equal(32000, Data(store[OctoberAddress]).GetProperty("used").GetInt32());
        }

        [Fact]
        public async Task TheLedgerMayRefuseAMonthsInputByItsKey()
        {
            RuleContext ledger = Context(new MemoryStore(), Ledger.Replace(
                "\n  ]\n}",
                """
                  ],
                  "held": {
                    "months": {
                      "create": { "when": { "op": "cmp.eq", "right": "2026-10",
                                            "left": { "op": "rec.at", "record": "@months", "key": "key" } } }
                    }
                  }
                }
                """,
                StringComparison.Ordinal));

            Assert.Single(await ledger.GetValidInputsAsync(OctoberAddress, 100));
            Assert.Empty(await ledger.GetValidInputsAsync(StateAddress.Of("months", "2026-11"), 100));
            await Assert.ThrowsAsync<IllegalInputException>(() => ledger.ApplyAsync(Input("months.create", month: "2026-11")));
        }

        [Fact]
        public async Task AMonthKeptInAnotherStoreKeepsItsClaimsThere()
        {
            MemoryStore store = new();
            MemoryStore other = new();
            RuleContext ledger = Context(store, month: MonthElsewhere, other: other);

            await ledger.ApplyAsync(Input("months.create"));

            Assert.True(other.Holds(OctoberAddress.Child("claims", "c-1")));
            Assert.False(store.Holds(OctoberAddress.Child("claims", "c-1")));
        }

        [Fact]
        public async Task ATransitionAcrossTwoStoresIsNotCommittedWithoutACoordinator()
        {
            MemoryStore store = new();
            MemoryStore other = new();
            RuleContext ledger = await Drafted(Context(store, month: MonthElsewhere, other: other), October, 32000);

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => ledger.ApplyAsync(Input("months.submit", "c-1", """{ "by": "ann" }""")));

            Assert.Contains("coordinator", refused.Message, StringComparison.Ordinal);
            Assert.False(store.Holds(OctoberAddress));
            Assert.Equal("draft", Data(other[OctoberAddress.Child("claims", "c-1")]).GetProperty("stage").GetString());
        }

        [Fact]
        public async Task ACoordinatorIsHandedWhatEachStoreIsWritten()
        {
            MemoryStore store = new();
            MemoryStore other = new();
            Coordinator coordinator = new();
            RuleContext ledger = await Drafted(
                Context(store, month: MonthElsewhere, other: other, coordinator: coordinator),
                October,
                32000);

            await ledger.ApplyAsync(Input("months.submit", "c-1", """{ "by": "ann" }"""));

            IReadOnlyList<StoreWriteSet> handed = Assert.Single(coordinator.Handed);
            Assert.Equal(
                [(MemoryStore.Plugin, OctoberAddress), (MemoryStore.OtherPlugin, OctoberAddress.Child("claims", "c-1"))],
                handed.Select(static set => (set.Store, Assert.Single(set.Writes).Address)));
            Assert.Equal(32000, Data(store[OctoberAddress]).GetProperty("used").GetInt32());
            Assert.Equal("review", Data(other[OctoberAddress.Child("claims", "c-1")]).GetProperty("stage").GetString());
        }

        [Fact]
        public void AKeyedCollectionCreatesNothing()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(new MemoryStore(), Ledger.Replace(
                "\n  ]\n}",
                """
                  ],
                  "inputs": { "open": { "creates": "months" } }
                }
                """,
                StringComparison.Ordinal)));

            Assert.Contains("/inputs/open/creates", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void EveryMonthIsNotASequenceAProjectionCanRead()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(new MemoryStore(), Ledger.Replace(
                "\n  ]\n}",
                """
                  ],
                  "projections": { "months": { "op": "seq.count", "source": "$months" } }
                }
                """,
                StringComparison.Ordinal)));

            Assert.Contains("every key its schema admits", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ARuleSetThatKeepsInstancesIsHeldOnlyMany()
        {
            int many = Ledger.IndexOf(",\n      \"many\"", StringComparison.Ordinal);
            int end = Ledger.IndexOf(" }\n  ]", many, StringComparison.Ordinal);

            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(
                () => Context(new MemoryStore(), Ledger[..many] + Ledger[end..]));

            Assert.Contains("held only 'many'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ACollectionMayNotShareAnAliasWithOneItsInstancesKeep()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(new MemoryStore(), Ledger.Replace(
                "\"as\": \"months\"",
                "\"as\": \"claims\"",
                StringComparison.Ordinal)));

            Assert.Contains("/uses[0]/as", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void DrivingAMonthsInputAboutAClaimIsNotSupportedYet()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(new MemoryStore(), Ledger.Replace(
                "\n  ]\n}",
                """
                  ],
                  "inputs": {
                    "submit": {
                      "at": "months",
                      "fires": [ { "held": "months", "input": "submit", "args": { "by": "ann" } } ]
                    }
                  }
                }
                """,
                StringComparison.Ordinal)));

            Assert.Contains("/inputs/submit/fires[0]/input", refused.Message, StringComparison.Ordinal);
        }

        private static RuleContext Context(
            MemoryStore store,
            string ledger = Ledger,
            string? month = null,
            MemoryStore? other = null,
            IStoreCoordinator? coordinator = null)
        {
            RuleRuntime runtime = MemoryStore.Runtime(store, other);
            if (coordinator is not null)
            {
                runtime.UseCoordinator(coordinator);
            }

            return runtime.CreateContext(
                ledger,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["claim-month"] = month ?? InstanceTests.Month,
                    ["claim"] = InstanceTests.Claim
                },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["claim-start"] = InstanceTests.ClaimStart });
        }

        /// <summary>A ledger with one claim, c-1, drafted at an amount in a month.</summary>
        private static async Task<RuleContext> Drafted(RuleContext ledger, string month, int amount)
        {
            await ledger.ApplyAsync(Input("months.create", month: month));
            await ledger.ApplyAsync(Input("months.claims.enterAmount", "c-1", $$"""{ "amount": {{amount}} }""", month));
            return ledger;
        }

        private static string Input(string input, string? claim = null, string args = "{}", string month = October) =>
            claim is null
                ? $$"""{ "input": "{{input}}", "at": { "months": "{{month}}" }, "args": {{args}} }"""
                : $$"""{ "input": "{{input}}", "at": { "months": "{{month}}", "claims": "{{claim}}" }, "args": {{args}} }""";

        private static JsonElement Data(string state) =>
            JsonDocument.Parse(state).RootElement.GetProperty("data").Clone();

        /// <summary>Commits each store in turn, and remembers what it was handed.</summary>
        /// <remarks>Not all or none: a coordinator that is, is the host's to write with its stores.</remarks>
        private sealed class Coordinator : IStoreCoordinator
        {
            public List<IReadOnlyList<StoreWriteSet>> Handed { get; } = [];

            public async Task<IReadOnlyDictionary<string, string>> CommitAsync(
                IReadOnlyList<StoreWriteSet> writes,
                CancellationToken cancellationToken = default)
            {
                Handed.Add(writes);
                Dictionary<string, string> created = new(StringComparer.Ordinal);
                foreach (StoreWriteSet set in writes)
                {
                    foreach ((string temporary, string key) in await set.Session.CommitAsync(set.Writes, cancellationToken))
                    {
                        created[temporary] = key;
                    }
                }

                return created;
            }
        }
    }
}
