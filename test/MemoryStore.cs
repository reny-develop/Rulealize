// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction.Plugin;
using Rulealize.Abstraction.Store;

namespace Rulealize.Tests
{
    /// <summary>A store that keeps documents in a dictionary, each with a version.</summary>
    /// <remarks>
    /// <para>
    /// Everything the contract leaves to a store and nothing more. A session remembers the
    /// version it read each document at, and on commit refuses a write to a document that has
    /// moved since. Keys are <c>c-1</c>, <c>c-2</c>, … in the order instances are created, at
    /// whatever depth, and a collection is read back in that order.
    /// </para>
    /// <para>
    /// A document that was never written has no entry and counts as version 0, so two
    /// transitions that both find a keyed instance unwritten and both write it collide on that,
    /// as two that found the same version of any document do. The rule set's own document is
    /// the same: nothing is stored until something writes it.
    /// </para>
    /// </remarks>
    internal sealed class MemoryStore : IStateStore
    {
        /// <summary>The identifier of the plugin that declares this store.</summary>
        public const string Plugin = "Rulealize.Tests.MemoryStore";

        /// <summary>A second plugin declaring the same kind of store, for a rule set kept apart from its holder.</summary>
        public const string OtherPlugin = "Rulealize.Tests.OtherStore";

        /// <summary>Every published vocabulary and this store, as a <c>requires</c> a test document can paste in.</summary>
        public const string Requires = $$"""
              "requires": [
            {{StandardRuntime.Vocabularies}},
                { "plugin": "{{Plugin}}" }
              ],
            """;

        private readonly Dictionary<StateAddress, Stored> _documents = [];
        private int _created;
        private long _order;

        /// <summary>Gets or sets something to run once inside the next commit, before anything is checked.</summary>
        /// <remarks>How a test has another transition get there first.</remarks>
        public Func<Task>? BeforeNextCommit { get; set; }

        /// <summary>Gets how many sessions have been opened.</summary>
        public int Sessions { get; private set; }

        public string this[StateAddress address] => _documents[address].State;

        /// <summary>Builds a runtime with the standard plugins and both store plugins, given these stores.</summary>
        public static RuleRuntime Runtime(MemoryStore store, MemoryStore? other = null)
        {
            RuleRuntime runtime = new RuleRuntime()
                .LoadPluginsFrom(StandardRuntime.PluginFolder)
                .AddPlugin(new MemoryStorePlugin(Plugin, "memory"))
                .AddPlugin(new MemoryStorePlugin(OtherPlugin, "other"))
                .UseStore(Plugin, store);

            return other is null ? runtime : runtime.UseStore(OtherPlugin, other);
        }

        public bool Holds(StateAddress address) => _documents.ContainsKey(address);

        /// <summary>Reads what an input reads, the way the runtime would, for a test of the pure overloads.</summary>
        public IReadOnlyDictionary<StateAddress, string> Read(RuleContext rules, string input)
        {
            Dictionary<StateAddress, string> states = rules.GetReads(input)
                .Where(_documents.ContainsKey)
                .ToDictionary(static address => address, address => _documents[address].State);
            states.TryAdd(StateAddress.Root, rules.InitialState);
            return states;
        }

        public Task<IStateStoreSession> OpenSessionAsync(CancellationToken cancellationToken = default)
        {
            Sessions++;
            return Task.FromResult<IStateStoreSession>(new Session(this));
        }

        private int VersionOf(StateAddress address) =>
            _documents.TryGetValue(address, out Stored? held) ? held.Version : 0;

        private sealed record Stored(string State, int Version, long Created);

        private sealed class Session(MemoryStore store) : IStateStoreSession
        {
            private readonly Dictionary<StateAddress, int> _read = [];
            private bool _committed;

            public Task<IReadOnlyDictionary<StateAddress, string>> ReadAsync(
                IReadOnlyList<StateAddress> addresses,
                CancellationToken cancellationToken = default)
            {
                Dictionary<StateAddress, string> found = [];
                foreach (StateAddress address in addresses)
                {
                    _read[address] = store.VersionOf(address);
                    if (store._documents.TryGetValue(address, out Stored? held))
                    {
                        found[address] = held.State;
                    }
                }

                return Task.FromResult<IReadOnlyDictionary<StateAddress, string>>(found);
            }

            public Task<IReadOnlyList<KeyValuePair<string, string>>> ReadCollectionAsync(
                StateAddress holder,
                string alias,
                CancellationToken cancellationToken = default)
            {
                IReadOnlyList<KeyValuePair<string, string>> instances =
                [
                    .. store._documents
                        .Where(entry => !entry.Key.IsRoot
                            && entry.Key.Parent.Equals(holder)
                            && string.Equals(entry.Key.Alias, alias, StringComparison.Ordinal))
                        .OrderBy(static entry => entry.Value.Created)
                        .Select(static entry => KeyValuePair.Create(entry.Key.Key, entry.Value.State))
                ];

                return Task.FromResult(instances);
            }

            public async Task<IReadOnlyDictionary<string, string>> CommitAsync(
                IReadOnlyList<StateWrite> writes,
                CancellationToken cancellationToken = default)
            {
                if (_committed)
                {
                    throw new InvalidOperationException("A session commits once.");
                }

                _committed = true;

                if (store.BeforeNextCommit is Func<Task> meanwhile)
                {
                    store.BeforeNextCommit = null;
                    await meanwhile();
                }

                foreach (StateWrite write in writes)
                {
                    if (write.IsInsert && write.Address.Key.StartsWith('~'))
                    {
                        continue;
                    }

                    // A replayed creation was read at its absence, as an unwritten keyed instance is.
                    int read = write.IsInsert ? 0 : _read.GetValueOrDefault(write.Address);
                    if (store.VersionOf(write.Address) != read)
                    {
                        throw new StateConflictException(
                            write.Address,
                            $"'{write.Address}' moved since it was read at version {read}.");
                    }
                }

                Dictionary<string, string> created = new(StringComparer.Ordinal);
                foreach (StateWrite write in writes)
                {
                    StateAddress address = write.Address;
                    if (write.IsInsert && address.Key.StartsWith('~'))
                    {
                        string key = $"c-{++store._created}";
                        created[address.Key] = key;
                        address = address.Parent.Child(address.Alias, key);
                    }

                    store._documents[address] = new Stored(
                        write.State,
                        store.VersionOf(address) + 1,
                        store._documents.TryGetValue(address, out Stored? before) ? before.Created : ++store._order);
                }

                return created;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>Declares <see cref="MemoryStore"/>, which is all a store plugin has to do.</summary>
    internal sealed class MemoryStorePlugin(string id, string @namespace) : IRulealizePlugin
    {
        public PluginManifest Manifest { get; } = new(id, new Version(1, 0, 0), @namespace);

        public void Register(IPluginRegistry registry) => registry.AddStore<MemoryStore>();
    }
}
