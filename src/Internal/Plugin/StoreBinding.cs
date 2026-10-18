// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize.Abstraction.Store;

namespace Rulealize.Internal.Plugin
{
    /// <summary>The stores a host opened, by plugin, and who keeps a transition that spans them.</summary>
    /// <remarks>
    /// Taken from the runtime when a context is created, as the operations are, so what a
    /// context reads and writes through does not change underneath it.
    /// </remarks>
    /// <param name="Stores">Each opened store, by the identifier of the plugin that declared it.</param>
    /// <param name="Coordinator">Commits a transition that writes to more than one store, when the host gave one.</param>
    internal sealed record StoreBinding(ImmutableDictionary<string, IStateStore> Stores, IStoreCoordinator? Coordinator)
    {
        /// <summary>Gets a binding with no store and no coordinator.</summary>
        public static StoreBinding None { get; } = new(ImmutableDictionary.Create<string, IStateStore>(StringComparer.OrdinalIgnoreCase), null);
    }
}
