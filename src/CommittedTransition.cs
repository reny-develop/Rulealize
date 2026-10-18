// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

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
        internal CommittedTransition(bool isTerminal, string? result, string commit)
        {
            IsTerminal = isTerminal;
            Result = result;
            Commit = commit;
        }

        /// <summary>Gets a value indicating whether the rule set considers the new state final.</summary>
        public bool IsTerminal { get; }

        /// <summary>
        /// Gets the outcome the rule set reports, or <see langword="null"/> when the state is
        /// not terminal or the rule set declares no result.
        /// </summary>
        public string? Result { get; }

        /// <summary>Gets the <c>rulealize/commit/v1</c> document: the input, and the key the store assigned each new instance.</summary>
        public string Commit { get; }

        /// <inheritdoc />
        public override string ToString() =>
            IsTerminal ? $"terminal{(Result is null ? string.Empty : $" ({Result})")}" : "ongoing";
    }
}
