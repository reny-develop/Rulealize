// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rulealize.Abstraction;

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
    /// The store is <see cref="MemoryStore"/>, a dictionary with a version per document. The
    /// runtime stores nothing and knows no versions: it says what a transition reads before it
    /// runs and what it writes afterwards, and the store checks the one against the other.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class InstanceTests(StandardRuntime standard)
    {
        private const string Requires = StandardRuntime.Requires;

        /// <summary>One claim: an amount entered while it is a draft, then submitted and decided.</summary>
        private const string Claim = $$"""
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
        private const string ClaimStart = """
            { "$schema": "rulealize/state/v1", "ruleSet": "claim@1.0.0",
              "data": { "stage": "draft", "amount": 0, "approver": "bo" } }
            """;

        /// <summary>A month: its claims, and how much of its limit they have used.</summary>
        private const string Month = $$"""
            {
              "id": "claim-month", "version": "0.1.0",
            {{Requires}}
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
            RuleContext month = Context();
            MemoryStore store = MemoryStore.Opened(month);

            TransitionResult result = month.ApplyToState(Input("create"), store.Read(month, Input("create")));

            StateWrite write = Assert.Single(result.Writes);
            Assert.True(write.IsInsert);
            Assert.Equal(StateAddress.Of("claims", "~1"), write.Address);
            Assert.Equal("bo", Data(write.State).GetProperty("approver").GetString());
        }

        [Fact]
        public void TheStoreAssignsTheKeyAndRecordsIt()
        {
            RuleContext month = Context();
            MemoryStore store = MemoryStore.Opened(month);

            string commit = store.Apply(month, Input("create"));

            using JsonDocument recorded = JsonDocument.Parse(commit);
            Assert.Equal("c-1", recorded.RootElement.GetProperty("created").GetProperty("~1").GetString());
            Assert.True(store.Holds(StateAddress.Of("claims", "c-1")));
        }

        [Fact]
        public void ReplayingACreationWithItsCommitWritesTheSameKey()
        {
            RuleContext month = Context();
            MemoryStore store = MemoryStore.Opened(month);
            string commit = store.Apply(month, Input("create"));

            TransitionResult replayed = month.ApplyToState(Input("create"), store.Read(month, Input("create")), commit);

            Assert.Equal(StateAddress.Of("claims", "c-1"), Assert.Single(replayed.Writes).Address);
        }

        [Fact]
        public void AnInputAtAClaimReadsTheMonthAndThatClaim()
        {
            RuleContext month = Context();

            ImmutableArray<StateAddress> reads = month.GetReads(Input("claims.enterAmount", "c-3", """{ "amount": 1 }"""));

            Assert.Equal([MonthAddress, StateAddress.Of("claims", "c-3")], reads.AsEnumerable());
        }

        [Fact]
        public void EnteringAnAmountWritesOnlyTheClaim()
        {
            RuleContext month = Context();
            MemoryStore store = MemoryStore.Opened(month);
            store.Apply(month, Input("create"));

            string input = Input("claims.enterAmount", "c-1", """{ "amount": 32000 }""");
            TransitionResult result = month.ApplyToState(input, store.Read(month, input));

            StateWrite write = Assert.Single(result.Writes);
            Assert.Equal(StateAddress.Of("claims", "c-1"), write.Address);
            Assert.Equal(32000, Data(write.State).GetProperty("amount").GetInt32());
        }

        [Fact]
        public void SubmittingWritesTheMonthAndTheClaim()
        {
            RuleContext month = Context();
            MemoryStore store = Drafted(month, 32000);

            string input = Input("submit", "c-1", """{ "by": "ann" }""");
            TransitionResult result = month.ApplyToState(input, store.Read(month, input));

            Assert.Equal(
                [MonthAddress, StateAddress.Of("claims", "c-1")],
                result.Writes.Select(static write => write.Address));
            Assert.Equal(32000, Data(result.Writes[0].State).GetProperty("used").GetInt32());
            Assert.Equal("review", Data(result.Writes[1].State).GetProperty("stage").GetString());
        }

        [Fact]
        public void ASubmissionThatDoesNotFitInTheMonthIsRefused()
        {
            RuleContext month = Context();
            MemoryStore store = Drafted(month, 60000);

            string input = Input("submit", "c-1", """{ "by": "ann" }""");

            Assert.Throws<IllegalInputException>(() => month.ApplyToState(input, store.Read(month, input)));
        }

        [Fact]
        public void RejectingAClaimGivesItsAmountBack()
        {
            RuleContext month = Context();
            MemoryStore store = Drafted(month, 32000);
            store.Apply(month, Input("submit", "c-1", """{ "by": "ann" }"""));

            store.Apply(month, Input("reject", "c-1", """{ "by": "bo" }"""));

            Assert.Equal(0, Data(store[MonthAddress]).GetProperty("used").GetInt32());
            Assert.Equal("denied", Data(store[StateAddress.Of("claims", "c-1")]).GetProperty("stage").GetString());
            Assert.Equal("50000", month.Project("left", store[MonthAddress]));
        }

        [Fact]
        public void AProjectionReadsEveryClaimInKeyOrder()
        {
            RuleContext month = Context();
            MemoryStore store = Drafted(month, 32000);
            store.Apply(month, Input("create"));

            // Handed over last-first, which is not the order a projection sees them in.
            Dictionary<StateAddress, string> position = store.Snapshot().Reverse().ToDictionary();

            using JsonDocument answer = JsonDocument.Parse(month.Project("claims", position));
            Assert.Equal(
                [("c-1", "draft", 32000), ("c-2", "draft", 0)],
                answer.RootElement.EnumerateArray().Select(static row => (
                    row.GetProperty("key").GetString(),
                    row.GetProperty("stage").GetString(),
                    row.GetProperty("amount").GetInt32())));
        }

        [Fact]
        public void AMonthWithNoClaimsProjectsNone()
        {
            RuleContext month = Context();

            Assert.Equal("[]", month.Project("claims", MemoryStore.Opened(month).Snapshot()));
        }

        [Fact]
        public void TheMonthCanSayWhetherItsUseIsWhatItsClaimsSay()
        {
            RuleContext month = Context();
            MemoryStore store = Drafted(month, 32000);
            store.Apply(month, Input("submit", "c-1", """{ "by": "ann" }"""));

            Assert.Equal("true", month.Project("consistent", store.Snapshot()));

            store.Apply(month, Input("reject", "c-1", """{ "by": "bo" }"""));

            Assert.Equal("true", month.Project("consistent", store.Snapshot()));
        }

        [Fact]
        public void AProjectionSaysWhichCollectionsItReads()
        {
            RuleContext month = Context();

            Assert.Equal(["claims"], month.GetCollectionsRead("claims").AsEnumerable());
            Assert.Empty(month.GetCollectionsRead("left"));
        }

        [Fact]
        public void AProjectionOverEveryClaimRefusesOneDocument()
        {
            RuleContext month = Context();

            Assert.Throws<InvalidOperationException>(() => month.Project("claims", month.InitialState));
        }

        [Fact]
        public void AProjectionIsNotAskedAboutAnUncommittedClaim()
        {
            RuleContext month = Context();
            Dictionary<StateAddress, string> position = new(MemoryStore.Opened(month).Snapshot())
            {
                [StateAddress.Of("claims", "~1")] = ClaimStart
            };

            Assert.Throws<RuleDocumentException>(() => month.Project("claims", position));
        }

        [Fact]
        public void AGuardMayNotReadEveryClaim()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(Month.Replace(
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
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(Month.Replace(
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
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(Month.Replace(
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
        public void TheClaimsOwnRejectIsDrivenOnlyThroughTheMonth()
        {
            RuleContext month = Context();
            MemoryStore store = Drafted(month, 32000);
            store.Apply(month, Input("submit", "c-1", """{ "by": "ann" }"""));

            string input = Input("claims.reject", "c-1", """{ "by": "bo" }""");

            Assert.Throws<IllegalInputException>(() => month.ApplyToState(input, store.Read(month, input)));
        }

        [Fact]
        public void TheMonthOffersCreationAtItsOwnAddress()
        {
            RuleContext month = Context();
            MemoryStore store = Drafted(month, 32000);

            ValidInputSet offered = month.GetValidInputs(store.Snapshot(), MonthAddress, 100);

            Assert.Equal(["create"], offered.Select(static input => input.ToString()));
        }

        [Fact]
        public void AClaimIsOfferedItsMovesAtItsOwnAddress()
        {
            RuleContext month = Context();
            MemoryStore store = Drafted(month, 32000);

            ValidInputSet offered = month.GetValidInputs(store.Snapshot(), StateAddress.Of("claims", "c-1"), 100);

            Assert.Equal(
                ["submit(by: ann)", "submit(by: bo)", "submit(by: cy)", "claims.enterAmount(amount: <type.int>)"],
                offered.Select(static input => input.ToString()));
        }

        [Fact]
        public void AMoveFoundAtAClaimIsWrittenWithItsAt()
        {
            RuleContext month = Context();
            MemoryStore store = Drafted(month, 32000);

            ValidInput submit = month.GetValidInputs(store.Snapshot(), StateAddress.Of("claims", "c-1"), 100)[0];
            store.Apply(month, submit.ToInputDocument(month.RuleSet));

            Assert.Equal(32000, Data(store[MonthAddress]).GetProperty("used").GetInt32());
        }

        [Fact]
        public void TwoSubmissionsInOneMonthCollideOnTheMonth()
        {
            RuleContext month = Context();
            MemoryStore store = Drafted(month, 10000);
            store.Apply(month, Input("create"));
            store.Apply(month, Input("claims.enterAmount", "c-2", """{ "amount": 10000 }"""));

            MemoryStore.Pending first = store.Begin(month, Input("submit", "c-1", """{ "by": "ann" }"""));
            MemoryStore.Pending second = store.Begin(month, Input("submit", "c-2", """{ "by": "ann" }"""));
            store.Commit(first);

            InvalidOperationException stale = Assert.Throws<InvalidOperationException>(() => store.Commit(second));
            Assert.Contains("'/'", stale.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AnAmountEnteredMeanwhileDoesNotCollideWithASubmission()
        {
            RuleContext month = Context();
            MemoryStore store = Drafted(month, 10000);
            store.Apply(month, Input("create"));

            MemoryStore.Pending submit = store.Begin(month, Input("submit", "c-1", """{ "by": "ann" }"""));
            MemoryStore.Pending enter = store.Begin(month, Input("claims.enterAmount", "c-2", """{ "amount": 5 }"""));
            store.Commit(submit);
            store.Commit(enter);

            Assert.Equal(5, Data(store[StateAddress.Of("claims", "c-2")]).GetProperty("amount").GetInt32());
        }

        [Fact]
        public void AnInputAtAClaimNeedsTheInputDocumentToSayWhichClaim()
        {
            RuleContext month = Context();

            RuleDocumentException refused = Assert.Throws<RuleDocumentException>(
                () => month.GetReads(Input("submit", null, """{ "by": "ann" }""")));
            Assert.Contains("does not say which", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ATemporaryKeyCannotBeWhereAnInputIs()
        {
            RuleContext month = Context();

            RuleDocumentException refused = Assert.Throws<RuleDocumentException>(
                () => month.GetReads(Input("submit", "~1", """{ "by": "ann" }""")));
            Assert.Contains("temporary key", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ARuleSetThatKeepsInstancesRefusesOneDocument()
        {
            RuleContext month = Context();

            Assert.Throws<InvalidOperationException>(() => month.GetValidInputs(month.InitialState, 10));
            Assert.Throws<InvalidOperationException>(() => month.ApplyToState(Input("create"), month.InitialState));
        }

        [Fact]
        public void AnInputAtAClaimThatDrivesNoneOfItsInputsIsRefused()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(Month.Replace(
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

            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(unaddressed));

            Assert.Contains("Write \"at\": \"claims\"", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AGuardOverAClaimsOwnInputMayNotReadTheMonth()
        {
            // claims.enterAmount writes the claim and never the month, so a store checking the
            // claim's version would not see the month move between reading and committing.
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(Month.Replace(
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
        public void AGuardOverAClaimsOwnInputMayReadTheClaim()
        {
            RuleContext month = Context(Month.Replace(
                """
                "submit": { "when": false },
                """,
                """
                "submit": { "when": false }, "enterAmount": { "when": { "op": "cmp.eq", "left": { "op": "def.call", "def": "amountOf", "args": { "claim": "@claims" } }, "right": 0 } },
                """,
                StringComparison.Ordinal));
            MemoryStore store = Drafted(month, 0);

            store.Apply(month, Input("claims.enterAmount", "c-1", """{ "amount": 5 }"""));
            string again = Input("claims.enterAmount", "c-1", """{ "amount": 6 }""");

            Assert.Throws<IllegalInputException>(() => month.ApplyToState(again, store.Read(month, again)));
        }

        [Fact]
        public void CreatingMayNotBeDecidedOnTheMonthItDoesNotWrite()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(Month.Replace(
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
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(Month
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

            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(month));

            Assert.Contains("/inputs/reject/fires", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AStartWrittenForAnotherMajorVersionIsRefused()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(
                Month,
                ClaimStart.Replace("claim@1.0.0", "claim@2.0.0", StringComparison.Ordinal)));

            Assert.Contains("claim@2.0.0", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AStartThatIsNotAStateOfTheClaimIsRefused()
        {
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(() => Context(
                Month,
                ClaimStart.Replace("\"bo\"", "\"di\"", StringComparison.Ordinal)));

            Assert.Contains("approver", refused.Message, StringComparison.Ordinal);
        }

        private RuleContext Context(string month = Month, string start = ClaimStart) =>
            standard.Runtime.CreateContext(
                month,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["claim"] = Claim },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["claim-start"] = start });

        /// <summary>A month with one claim, c-1, drafted at an amount.</summary>
        private static MemoryStore Drafted(RuleContext month, int amount)
        {
            MemoryStore store = MemoryStore.Opened(month);
            store.Apply(month, Input("create"));
            store.Apply(month, Input("claims.enterAmount", "c-1", $$"""{ "amount": {{amount}} }"""));
            return store;
        }

        private static string Input(string input, string? claim = null, string args = "{}") =>
            claim is null
                ? $$"""{ "input": "{{input}}", "args": {{args}} }"""
                : $$"""{ "input": "{{input}}", "at": { "claims": "{{claim}}" }, "args": {{args}} }""";

        private static JsonElement Data(string state) =>
            JsonDocument.Parse(state).RootElement.GetProperty("data").Clone();

        /// <summary>A store that keeps documents in a dictionary, each with a version.</summary>
        /// <remarks>
        /// Everything the runtime leaves to a store and nothing more: it fetches what
        /// <see cref="RuleContext.GetReads"/> lists, remembers the version each was read at,
        /// and on commit refuses a write to a document that has moved since. Keys are
        /// <c>c-1</c>, <c>c-2</c>, … in the order instances are created.
        /// </remarks>
        private sealed class MemoryStore
        {
            private readonly Dictionary<StateAddress, (string State, int Version)> _documents = [];
            private int _created;

            public string this[StateAddress address] => _documents[address].State;

            public static MemoryStore Opened(RuleContext month)
            {
                MemoryStore store = new();
                store._documents[StateAddress.Root] = (month.InitialState, 1);
                return store;
            }

            public bool Holds(StateAddress address) => _documents.ContainsKey(address);

            public IReadOnlyDictionary<StateAddress, string> Snapshot() =>
                _documents.ToDictionary(static entry => entry.Key, static entry => entry.Value.State);

            public IReadOnlyDictionary<StateAddress, string> Read(RuleContext rules, string input) =>
                rules.GetReads(input).ToDictionary(static address => address, address => _documents[address].State);

            public Pending Begin(RuleContext rules, string input)
            {
                Dictionary<StateAddress, int> versions = rules.GetReads(input)
                    .ToDictionary(static address => address, address => _documents[address].Version);

                return new Pending(rules, input, rules.ApplyToState(input, Read(rules, input)), versions);
            }

            public string Commit(Pending pending)
            {
                foreach (StateWrite write in pending.Result.Writes.Where(static write => !write.IsInsert))
                {
                    // A guard reads only what its transition writes, so a write never lands on a
                    // document the transition did not read, and checking writes is enough.
                    int read = pending.Versions[write.Address];
                    if (_documents[write.Address].Version != read)
                    {
                        throw new InvalidOperationException($"'{write.Address}' moved since it was read at version {read}.");
                    }
                }

                Dictionary<string, string> created = new(StringComparer.Ordinal);
                foreach (StateWrite write in pending.Result.Writes)
                {
                    if (write.IsInsert)
                    {
                        string key = $"c-{++_created}";
                        created[write.Address.Segments[^1]] = key;
                        _documents[StateAddress.Of(write.Address.Segments[0], key)] = (write.State, 1);
                    }
                    else
                    {
                        _documents[write.Address] = (write.State, _documents[write.Address].Version + 1);
                    }
                }

                JsonObject commit = new()
                {
                    ["$schema"] = "rulealize/commit/v1",
                    ["ruleSet"] = pending.Rules.RuleSet,
                    ["input"] = JsonNode.Parse(pending.Input)!["input"]!.GetValue<string>(),
                    ["created"] = new JsonObject(created.Select(static pair =>
                        KeyValuePair.Create<string, JsonNode?>(pair.Key, pair.Value)))
                };

                return commit.ToJsonString();
            }

            public string Apply(RuleContext rules, string input) => Commit(Begin(rules, input));

            public sealed record Pending(
                RuleContext Rules,
                string Input,
                TransitionResult Result,
                IReadOnlyDictionary<StateAddress, int> Versions);
        }
    }
}
