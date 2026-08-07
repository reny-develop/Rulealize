// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;

namespace Rulealize
{
    /// <summary>
    /// Thrown when a state or input document handed to a context is not one this rule set
    /// can accept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// State documents come from outside — written by hand, produced by another system,
    /// stored and replayed — so being strict about them is the whole point of declaring a
    /// schema. Every violation found is reported, not just the first, because a malformed
    /// document is usually malformed in more than one place and diagnosing it one round trip
    /// at a time is miserable.
    /// </para>
    /// </remarks>
    public class RuleDocumentException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="RuleDocumentException"/> class.</summary>
        /// <param name="message">What is wrong.</param>
        public RuleDocumentException(string message)
            : base(message)
        {
            Violations = [];
        }

        /// <summary>Initializes a new instance of the <see cref="RuleDocumentException"/> class.</summary>
        /// <param name="message">A summary.</param>
        /// <param name="violations">One entry per problem found, each naming where it is.</param>
        public RuleDocumentException(string message, ImmutableArray<string> violations)
            : base(violations.IsDefaultOrEmpty ? message : $"{message}{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", violations)}")
        {
            Violations = violations.IsDefault ? [] : violations;
        }

        /// <summary>Initializes a new instance of the <see cref="RuleDocumentException"/> class.</summary>
        /// <param name="message">What is wrong.</param>
        /// <param name="innerException">The underlying cause.</param>
        public RuleDocumentException(string message, Exception innerException)
            : base(message, innerException)
        {
            Violations = [];
        }

        /// <summary>Gets every problem found, one entry each.</summary>
        public ImmutableArray<string> Violations { get; }
    }
}
