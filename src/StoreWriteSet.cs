// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize.Abstraction.Store;

namespace Rulealize
{
    /// <summary>What one transition wrote to one store.</summary>
    /// <remarks>
    /// Handed to an <see cref="IStoreCoordinator"/> only, and only for a transition that wrote
    /// to more than one store.
    /// </remarks>
    public sealed class StoreWriteSet
    {
        internal StoreWriteSet(string store, IStateStoreSession session, ImmutableArray<StateWrite> writes)
        {
            Store = store;
            Session = session;
            Writes = writes;
        }

        /// <summary>Gets the store plugin's identifier, as the rule set's <c>requires</c> names it.</summary>
        public string Store { get; }

        /// <summary>Gets the session the transition read this store through, and commits through.</summary>
        public IStateStoreSession Session { get; }

        /// <summary>Gets the documents written to this store, in the order the transition wrote them.</summary>
        public ImmutableArray<StateWrite> Writes { get; }

        /// <inheritdoc />
        public override string ToString() => $"{Store}: {Writes.Length} write(s)";
    }
}
