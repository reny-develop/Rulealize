// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize
{
    /// <summary>Keeps a transition that writes to more than one store, all of it or none of it.</summary>
    /// <remarks>
    /// <para>
    /// A rule set and a rule set it holds may name different stores, and a transition that
    /// writes both — a claim kept in one, the month whose limit it uses in another — cannot be
    /// committed in one transaction by either. Whether such a transition is refused, committed
    /// in two phases or compensated afterwards is the host's to decide, together with what its
    /// stores can do; the runtime does not check it and does not refuse it.
    /// </para>
    /// <para>
    /// What the runtime does is notice. A transition that writes to one store is committed
    /// through that store's session, and this is never asked. One that writes to two or more is
    /// handed here, split per store, and with no coordinator given to
    /// <see cref="RuleRuntime.UseCoordinator(IStoreCoordinator)"/> it fails rather than commit
    /// part of itself.
    /// </para>
    /// </remarks>
    public interface IStoreCoordinator
    {
        /// <summary>Commits what one transition wrote across several stores.</summary>
        /// <param name="writes">What the transition wrote, one entry per store, each with the session that read it.</param>
        /// <param name="cancellationToken">Cancels the commit.</param>
        /// <returns>
        /// The key each store assigned each new instance, by the temporary key it was written
        /// under, from every store together.
        /// </returns>
        /// <remarks>
        /// Commit through each <see cref="StoreWriteSet.Session"/>; the runtime disposes of the
        /// sessions afterwards. A
        /// <see cref="Abstraction.Store.StateConflictException"/> a session throws may be let
        /// through as it is.
        /// </remarks>
        Task<IReadOnlyDictionary<string, string>> CommitAsync(
            IReadOnlyList<StoreWriteSet> writes,
            CancellationToken cancellationToken = default);
    }
}
