// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Store;

namespace Rulealize.Tests
{
    /// <summary>An input that draws, applied to the documents a store keeps.</summary>
    /// <remarks>
    /// The claim of <see cref="InstanceTests"/>, with one input more: <c>assign</c> picks who
    /// approves it, by chance. The month offers it at the claim as it offers every input of the
    /// claim's own, and the store keeps the claim, so what may happen is asked through the store
    /// and the host picks one; the commit records what was drawn, so the input and its commit
    /// replay it.
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class StoreOutcomeTests
    {
        private static readonly string Drawing = InstanceTests.Claim.Replace(
            "\"enterAmount\": {",
            """
            "assign": {
                              "when": { "op": "cmp.eq", "left": "$stage", "right": "draft" },
                              "effects": [ { "op": "state.set", "path": "approver",
                                             "value": { "op": "chance.pick", "of": { "op": "seq.of", "of": ["ann", "bo"] } } } ]
                            },
                            "enterAmount": {
            """,
            StringComparison.Ordinal);

        private static readonly StateAddress First = StateAddress.Of("claims", "c-1");

        [Fact]
        public async Task WhatMayHappenIsAskedThroughTheStoreAndNothingIsWritten()
        {
            MemoryStore store = new();
            RuleContext month = await Created(store);
            string before = store[First];

            StoreOutcomeSet outcomes = await month.GetOutcomesAsync(Assign, 10);

            Assert.Equal(["ann (0.5)", "bo (0.5)"], outcomes.Select(static outcome => outcome.ToString()));
            Assert.Equal(1, outcomes.Coverage);
            Assert.False(outcomes.Truncated);
            Assert.All(outcomes, outcome => Assert.Equal(First, outcome.At));
            Assert.Equal(before, store[First]);
        }

        [Fact]
        public async Task AnInputThatDrawsIsNotAppliedWithoutAnOutcome()
        {
            RuleContext month = await Created(new MemoryStore());

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => month.ApplyAsync(Assign));

            Assert.Contains("GetOutcomesAsync", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheOutcomeTheHostChoseIsKept()
        {
            MemoryStore store = new();
            RuleContext month = await Created(store);
            StoreOutcome bo = (await month.GetOutcomesAsync(Assign, 10))[1];

            CommittedTransition kept = await month.ApplyAsync(Assign, bo);

            Assert.Equal("bo", Data(store[First]).GetProperty("approver").GetString());
            Assert.Equal(First, kept.At);
        }

        [Fact]
        public async Task TheCommitRecordsWhatWasDrawn()
        {
            MemoryStore store = new();
            RuleContext month = await Created(store);
            StoreOutcome ann = (await month.GetOutcomesAsync(Assign, 10))[0];

            CommittedTransition kept = await month.ApplyAsync(Assign, ann);

            using JsonDocument commit = JsonDocument.Parse(kept.Commit);
            Assert.Equal(["ann"], commit.RootElement.GetProperty("draws").EnumerateArray().Select(static draw => draw.GetString()));
        }

        [Fact]
        public async Task AnInputThatDrawsNothingRecordsNoDraws()
        {
            RuleContext month = Context(new MemoryStore());

            StoreOutcome certain = Assert.Single(await month.GetOutcomesAsync(Create, 10));
            CommittedTransition kept = await month.ApplyAsync(Create, certain);

            Assert.Equal(1, certain.Probability);
            Assert.Empty(certain.Draws);
            Assert.Equal(StateAddress.Of("claims", "~1"), certain.At);
            Assert.Equal(First, kept.At);
            using JsonDocument commit = JsonDocument.Parse(kept.Commit);
            Assert.False(commit.RootElement.TryGetProperty("draws", out _));
        }

        [Fact]
        public async Task AHistoryWithDrawsReplaysIntoAnEmptyStore()
        {
            MemoryStore store = new();
            RuleContext month = Context(store);
            List<(string Input, string Commit)> history = [(Create, (await month.ApplyAsync(Create)).Commit)];
            history.Add((Assign, (await month.ApplyAsync(Assign, (await month.GetOutcomesAsync(Assign, 10))[1])).Commit));

            MemoryStore rebuilt = new();
            RuleContext again = Context(rebuilt);
            foreach ((string input, string commit) in history)
            {
                await again.ApplyAsync(input, commit);
            }

            Assert.Equal(store[First], rebuilt[First]);
        }

        [Fact]
        public async Task ACommitWithoutTheDrawsDoesNotReplayAnInputThatDraws()
        {
            MemoryStore store = new();
            RuleContext month = await Created(store);
            string commit = """{ "$schema": "rulealize/commit/v1", "ruleSet": "claim-month@0.1.0", "input": "claims.assign" }""";

            RuleDocumentException refused = await Assert.ThrowsAsync<RuleDocumentException>(
                () => month.ApplyAsync(Assign, commit));

            Assert.Contains("writes down 0 draws", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ACommitWithDrawsDoesNotReplayAnInputThatDrawsNothing()
        {
            MemoryStore store = new();
            RuleContext month = await Created(store);
            string input = """{ "input": "claims.enterAmount", "at": { "claims": "c-1" }, "args": { "amount": 5 } }""";
            string commit = """{ "$schema": "rulealize/commit/v1", "ruleSet": "claim-month@0.1.0", "input": "claims.enterAmount", "draws": ["ann"] }""";

            RuleDocumentException refused = await Assert.ThrowsAsync<RuleDocumentException>(
                () => month.ApplyAsync(input, commit));

            Assert.Contains("writes down 1 draws", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AnOutcomeOfAnotherInputIsRefused()
        {
            RuleContext month = await Created(new MemoryStore());
            StoreOutcome certain = Assert.Single(await month.GetOutcomesAsync(Create, 10));

            await Assert.ThrowsAsync<ArgumentException>(() => month.ApplyAsync(Assign, certain));
        }

        [Fact]
        public async Task AnOutcomeTheDocumentsNoLongerAllowIsRefused()
        {
            MemoryStore store = new();
            RuleContext month = await Created(store);
            StoreOutcome bo = (await month.GetOutcomesAsync(Assign, 10))[1];
            await month.ApplyAsync("""{ "input": "claims.enterAmount", "at": { "claims": "c-1" }, "args": { "amount": 5 } }""");
            await month.ApplyAsync("""{ "input": "submit", "at": { "claims": "c-1" }, "args": { "by": "ann" } }""");

            await Assert.ThrowsAsync<IllegalInputException>(() => month.ApplyAsync(Assign, bo));
            Assert.Equal("bo", Data(store[First]).GetProperty("approver").GetString());
        }

        private const string Create = """{ "input": "create" }""";

        private const string Assign = """{ "input": "claims.assign", "at": { "claims": "c-1" } }""";

        private static RuleContext Context(MemoryStore store) =>
            MemoryStore.Runtime(store).CreateContext(
                InstanceTests.Month,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["claim"] = Drawing },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["claim-start"] = InstanceTests.ClaimStart });

        /// <summary>A month with one claim, c-1, in draft.</summary>
        private static async Task<RuleContext> Created(MemoryStore store)
        {
            RuleContext month = Context(store);
            await month.ApplyAsync(Create);
            return month;
        }

        private static JsonElement Data(string state) =>
            JsonDocument.Parse(state).RootElement.GetProperty("data").Clone();
    }
}
