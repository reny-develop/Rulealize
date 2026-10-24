// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction.Store;

namespace Rulealize
{
    /// <summary>Thrown when a call is about an instance there is no document for, in a collection whose keys a store generates.</summary>
    /// <remarks>
    /// <para>
    /// The one way a well-formed address can be about nothing, which is why it is told apart
    /// from a document that is wrong: a host answering for one instance at a time has a "not
    /// found" to give, and this is it. A <see cref="RuleDocumentException"/> still, so a caller
    /// that catches that goes on catching this.
    /// </para>
    /// <para>
    /// Never thrown for a collection whose key is written as a schema. Every key the schema
    /// admits is an instance already, and one with no document is at its collection's
    /// <c>start</c>.
    /// </para>
    /// </remarks>
    public sealed class InstanceNotFoundException : RuleDocumentException
    {
        /// <summary>Initializes a new instance of the <see cref="InstanceNotFoundException"/> class.</summary>
        /// <param name="at">The address there is no instance at.</param>
        public InstanceNotFoundException(StateAddress at)
            : base($"There is no instance at '{at}'.")
        {
            ArgumentNullException.ThrowIfNull(at);
            At = at;
        }

        /// <summary>Gets the address there is no instance at.</summary>
        public StateAddress At { get; }
    }
}
