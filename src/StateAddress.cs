// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;

namespace Rulealize
{
    /// <summary>Where one state document sits among the documents of a rule set that keeps instances.</summary>
    /// <remarks>
    /// <para>
    /// A rule set whose <c>uses</c> declares a <c>many</c> keeps each instance of what it holds
    /// as a document of its own, so a position is no longer one document. The rule set's own
    /// document is <see cref="Root"/>, and an instance is reached by naming the alias it is
    /// held under and its key: <c>claims/c-8</c>. An instance of a rule set that keeps
    /// instances of its own holds them under its address: <c>months/2026-10/claims/c-8</c>. The
    /// segments alternate that way, alias then key, outermost first, which is the order an input
    /// document's <c>at</c> gives them in.
    /// </para>
    /// <para>
    /// A key is whatever the store assigned, so it may contain anything, and the address is
    /// compared and written segment by segment rather than as one string.
    /// </para>
    /// </remarks>
    internal sealed class StateAddress : IEquatable<StateAddress>
    {
        private StateAddress(ImmutableArray<string> segments)
        {
            Segments = segments;
        }

        /// <summary>Gets the address of the rule set's own document.</summary>
        public static StateAddress Root { get; } = new([]);

        /// <summary>Gets the segments, alias then key, outermost first.</summary>
        public ImmutableArray<string> Segments { get; }

        /// <summary>Gets a value indicating whether this is the rule set's own document.</summary>
        public bool IsRoot => Segments.IsEmpty;

        /// <summary>Gets the address of one instance held under an alias.</summary>
        /// <param name="alias">The alias <c>uses</c> gave the collection.</param>
        /// <param name="key">The instance's key.</param>
        /// <returns>The address.</returns>
        public static StateAddress Of(string alias, string key) => Root.Child(alias, key);

        /// <summary>Gets the address of one instance held under an alias by the instance this address is.</summary>
        /// <param name="alias">The alias the instance's own rule set gave the collection.</param>
        /// <param name="key">The instance's key.</param>
        /// <returns>The address, one level further down: <c>months/2026-10/claims/c-8</c>.</returns>
        public StateAddress Child(string alias, string key)
        {
            ArgumentException.ThrowIfNullOrEmpty(alias);
            ArgumentException.ThrowIfNullOrEmpty(key);
            return new(Segments.AddRange(alias, key));
        }

        /// <summary>Gets the address of the instance or rule set that holds this one.</summary>
        /// <exception cref="InvalidOperationException">This is <see cref="Root"/>, which nothing holds.</exception>
        public StateAddress Parent => IsRoot
            ? throw new InvalidOperationException("The rule set's own document is held by nothing.")
            : new(Segments.RemoveRange(Segments.Length - 2, 2));

        /// <inheritdoc />
        public bool Equals(StateAddress? other) =>
            other is not null && Segments.AsSpan().SequenceEqual(other.Segments.AsSpan());

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as StateAddress);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            HashCode hash = default;
            foreach (string segment in Segments)
            {
                hash.Add(segment, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }

        /// <inheritdoc />
        /// <remarks>Segments joined with <c>/</c>, and the root as <c>/</c>. For reading, not for parsing.</remarks>
        public override string ToString() => IsRoot ? "/" : string.Join('/', Segments);
    }
}
