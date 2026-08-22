// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction.Value;

namespace Rulealize.Internal.Evaluation
{
    /// <summary>Whether a value read from a document names a value the runtime produced.</summary>
    /// <remarks>
    /// <para>
    /// Two documents ask this question and they have to answer it the same way. An input
    /// document names an argument, which is matched against its parameter's domain; an
    /// outcome document names a draw, which is matched against what could have come out of
    /// it. Both are the return leg of a trip the runtime opened by writing a value out, and
    /// a second implementation of the rule would eventually disagree with the first about a
    /// coordinate.
    /// </para>
    /// <para>
    /// Equal values match, and beyond that exactly one concession is made: text matches an
    /// opaque value whose canonical text it is. That is as wide as the trip needs and no
    /// wider. A number goes out as a number and comes back as one; only a value JSON has no
    /// form for has to travel as text, so only such a value has to be recognised in it.
    /// </para>
    /// <para>
    /// In particular <c>"2"</c> still does not match <c>2</c>. Different kinds are unequal in
    /// the value model, and a boundary that quietly disagreed with that would be a worse
    /// place to disagree than most.
    /// </para>
    /// </remarks>
    internal static class ValueMatch
    {
        /// <summary>Whether a supplied value names a produced one.</summary>
        /// <param name="supplied">What the document said.</param>
        /// <param name="produced">What the runtime produced.</param>
        /// <returns><see langword="true"/> when the one names the other.</returns>
        public static bool Matches(RuleValue supplied, RuleValue produced) =>
            supplied.Equals(produced)
            || (supplied is TextValue text
                && produced is OpaqueValue
                && produced.GetCanonicalText() is string canonical
                && string.Equals(canonical, text.Value, StringComparison.Ordinal));
    }
}
