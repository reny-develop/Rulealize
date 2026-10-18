// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize
{
    /// <summary>One document a transition writes.</summary>
    /// <remarks>
    /// <para>
    /// The runtime never stores anything. It hands back what a transition wrote, document by
    /// document, and keeping those documents — checking each against the version it was read
    /// at, assigning a key to a new one — is the store's. Which documents are written is
    /// settled by the input document and the rule set alone, so a store can tell which
    /// versions it has to check before anything is evaluated.
    /// </para>
    /// <para>
    /// A new instance is written under a temporary key, <c>~1</c>, because its real key does
    /// not exist until the store assigns one. A <c>rulealize/commit/v1</c> document records
    /// which key that was, and replaying the input with it writes the same instance under the
    /// same key.
    /// </para>
    /// </remarks>
    internal sealed class StateWrite
    {
        internal StateWrite(StateAddress address, string state, bool isInsert)
        {
            Address = address;
            State = state;
            IsInsert = isInsert;
        }

        /// <summary>Gets where the document goes.</summary>
        public StateAddress Address { get; }

        /// <summary>Gets the document, a complete <c>rulealize/state/v1</c>.</summary>
        public string State { get; }

        /// <summary>Gets a value indicating whether this is a new instance, under a temporary key.</summary>
        public bool IsInsert { get; }

        /// <inheritdoc />
        public override string ToString() => IsInsert ? $"insert {Address}" : $"write {Address}";
    }
}
