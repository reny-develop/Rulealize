// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction.Store;

namespace Rulealize
{
    /// <summary>A store that keeps documents in memory, and stands in for any store a rule set names.</summary>
    /// <remarks>
    /// <para>
    /// Evaluation is pure, so where the documents are kept changes no answer: a walk over a rule
    /// set, or a test of a host, can keep them here rather than in the store the rule set's
    /// <c>requires</c> names. <see cref="RuleRuntime.UseStore(string, IStateStore)"/> takes one
    /// for any store plugin, and <see cref="RuleRuntime.UseStore(MemoryStateStore)"/> for every
    /// one at once. The plugin is still loaded, because a rule set is built against it; nothing
    /// it would have opened is.
    /// </para>
    /// <para>
    /// It is a store like any other. A session remembers the version it read each document at,
    /// and on commit refuses, with <see cref="StateConflictException"/>, a write to a document
    /// that moved since; a document never written counts as unread by everyone. A new instance
    /// gets the key <c>1</c>, <c>2</c>, … — one more than its collection holds, so the key
    /// follows from the documents and not from what was created before — and a collection is
    /// read back in the order its instances were created.
    /// </para>
    /// <para>
    /// <see cref="Documents"/> and <see cref="Load"/> are what it adds: a position read out and
    /// put back, which is how one is kept between moves, or begun in.
    /// </para>
    /// </remarks>
    public sealed class MemoryStateStore : IStateStore
    {
        private readonly Dictionary<StateAddress, Stored> _documents = [];
        private long _order;

        /// <summary>Gets every document kept, the rule set's own first if it was written, then in the order they were created.</summary>
        public IReadOnlyList<KeyValuePair<StateAddress, string>> Documents =>
        [
            .. _documents
                .OrderBy(static entry => entry.Key.IsRoot ? 0 : 1)
                .ThenBy(static entry => entry.Value.Created)
                .Select(static entry => KeyValuePair.Create(entry.Key, entry.Value.State)),
        ];

        /// <summary>Replaces everything kept with these documents.</summary>
        /// <param name="documents">The documents, by address, in the order their instances were created.</param>
        /// <exception cref="ArgumentException">An address appears twice.</exception>
        /// <remarks>
        /// What <see cref="Documents"/> read out goes back in unchanged. Nothing is checked
        /// against a rule set here; a document that is not a state of one is refused when a
        /// transition reads it, as one from any store would be.
        /// </remarks>
        public void Load(IEnumerable<KeyValuePair<StateAddress, string>> documents)
        {
            ArgumentNullException.ThrowIfNull(documents);

            Dictionary<StateAddress, Stored> loaded = [];
            long order = 0;
            foreach ((StateAddress address, string state) in documents)
            {
                ArgumentNullException.ThrowIfNull(address, nameof(documents));
                ArgumentNullException.ThrowIfNull(state, nameof(documents));
                if (!loaded.TryAdd(address, new Stored(state, 1, ++order)))
                {
                    throw new ArgumentException($"'{address}' appears twice.", nameof(documents));
                }
            }

            _documents.Clear();
            foreach ((StateAddress address, Stored stored) in loaded)
            {
                _documents[address] = stored;
            }

            _order = order;
        }

        /// <inheritdoc />
        public Task<IStateStoreSession> OpenSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IStateStoreSession>(new Session(this));

        private int VersionOf(StateAddress address) =>
            _documents.TryGetValue(address, out Stored? held) ? held.Version : 0;

        // One more than the collection holds, and past any key a replay put there.
        private string NextKey(StateAddress holder, string alias)
        {
            int count = _documents.Keys.Count(address => !address.IsRoot && address.Alias == alias && address.Parent.Equals(holder));
            int next = count + 1;
            while (_documents.ContainsKey(holder.Child(alias, next.ToString(System.Globalization.CultureInfo.InvariantCulture))))
            {
                next++;
            }

            return next.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private sealed record Stored(string State, int Version, long Created);

        private sealed class Session(MemoryStateStore store) : IStateStoreSession
        {
            private readonly Dictionary<StateAddress, int> _read = [];
            private bool _committed;

            public Task<IReadOnlyDictionary<StateAddress, string>> ReadAsync(
                IReadOnlyList<StateAddress> addresses,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(addresses);

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
                ArgumentNullException.ThrowIfNull(holder);
                ArgumentException.ThrowIfNullOrEmpty(alias);

                IReadOnlyList<KeyValuePair<string, string>> instances =
                [
                    .. store._documents
                        .Where(entry => !entry.Key.IsRoot
                            && entry.Key.Parent.Equals(holder)
                            && string.Equals(entry.Key.Alias, alias, StringComparison.Ordinal))
                        .OrderBy(static entry => entry.Value.Created)
                        .Select(static entry => KeyValuePair.Create(entry.Key.Key, entry.Value.State)),
                ];

                return Task.FromResult(instances);
            }

            public Task<IReadOnlyDictionary<string, string>> CommitAsync(
                IReadOnlyList<StateWrite> writes,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(writes);
                if (_committed)
                {
                    throw new InvalidOperationException("A session commits once.");
                }

                _committed = true;

                foreach (StateWrite write in writes)
                {
                    if (write.IsInsert && write.Address.Key.StartsWith('~'))
                    {
                        continue;
                    }

                    // A replayed creation is read at its absence, as an unwritten keyed instance is.
                    int read = write.IsInsert ? 0 : _read.GetValueOrDefault(write.Address);
                    if (store.VersionOf(write.Address) != read)
                    {
                        throw new StateConflictException(
                            write.Address,
                            $"'{write.Address}' moved since it was read.");
                    }
                }

                Dictionary<string, string> created = new(StringComparer.Ordinal);
                foreach (StateWrite write in writes)
                {
                    StateAddress address = write.Address;
                    if (write.IsInsert && address.Key.StartsWith('~'))
                    {
                        string key = store.NextKey(address.Parent, address.Alias);
                        created[address.Key] = key;
                        address = address.Parent.Child(address.Alias, key);
                    }

                    store._documents[address] = new Stored(
                        write.State,
                        store.VersionOf(address) + 1,
                        store._documents.TryGetValue(address, out Stored? before) ? before.Created : ++store._order);
                }

                return Task.FromResult<IReadOnlyDictionary<string, string>>(created);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
