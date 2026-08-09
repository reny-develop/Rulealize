// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize
{
    /// <summary>
    /// Thrown when an input is applied to a state whose rules do not allow it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from a malformed document and from an evaluation fault: the document was
    /// well formed and everything evaluated cleanly, and the answer was that this move is
    /// not available. Callers that drive a game loop want to tell those apart, because only
    /// this one is the user's fault.
    /// </para>
    /// <para>
    /// Raised for either way a rule set can say no — an argument that is not among the
    /// values its parameter's domain allows, or a guard that rejects the input. They are not
    /// distinguished, because from the caller's side they are the same answer: this is not
    /// one of the inputs available here.
    /// </para>
    /// </remarks>
    public class IllegalInputException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="IllegalInputException"/> class.</summary>
        /// <param name="input">The input that was refused.</param>
        /// <param name="message">Why it was refused.</param>
        public IllegalInputException(string input, string message)
            : base(message)
        {
            Input = input;
        }

        /// <summary>Initializes a new instance of the <see cref="IllegalInputException"/> class.</summary>
        /// <param name="message">Why the input was refused.</param>
        public IllegalInputException(string message)
            : base(message)
        {
            Input = string.Empty;
        }

        /// <summary>Initializes a new instance of the <see cref="IllegalInputException"/> class.</summary>
        /// <param name="message">Why the input was refused.</param>
        /// <param name="innerException">The underlying cause.</param>
        public IllegalInputException(string message, Exception innerException)
            : base(message, innerException)
        {
            Input = string.Empty;
        }

        /// <summary>Gets the name of the input that was refused.</summary>
        public string Input { get; }
    }
}
