// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction.Store;

namespace Rulealize.Tests
{
    /// <summary>The store that keeps documents in memory, in place of whichever store a rule set names.</summary>
    /// <remarks>
    /// The month names <see cref="MemoryStore"/>'s plugin, and nothing here opens a
    /// <see cref="MemoryStore"/>: the plugin is loaded because the month is built against it, and
    /// the documents are kept in a <see cref="MemoryStateStore"/> instead.
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class MemoryStateStoreTests(StandardRuntime standard)
    {
        [Fact]
        public async Task AMemoryStoreStandsInForTheStoreAPluginDeclared()
        {
            MemoryStateStore store = new();
            RuleContext month = Runtime().UseStore(MemoryStore.Plugin, store).CreateContext(InstanceTests.Month, Held, Starts);

            await month.ApplyAsync(Input("create"));

            Assert.Equal([StateAddress.Of("claims", "1")], store.Documents.Select(static document => document.Key));
        }

        [Fact]
        public async Task AKeyIsOneMoreThanTheCollectionHolds()
        {
            MemoryStateStore store = new();
            RuleContext month = Month(store);

            await month.ApplyAsync(Input("create"));
            IReadOnlyList<KeyValuePair<StateAddress, string>> one = store.Documents;
            await month.ApplyAsync(Input("create"));
            Assert.Equal("3", Created(await month.ApplyAsync(Input("create")), "~1"));

            // Put back where it held one, the next is 2 again: the key follows from the
            // documents, not from what was created before.
            store.Load(one);
            Assert.Equal("2", Created(await month.ApplyAsync(Input("create")), "~1"));
        }

        [Fact]
        public async Task DocumentsGoBackAsTheyCameOut()
        {
            MemoryStateStore store = new();
            RuleContext month = Month(store);
            await month.ApplyAsync(Input("create"));
            await month.ApplyAsync(Input("claims.enterAmount", "1", """{ "amount": 32000 }"""));
            await month.ApplyAsync(Input("create"));
            IReadOnlyList<KeyValuePair<StateAddress, string>> before = store.Documents;
            string claims = await month.ProjectAsync("claims");

            await month.ApplyAsync(Input("submit", "1", """{ "by": "ann" }"""));
            await month.ApplyAsync(Input("create"));
            store.Load(before);

            Assert.Equal(before, store.Documents);
            Assert.Equal(claims, await month.ProjectAsync("claims"));
        }

        [Fact]
        public async Task TheOrderInstancesWereLoadedInIsTheOrderTheyWereCreatedIn()
        {
            MemoryStateStore store = new();
            RuleContext month = Month(store);
            await month.ApplyAsync(Input("create"));
            await month.ApplyAsync(Input("create"));

            store.Load([.. store.Documents.Reverse()]);

            using JsonDocument answer = JsonDocument.Parse(await month.ProjectAsync("claims"));
            Assert.Equal(["2", "1"], answer.RootElement.EnumerateArray().Select(static row => row.GetProperty("key").GetString()));
        }

        [Fact]
        public void AnAddressLoadedTwiceIsRefused()
        {
            MemoryStateStore store = new();
            KeyValuePair<StateAddress, string> claim = KeyValuePair.Create(StateAddress.Of("claims", "1"), InstanceTests.ClaimStart);

            Assert.Throws<ArgumentException>(() => store.Load([claim, claim]));
        }

        [Fact]
        public async Task EveryStoreKeptInMemoryIsOneStore()
        {
            // The month keeps its claims in the other store, so submitting writes to two: with
            // the stores themselves that takes a coordinator, and in memory it is one commit.
            MemoryStateStore store = new();
            RuleContext ledger = Runtime().UseStore(store).CreateContext(
                LedgerTests.Ledger,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["claim-month"] = LedgerTests.MonthElsewhere,
                    ["claim"] = InstanceTests.Claim,
                },
                Starts);
            const string At = """ "at": { "months": "2026-10", "claims": "1" } """;

            await ledger.ApplyAsync("""{ "input": "months.create", "at": { "months": "2026-10" }, "args": {} }""");
            await ledger.ApplyAsync($$"""{ "input": "months.claims.enterAmount", {{At}}, "args": { "amount": 32000 } }""");
            await ledger.ApplyAsync($$"""{ "input": "months.submit", {{At}}, "args": { "by": "ann" } }""");

            StateAddress october = StateAddress.Of("months", "2026-10");
            Dictionary<StateAddress, string> kept = store.Documents.ToDictionary();
            Assert.Equal(32000, Data(kept[october]).GetProperty("used").GetInt32());
            Assert.Equal("review", Data(kept[october.Child("claims", "1")]).GetProperty("stage").GetString());
        }

        [Fact]
        public async Task AStoreGivenForAPluginIsStillUsedForIt()
        {
            MemoryStateStore memory = new();
            MemoryStore own = new();
            RuleContext month = Runtime().UseStore(memory).UseStore(MemoryStore.Plugin, own)
                .CreateContext(InstanceTests.Month, Held, Starts);

            await month.ApplyAsync(Input("create"));

            Assert.Empty(memory.Documents);
            Assert.True(own.Holds(StateAddress.Of("claims", "c-1")));
        }

        [Fact]
        public async Task AWriteToADocumentThatMovedIsStillRefused()
        {
            MemoryStateStore store = new();
            RuleContext month = Month(store);
            await month.ApplyAsync(Input("create"));

            await using IStateStoreSession first = await store.OpenSessionAsync();
            await first.ReadAsync([StateAddress.Of("claims", "1")]);
            await month.ApplyAsync(Input("claims.enterAmount", "1", """{ "amount": 5 }"""));

            await Assert.ThrowsAsync<StateConflictException>(() => first.CommitAsync(
                [new StateWrite(StateAddress.Of("claims", "1"), InstanceTests.ClaimStart, isInsert: false)]));
        }

        private static readonly Dictionary<string, string> Held = new(StringComparer.Ordinal) { ["claim"] = InstanceTests.Claim };

        private static readonly Dictionary<string, string> Starts = new(StringComparer.Ordinal) { ["claim-start"] = InstanceTests.ClaimStart };

        /// <summary>The standard plugins and both store plugins, given no store.</summary>
        private static RuleRuntime Runtime() => new RuleRuntime()
            .LoadPluginsFrom(StandardRuntime.PluginFolder)
            .AddPlugin(new MemoryStorePlugin(MemoryStore.Plugin, "memory"))
            .AddPlugin(new MemoryStorePlugin(MemoryStore.OtherPlugin, "other"));

        private static RuleContext Month(MemoryStateStore store) =>
            Runtime().UseStore(store).CreateContext(InstanceTests.Month, Held, Starts);

        private static string Created(CommittedTransition transition, string temporary)
        {
            using JsonDocument commit = JsonDocument.Parse(transition.Commit);
            return commit.RootElement.GetProperty("created").GetProperty(temporary).GetString()!;
        }

        private static string Input(string input, string? claim = null, string args = "{}") =>
            claim is null
                ? $$"""{ "input": "{{input}}", "args": {{args}} }"""
                : $$"""{ "input": "{{input}}", "at": { "claims": "{{claim}}" }, "args": {{args}} }""";

        private static JsonElement Data(string state) =>
            JsonDocument.Parse(state).RootElement.GetProperty("data").Clone();
    }
}
