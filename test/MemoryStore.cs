// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json.Nodes;

namespace Rulealize.Tests
{
    /// <summary>A store that keeps documents in a dictionary, each with a version.</summary>
    /// <remarks>
    /// <para>
    /// Everything the runtime leaves to a store and nothing more: it fetches what
    /// <see cref="RuleContext.GetReads"/> lists, remembers the version each was read at, and on
    /// commit refuses a write to a document that has moved since. Keys are <c>c-1</c>,
    /// <c>c-2</c>, … in the order instances are created, at whatever depth.
    /// </para>
    /// <para>
    /// An instance of a keyed collection that was never written has no document, so it is left
    /// out of what is fetched and counts as version 0. Two transitions that both find it so and
    /// both write it collide on that, as two that found the same version of any document do.
    /// </para>
    /// </remarks>
    internal sealed class MemoryStore
    {
        private readonly Dictionary<StateAddress, (string State, int Version)> _documents = [];
        private int _created;

        public string this[StateAddress address] => _documents[address].State;

        public static MemoryStore Opened(RuleContext rules)
        {
            MemoryStore store = new();
            store._documents[StateAddress.Root] = (rules.InitialState, 1);
            return store;
        }

        public bool Holds(StateAddress address) => _documents.ContainsKey(address);

        public IReadOnlyDictionary<StateAddress, string> Snapshot() =>
            _documents.ToDictionary(static entry => entry.Key, static entry => entry.Value.State);

        public IReadOnlyDictionary<StateAddress, string> Read(RuleContext rules, string input) =>
            Read(rules.GetReads(input));

        public IReadOnlyDictionary<StateAddress, string> Read(IEnumerable<StateAddress> addresses) =>
            addresses
                .Where(_documents.ContainsKey)
                .ToDictionary(static address => address, address => _documents[address].State);

        public Pending Begin(RuleContext rules, string input)
        {
            Dictionary<StateAddress, int> versions = rules.GetReads(input)
                .ToDictionary(static address => address, VersionOf);

            return new Pending(rules, input, rules.ApplyToState(input, Read(rules, input)), versions);
        }

        public string Commit(Pending pending)
        {
            foreach (StateWrite write in pending.Result.Writes.Where(static write => !write.IsInsert))
            {
                // A guard reads only what its transition writes, so a write never lands on a
                // document the transition did not read, and checking writes is enough.
                int read = pending.Versions[write.Address];
                if (VersionOf(write.Address) != read)
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
                    _documents[write.Address.Parent.Child(write.Address.Segments[^2], key)] = (write.State, 1);
                }
                else
                {
                    _documents[write.Address] = (write.State, VersionOf(write.Address) + 1);
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

        private int VersionOf(StateAddress address) =>
            _documents.TryGetValue(address, out (string State, int Version) held) ? held.Version : 0;

        public sealed record Pending(
            RuleContext Rules,
            string Input,
            TransitionResult Result,
            IReadOnlyDictionary<StateAddress, int> Versions);
    }
}
