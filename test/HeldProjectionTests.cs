// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Rulealize.Abstraction.Store;

namespace Rulealize.Tests
{
    /// <summary>What a held rule set says about itself, asked through the rule set holding it.</summary>
    /// <remarks>
    /// <para>
    /// A claim says what one claim looks like and whether it is decided, in its own
    /// <c>projections</c> and <c>terminal</c>. Held <c>many</c> by a month, it is asked one
    /// instance at a time, at that instance's address, and the month's own document is not
    /// read for it. Held once, it is asked about its field of the holder's state.
    /// </para>
    /// <para>
    /// The month is <see cref="InstanceTests.Month"/> unchanged; only the claim has gained a
    /// projection and a <c>terminal</c>.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class HeldProjectionTests(StandardRuntime standard)
    {
        private const string Requires = StandardRuntime.Requires;

        /// <summary><see cref="InstanceTests.Claim"/>, saying what it looks like and when it is decided.</summary>
        private static readonly string Claim = Extended(
            InstanceTests.Claim,
            """
            { "summary": { "stage": "$stage", "amount": "$amount",
                           "editable": { "op": "cmp.eq", "left": "$stage", "right": "draft" } } }
            """,
            """
            { "when": { "op": "logic.or", "any": [
                { "op": "cmp.eq", "left": "$stage", "right": "granted" },
                { "op": "cmp.eq", "left": "$stage", "right": "denied" } ] },
              "result": "$stage" }
            """);

        /// <summary>A counter whose projection doubles it.</summary>
        private const string Counter = $$"""
            {
              "id": "counter", "version": "1.0.0",
            {{Requires}}
              "state": {
                "schema": { "n": { "op": "type.int", "min": 0, "max": 10 } },
                "initial": { "n": 2 }
              },
              "inputs": {
                "bump": { "effects": [ { "op": "state.set", "path": "n", "value": { "op": "math.add", "of": ["$n", 1] } } ] }
              },
              "projections": { "doubled": { "op": "math.add", "of": ["$n", "$n"] } }
            }
            """;

        /// <summary>A rule set holding one counter, and saying nothing of its own.</summary>
        private const string Box = $$"""
            {
              "id": "box", "version": "1.0.0",
            {{Requires}}
              "uses": [ { "ruleSet": "counter", "version": "^1.0", "as": "c" } ]
            }
            """;

        private static readonly StateAddress First = StateAddress.Of("claims", "c-1");

        private static readonly StateAddress Second = StateAddress.Of("claims", "c-2");

        // ── Held many ──────────────────────────────────────────────────────────

        [Fact]
        public void AnInstancesProjectionsAreListedApartUnderItsAlias()
        {
            RuleContext month = Month(new MemoryStore());

            Assert.Equal<string>(["left", "claims", "consistent"], month.Projections);
            Assert.Equal<string>(["claims.summary"], month.InstanceProjections);
        }

        [Fact]
        public async Task EveryNameProjectionsListsIsAskedOfThePosition()
        {
            // What a walk does: ask each one of every position it reaches, in turn.
            RuleContext month = await Drafted(new MemoryStore(), 1200);

            foreach (string projection in month.Projections)
            {
                Assert.NotEmpty(await month.ProjectAsync(projection));
            }
        }

        [Fact]
        public void AKeptRuleSetsProjectionsAreAllAboutInstances()
        {
            RuleContext ledger = Ledger(new MemoryStore());

            Assert.Empty(ledger.Projections);
            Assert.Equal<string>(
                ["months.left", "months.claims", "months.consistent", "months.claims.summary"],
                ledger.InstanceProjections);
        }

        [Fact]
        public async Task AClaimIsAskedAboutAtItsAddress()
        {
            MemoryStore store = new();
            RuleContext month = Month(store);
            await month.ApplyAsync(Input("create"));
            await month.ApplyAsync(Input("create"));
            await month.ApplyAsync(Input("claims.enterAmount", "c-2", """{ "amount": 1200 }"""));

            using JsonDocument first = JsonDocument.Parse(await month.ProjectAsync(First, "claims.summary"));
            using JsonDocument second = JsonDocument.Parse(await month.ProjectAsync(Second, "claims.summary"));

            Assert.Equal(0, first.RootElement.GetProperty("amount").GetInt32());
            Assert.Equal(1200, second.RootElement.GetProperty("amount").GetInt32());
            Assert.True(second.RootElement.GetProperty("editable").GetBoolean());
        }

        [Fact]
        public async Task TheAnswerFollowsTheClaimAsItMoves()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 1200);

