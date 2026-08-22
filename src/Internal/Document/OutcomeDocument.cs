// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using Rulealize.Abstraction.Value;
using Rulealize.Internal.RuleSet;

namespace Rulealize.Internal.Document
{
    /// <summary>An outcome document, read.</summary>
    internal sealed record OutcomeRequest(string Input, ImmutableArray<RuleValue> Draws);

    /// <summary>Reads the <c>rulealize/outcome/v1</c> document.</summary>
    /// <remarks>
    /// <para>
    /// The third document, and the one that says what happened rather than what anybody
    /// decided. An input document names a move; an outcome document names the draws that
    /// resolved it, in the order they were drawn. Together they determine a transition
    /// exactly, which is what makes a hand with chance in it replayable and auditable.
    /// </para>
    /// <para>
    /// It names its input as well as its rule set. An outcome paired with the wrong input is
    /// a real mistake with a silent failure mode — a script of the right length would apply
    /// and produce a state nobody meant — and one string in the document turns it into a
    /// sentence.
    /// </para>
    /// <para>
    /// Draws arrive as JSON scalars, so an opaque value arrives as its canonical text.
    /// Nothing here converts one; matching a drawn value against what could have come out of
    /// that draw belongs to <c>DrawTrail</c>, for the same reason matching an argument
    /// against its domain belongs to <c>RuleContext</c>.
    /// </para>
    /// </remarks>
    internal static class OutcomeDocument
    {
        public const string SchemaId = "rulealize/outcome/v1";

        public static OutcomeRequest Read(CompiledRuleSet ruleSet, JsonElement document)
        {
            if (document.ValueKind != JsonValueKind.Object)
            {
                throw new RuleDocumentException("An outcome document must be a JSON object.");
            }

            if (document.TryGetProperty("ruleSet", out JsonElement declared)
                && declared.ValueKind == JsonValueKind.String
                && !string.Equals(declared.GetString(), ruleSet.Qualified, StringComparison.Ordinal))
            {
                throw new RuleDocumentException(
                    $"This outcome was written for '{declared.GetString()}', but the context is '{ruleSet.Qualified}'.");
            }

            if (!document.TryGetProperty("input", out JsonElement name) || name.ValueKind != JsonValueKind.String)
            {
                throw new RuleDocumentException(
                    "An outcome document needs an 'input' naming the input it resolves.");
            }

            ImmutableArray<RuleValue>.Builder draws = ImmutableArray.CreateBuilder<RuleValue>();
            if (document.TryGetProperty("draws", out JsonElement drawn))
            {
                if (drawn.ValueKind != JsonValueKind.Array)
                {
                    throw new RuleDocumentException(
                        "The 'draws' of an outcome document must be an array, in the order they were drawn.");
                }

                int index = 0;
                foreach (JsonElement draw in drawn.EnumerateArray())
                {
                    draws.Add(ReadValue(draw, index++));
                }
            }

            return new OutcomeRequest(name.GetString()!, draws.ToImmutable());
        }

        /// <summary>Reads one drawn value.</summary>
        /// <remarks>
        /// Narrower than what an input argument may be. An argument may be a record, because
        /// a rule set may want to hand one in; a draw may not, because the runtime had to
        /// write this value out before anyone could write it back, and a record has no
        /// canonical text to be written as.
        /// </remarks>
        private static RuleValue ReadValue(JsonElement element, int index) => element.ValueKind switch
        {
            JsonValueKind.String => RuleValue.Text(element.GetString()!),
            JsonValueKind.Number => RuleValue.Number(element.GetDecimal()),
            JsonValueKind.True => RuleValue.True,
            JsonValueKind.False => RuleValue.False,
            JsonValueKind.Null => RuleValue.Null,
            _ => throw new RuleDocumentException(
                $"The draw at index {index} is {element.ValueKind.ToString().ToLowerInvariant()}, "
                + "and a drawn value is written as the runtime wrote it: a scalar or its canonical text.")
        };
    }
}
