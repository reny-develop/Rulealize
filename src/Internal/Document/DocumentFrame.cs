// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction;

namespace Rulealize.Internal.Document
{
    /// <summary>The keys the core fixes in the three documents that travel per call.</summary>
    /// <remarks>
    /// <para>
    /// A state, an input and an outcome each have a frame the core owns entirely — the
    /// schema identifier, the rule set the document was written for, and one payload. What
    /// is inside the payload is the rule set's business; the frame is not, so an unfamiliar
    /// key in it is refused rather than passed over.
    /// </para>
    /// <para>
    /// Every key in a frame but the payload is optional, which is what makes the check worth
    /// its cost: a misspelled <c>ruleSet</c> is not a document that fails, it is a document
    /// whose identity was never checked, and the state it carries then belongs to whatever
    /// rule set happened to read it.
    /// </para>
    /// </remarks>
    internal static class DocumentFrame
    {
        /// <summary>Refuses a key the frame does not have.</summary>
        /// <param name="document">The parsed document.</param>
        /// <param name="what">Which document it is, article and all, for the message.</param>
        /// <param name="allowed">Every key the frame has.</param>
        /// <exception cref="RuleDocumentException">The document carries a key that is not one of them.</exception>
        public static void OnlyTheseKeys(JsonElement document, string what, params string[] allowed)
        {
            foreach (JsonProperty property in document.EnumerateObject())
            {
                if (allowed.Contains(property.Name, StringComparer.Ordinal))
                {
                    continue;
                }

                throw new RuleDocumentException(
                    $"'{property.Name}' is not a key of {what} document; those are {Listed(allowed)}.");
            }
        }

        private static string Listed(string[] names) =>
            string.Join(", ", names[..^1].Select(static name => $"'{name}'")) + $" and '{names[^1]}'";
    }
}