            await month.ApplyAsync(Input("submit", "c-1", """{ "by": "ann" }"""));

            using JsonDocument answer = JsonDocument.Parse(await month.ProjectAsync(First, "claims.summary"));
            Assert.Equal("review", answer.RootElement.GetProperty("stage").GetString());
            Assert.False(answer.RootElement.GetProperty("editable").GetBoolean());
        }

        [Fact]
        public async Task AClaimNobodyCreatedIsNotFound()
        {
            RuleContext month = Month(new MemoryStore());

            InstanceNotFoundException missing = await Assert.ThrowsAsync<InstanceNotFoundException>(
                () => month.ProjectAsync(First, "claims.summary"));

            Assert.Equal(First, missing.At);
        }

        [Fact]
        public async Task AClaimsProjectionIsNotAskedOfTheMonth()
        {
            RuleContext month = await Drafted(new MemoryStore(), 1200);

            ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(() => month.ProjectAsync("claims.summary"));

            Assert.Contains("one instance of 'claims'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheMonthsProjectionIsNotAskedOfAClaim()
        {
            RuleContext month = await Drafted(new MemoryStore(), 1200);

            ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(() => month.ProjectAsync(First, "left"));

            Assert.Contains("the rule set's own document", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AClaimsProjectionIsNotAskedOfOneStateDocument()
        {
            RuleContext month = Month(new MemoryStore());

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
                () => month.Project("claims.summary", month.InitialState));

            Assert.Contains("ProjectAsync(at, projection)", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AnUnknownNameListsTheQualifiedOnes()
        {
            RuleContext month = Month(new MemoryStore());

            ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(() => month.ProjectAsync(First, "claims.total"));

            Assert.Contains("'claims.summary'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task WhetherAClaimIsDecidedIsAskedOfTheClaim()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 1200);
            Assert.False((await month.GetTerminalStatusAsync(First)).IsTerminal);

            await month.ApplyAsync(Input("submit", "c-1", """{ "by": "ann" }"""));
            await month.ApplyAsync(Input("reject", "c-1", """{ "by": "bo" }"""));

            Assert.Equal(new TerminalStatus(true, "denied"), await month.GetTerminalStatusAsync(First));
            Assert.Equal(new TerminalStatus(false, null), await month.GetTerminalStatusAsync());
        }

        [Fact]
        public async Task WhetherAClaimNobodyCreatedIsDecidedIsNotFound()
        {
            RuleContext month = Month(new MemoryStore());

            await Assert.ThrowsAsync<InstanceNotFoundException>(() => month.GetTerminalStatusAsync(First));
        }

        [Fact]
        public async Task ACommittedTransitionSaysWhereTheInputArrived()
        {
            MemoryStore store = new();
            RuleContext month = await Drafted(store, 1200);
            await month.ApplyAsync(Input("submit", "c-1", """{ "by": "ann" }"""));

            CommittedTransition rejected = await month.ApplyAsync(Input("reject", "c-1", """{ "by": "bo" }"""));

            Assert.Equal(First, rejected.At);
            Assert.Equal(new TerminalStatus(true, "denied"), rejected.AtStatus);
            Assert.False(rejected.IsTerminal);
        }

        [Fact]
        public async Task ACreationArrivesAtTheKeyTheStoreAssigned()
        {
            MemoryStore store = new();
            RuleContext month = Month(store);
            await month.ApplyAsync(Input("create"));

            CommittedTransition created = await month.ApplyAsync(Input("create"));

            Assert.Equal(Second, created.At);
            Assert.Equal(new TerminalStatus(false, null), created.AtStatus);
        }

        [Fact]
        public async Task AReplayedCreationArrivesAtTheKeyItWasAssigned()
        {
            MemoryStore store = new();
            RuleContext month = Month(store);
            await month.ApplyAsync(Input("create"));
            CommittedTransition created = await month.ApplyAsync(Input("create"));

            MemoryStore rebuilt = new();
            RuleContext again = Month(rebuilt);
            CommittedTransition replayed = await again.ApplyAsync(Input("create"), created.Commit);

            Assert.Equal(Second, replayed.At);
        }

        [Fact]
        public async Task AMonthIsAskedAboutItsOwnClaimsAtItsAddressInTheLedger()
        {
            MemoryStore store = new();
            RuleContext ledger = Ledger(store);
            StateAddress october = StateAddress.Of("months", "2026-10");
            StateAddress november = StateAddress.Of("months", "2026-11");
            await ledger.ApplyAsync(LedgerInput("months.create", "2026-10"));
            await ledger.ApplyAsync(LedgerInput("months.create", "2026-10"));
            await ledger.ApplyAsync(LedgerInput("months.create", "2026-11"));
            await ledger.ApplyAsync(LedgerInput("months.claims.enterAmount", "2026-10", "c-2", """{ "amount": 700 }"""));

            using JsonDocument claims = JsonDocument.Parse(await ledger.ProjectAsync(october, "months.claims"));
            using JsonDocument claim = JsonDocument.Parse(
                await ledger.ProjectAsync(october.Child("claims", "c-2"), "months.claims.summary"));

            Assert.Equal(2, claims.RootElement.GetArrayLength());
            Assert.Equal(1, JsonDocument.Parse(await ledger.ProjectAsync(november, "months.claims")).RootElement.GetArrayLength());
            Assert.Equal(700, claim.RootElement.GetProperty("amount").GetInt32());
        }

        [Fact]
        public async Task AMonthNobodyWroteIsAtItsStartAndNotMissing()
        {
            // A month's key is a schema, so every month is there already; only a claim, whose
            // key a store generates, can be missing.
            RuleContext ledger = Ledger(new MemoryStore());

            Assert.Equal("50000", await ledger.ProjectAsync(StateAddress.Of("months", "2026-12"), "months.left"));
        }

        // ── Held once ──────────────────────────────────────────────────────────

        [Fact]
        public void WhatIsHeldOnceIsListedUnderItsAlias()
        {
            Assert.Equal<string>(["c.doubled"], Boxed().Projections);
        }

        [Fact]
        public void WhatIsHeldOnceIsAskedAboutItsField()
        {
            RuleContext box = Boxed();
            TransitionResult bumped = box.ApplyToState("""{ "input": "c.bump", "args": {} }""", box.InitialState);

            Assert.Equal("4", box.Project("c.doubled", box.InitialState));
            Assert.Equal("6", box.Project("c.doubled", bumped.State));
        }

        [Fact]
        public void AHeldProjectionIsNotTheHoldersOwn()
        {
            ArgumentException refused = Assert.Throws<ArgumentException>(() => Boxed().Project("doubled", Boxed().InitialState));

            Assert.Contains("'c.doubled'", refused.Message, StringComparison.Ordinal);
        }

        private static string Extended(string document, string projections, string terminal)
        {
            JsonObject root = JsonNode.Parse(document)!.AsObject();
            root["projections"] = JsonNode.Parse(projections);
            root["terminal"] = JsonNode.Parse(terminal);
            return root.ToJsonString();
        }

        private RuleContext Boxed() =>
            standard.Runtime.CreateContext(Box, new Dictionary<string, string>(StringComparer.Ordinal) { ["counter"] = Counter });

        private static RuleContext Month(MemoryStore store) =>
            MemoryStore.Runtime(store).CreateContext(
                InstanceTests.Month,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["claim"] = Claim },
                Starts());

        private static RuleContext Ledger(MemoryStore store) =>
            MemoryStore.Runtime(store).CreateContext(
                LedgerTests.Ledger,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["claim-month"] = InstanceTests.Month,
                    ["claim"] = Claim
                },
                Starts());

        private static Dictionary<string, string> Starts() =>
            new(StringComparer.Ordinal) { ["claim-start"] = InstanceTests.ClaimStart };

        /// <summary>A month with one claim, c-1, drafted at an amount.</summary>
        private static async Task<RuleContext> Drafted(MemoryStore store, int amount)
        {
            RuleContext month = Month(store);
            await month.ApplyAsync(Input("create"));
            await month.ApplyAsync(Input("claims.enterAmount", "c-1", $$"""{ "amount": {{amount}} }"""));
            return month;
        }

        private static string Input(string input, string? claim = null, string args = "{}") =>
            claim is null
                ? $$"""{ "input": "{{input}}", "args": {{args}} }"""
                : $$"""{ "input": "{{input}}", "at": { "claims": "{{claim}}" }, "args": {{args}} }""";

        private static string LedgerInput(string input, string month, string? claim = null, string args = "{}") =>
            claim is null
                ? $$"""{ "input": "{{input}}", "at": { "months": "{{month}}" }, "args": {{args}} }"""
                : $$"""{ "input": "{{input}}", "at": { "months": "{{month}}", "claims": "{{claim}}" }, "args": {{args}} }""";
    }
}
