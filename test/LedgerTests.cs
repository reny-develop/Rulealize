// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction;

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
    /// <see cref="MemoryStore"/>: nesting adds addresses and nothing a store has to know.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class LedgerTests(StandardRuntime standard)
    {
        private const string Requires = StandardRuntime.Requires;

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

        [Fact]
        public void CreatingAClaimWritesItUnderItsMonthAndNothingElse()
        {
            RuleContext ledger = Context();
            MemoryStore store = MemoryStore.Opened(ledger);

            TransitionResult result = ledger.ApplyToState(Input("months.create"), store.Read(ledger, Input("months.create")));

            StateWrite write = Assert.Single(result.Writes);
            Assert.True(write.IsInsert);
            Assert.Equal(OctoberAddress.Child("claims", "~1"), write.Address);
        }

        [Fact]
        public void AMonthNobodyHasWrittenIsReadWithoutADocument()
        {
            RuleContext ledger = Context();

            Assert.Equal([StateAddress.Root, OctoberAddress], ledger.GetReads(Input("months.create")).AsEnumerable());
            Assert.False(MemoryStore.Opened(ledger).Read(ledger, Input("months.create")).ContainsKey(OctoberAddress));
        }

        [Fact]
        public void TheStoreAssignsTheClaimsKeyUnderItsMonth()
        {
            RuleContext ledger = Context();
            MemoryStore store = MemoryStore.Opened(ledger);

            store.Apply(ledger, Input("months.create"));

            Assert.True(store.Holds(OctoberAddress.Child("claims", "c-1")));
            Assert.False(store.Holds(OctoberAddress));
        }

        [Fact]
        public void ReplayingACreationInAMonthWritesTheSameKey()
        {
            RuleContext ledger = Context();
            MemoryStore store = MemoryStore.Opened(ledger);
            string commit = store.Apply(ledger, Input("months.create"));

            TransitionResult replayed = ledger.ApplyToState(
                Input("months.create"), store.Read(ledger, Input("months.create")), commit);

            Assert.Equal(OctoberAddress.Child("claims", "c-1"), Assert.Single(replayed.Writes).Address);
        }

        [Fact]
        public void AnInputOfAClaimReadsTheMonthAndTheClaim()
        {
            RuleContext ledger = Context();

            Assert.Equal(
                [StateAddress.Root, OctoberAddress, OctoberAddress.Child("claims", "c-1")],
                ledger.GetReads(Input("months.claims.enterAmount", "c-1", """{ "amount": 5 }""")).AsEnumerable());
        }

        [Fact]
        public void EnteringAnAmountWritesOnlyTheClaim()
        {
            RuleContext ledger = Context();
            MemoryStore store = MemoryStore.Opened(ledger);
            store.Apply(ledger, Input("months.create"));

            string input = Input("months.claims.enterAmount", "c-1", """{ "amount": 32000 }""");
            TransitionResult result = ledger.ApplyToState(input, store.Read(ledger, input));

            StateWrite write = Assert.Single(result.Writes);
            Assert.Equal(OctoberAddress.Child("claims", "c-1"), write.Address);
            Assert.Equal(32000, Data(write.State).GetProperty("amount").GetInt32());
        }

        [Fact]
        public void TheFirstSubmissionWritesTheMonthForTheFirstTime()
        {
            RuleContext ledger = Context();
            MemoryStore store = Drafted(ledger, October, 32000);

            store.Apply(ledger, Input("months.submit", "c-1", """{ "by": "ann" }"""));

            Assert.Equal(32000, Data(store[OctoberAddress]).GetProperty("used").GetInt32());
            Assert.Equal("review", Data(store[OctoberAddress.Child("claims", "c-1")]).GetProperty("stage").GetString());
        }

        [Fact]
        public void EachMonthHasALimitOfItsOwn()
        {
            RuleContext ledger = Context();
            MemoryStore store = Drafted(ledger, October, 50000);
            store.Apply(ledger, Input("months.submit", "c-1", """{ "by": "ann" }"""));
            store.Apply(ledger, Input("months.create", month: "2026-11"));
            store.Apply(ledger, Input("months.claims.enterAmount", "c-2", """{ "amount": 50000 }""", "2026-11"));

            store.Apply(ledger, Input("months.submit", "c-2", """{ "by": "ann" }""", "2026-11"));

            Assert.Equal(50000, Data(store[StateAddress.Of("months", "2026-11")]).GetProperty("used").GetInt32());
        }

        [Fact]
        public void TwoFirstSubmissionsInOneMonthCollideOnTheMonth()
        {
            RuleContext ledger = Context();
            MemoryStore store = Drafted(ledger, October, 10000);
            store.Apply(ledger, Input("months.create"));
            store.Apply(ledger, Input("months.claims.enterAmount", "c-2", """{ "amount": 10000 }"""));

            MemoryStore.Pending first = store.Begin(ledger, Input("months.submit", "c-1", """{ "by": "ann" }"""));
            MemoryStore.Pending second = store.Begin(ledger, Input("months.submit", "c-2", """{ "by": "ann" }"""));
            store.Commit(first);

            InvalidOperationException stale = Assert.Throws<InvalidOperationException>(() => store.Commit(second));
            Assert.Contains($"'months/{October}'", stale.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AClaimIsReachedOnlyInTheMonthItWasCreatedIn()
        {
            RuleContext ledger = Context();
            MemoryStore store = Drafted(ledger, October, 32000);

            string input = Input("months.claims.enterAmount", "c-1", """{ "amount": 5 }""", "2026-11");

            RuleDocumentException missing = Assert.Throws<RuleDocumentException>(
                () => ledger.ApplyToState(input, store.Read(ledger, input)));
            Assert.Contains("months/2026-11/claims/c-1", missing.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AKeyTheSchemaRefusesIsNoMonth()
        {
            RuleContext ledger = Context();

            RuleDocumentException refused = Assert.Throws<RuleDocumentException>(
                () => ledger.GetReads(Input("months.create", month: "2026-1")));
            Assert.Contains("not a key of 'months'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AnInputOfAClaimNeedsBothKeys()
        {
            RuleContext ledger = Context();
            const string input = """{ "input": "months.claims.enterAmount", "at": { "claims": "c-1" }, "args": { "amount": 5 } }""";

            RuleDocumentException refused = Assert.Throws<RuleDocumentException>(() => ledger.GetReads(input));
            Assert.Contains("one instance of 'months'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AMonthIsOfferedCreationAtItsOwnAddress()
        {
            RuleContext ledger = Context();
            MemoryStore store = MemoryStore.Opened(ledger);

            ValidInputSet offered = ledger.GetValidInputs(store.Snapshot(), OctoberAddress, 100);

            Assert.Equal(["months.create"], offered.Select(static input => input.ToString()));
        }

        [Fact]
        public void TheLedgerOffersNothingAtItsOwnAddress()
        {
            RuleContext ledger = Context();

            Assert.Empty(ledger.GetValidInputs(MemoryStore.Opened(ledger).Snapshot(), StateAddress.Root, 100));
        }

        [Fact]
        public void AClaimOfAMonthIsOfferedItsMovesAtItsOwnAddress()
        {
            RuleContext ledger = Context();
            MemoryStore store = Drafted(ledger, October, 32000);

            ValidInputSet offered = ledger.GetValidInputs(store.Snapshot(), OctoberAddress.Child("claims", "c-1"), 100);

            Assert.Equal(
                [
                    "months.submit(by: ann)", "months.submit(by: bo)", "months.submit(by: cy)",
                    "months.claims.enterAmount(amount: <type.int>)"
                ],
                offered.Select(static input => input.ToString()));
        }

        [Fact]
        public void AMoveFoundAtAClaimOfAMonthIsWrittenWithBothKeys()
        {
            RuleContext ledger = Context();
            MemoryStore store = Drafted(ledger, October, 32000);

            ValidInput submit = ledger.GetValidInputs(store.Snapshot(), OctoberAddress.Child("claims", "c-1"), 100)[0];
            store.Apply(ledger, submit.ToInputDocument(ledger.RuleSet));

            Assert.Equal(32000, Data(store[OctoberAddress]).GetProperty("used").GetInt32());
        }

        [Fact]
        public void TheLedgerMayRefuseAMonthsInputByItsKey()
        {
            RuleContext ledger = Context(Ledger.Replace(
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
            MemoryStore store = MemoryStore.Opened(ledger);

            Assert.Single(ledger.GetValidInputs(store.Snapshot(), OctoberAddress, 100));
            Assert.Empty(ledger.GetValidInputs(store.Snapshot(), StateAddress.Of("months", "2026-11"), 100));
            Assert.Throws<IllegalInputException>(() => store.Apply(ledger, Input("months.create", month: "2026-11")));
        }

        [Fact]
        public void AKeyedCollectionCreatesNothing()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(Ledger.Replace(
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
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(Ledger.Replace(
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
            int end = Ledger.IndexOf(" }\n  ]", StringComparison.Ordinal);

            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(
                () => Context(Ledger[..many] + Ledger[end..]));

            Assert.Contains("held only 'many'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ACollectionMayNotShareAnAliasWithOneItsInstancesKeep()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(Ledger.Replace(
                "\"as\": \"months\"",
                "\"as\": \"claims\"",
                StringComparison.Ordinal)));

            Assert.Contains("/uses[0]/as", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void DrivingAMonthsInputAboutAClaimIsNotSupportedYet()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(Ledger.Replace(
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

        private RuleContext Context(string ledger = Ledger) =>
            standard.Runtime.CreateContext(
                ledger,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["claim-month"] = InstanceTests.Month,
                    ["claim"] = InstanceTests.Claim
                },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["claim-start"] = InstanceTests.ClaimStart });

        /// <summary>A ledger with one claim, c-1, drafted at an amount in a month.</summary>
        private static MemoryStore Drafted(RuleContext ledger, string month, int amount)
        {
            MemoryStore store = MemoryStore.Opened(ledger);
            store.Apply(ledger, Input("months.create", month: month));
            store.Apply(ledger, Input("months.claims.enterAmount", "c-1", $$"""{ "amount": {{amount}} }""", month));
            return store;
        }

        private static string Input(string input, string? claim = null, string args = "{}", string month = October) =>
            claim is null
                ? $$"""{ "input": "{{input}}", "at": { "months": "{{month}}" }, "args": {{args}} }"""
                : $$"""{ "input": "{{input}}", "at": { "months": "{{month}}", "claims": "{{claim}}" }, "args": {{args}} }""";

        private static JsonElement Data(string state) =>
            JsonDocument.Parse(state).RootElement.GetProperty("data").Clone();
    }
}
