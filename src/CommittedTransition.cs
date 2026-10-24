// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction.Store;

namespace Rulealize
{
    /// <summary>Where a transition kept in a store arrived, and what the store did about it.</summary>
    /// <remarks>
    /// <para>
    /// What <see cref="RuleContext.ApplyAsync(string, CancellationToken)"/> returns. There is no
    /// state document on it: the documents are the store's, and a host never handles one.
    /// </para>
    /// <para>
    /// <see cref="Commit"/> is the half of the record an input cannot carry. Keep it beside the
    /// input document, and the pair replays the same transition under the same keys however
    /// long afterwards, which is how a history of a collection reproduces it.
    /// </para>
    /// </remarks>
    public sealed class CommittedTransition
    {
        internal CommittedTransition(bool isTerminal, string? result, StateAddress at, TerminalStatus atStatus, string commit)
        {
            IsTerminal = isTerminal;
            Result = result;
            At = at;
            AtStatus = atStatus;
            Commit = commit;
        }

        /// <summary>Gets a value indicating whether the rule set considers its own new state final.</summary>
        /// <remarks>
        /// The rule set the context was created for, by its own <c>terminal</c>. Whether the
        /// instance the input was about is final is <see cref="AtStatus"/>.
        /// </remarks>
        public bool IsTerminal { get; }

        /// <summary>
        /// Gets the outcome the rule set reports about its own new state, or <see langword="null"/>
        /// when that state is not terminal or the rule set declares no result.
        /// </summary>
        public string? Result { get; }

        /// <summary>Gets the document the input was about.</summary>
        /// <remarks>
        /// The instance it created, under the key the store assigned; otherwise the innermost
        /// instance its <c>at</c> names; otherwise <see cref="StateAddress.Root"/>, the rule
        /// set's own document.
        /// </remarks>
        public StateAddress At { get; }

        /// <summary>Gets whether the document at <see cref="At"/> is final now, and what its outcome is.</summary>
        /// <remarks>
        /// Asked of the rule set held there, by its own <c>terminal</c>, about the state this
        /// transition left it in — so a reply to the input can say a claim was decided without
        /// reading it again, when another transition may have moved it since.
        /// </remarks>
        public TerminalStatus AtStatus { get; }

        /// <summary>Gets the <c>rulealize/commit/v1</c> document: the input, and the key the store assigned each new instance.</summary>
        public string Commit { get; }

        /// <inheritdoc />
        public override string ToString() =>
            IsTerminal ? $"terminal{(Result is null ? string.Empty : $" ({Result})")}" : "ongoing";
    }
}
