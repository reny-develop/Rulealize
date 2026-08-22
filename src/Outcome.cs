// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Rulealize.Abstraction.Value;

namespace Rulealize
{
    /// <summary>One thing that can happen when an input is applied, and where it leads.</summary>
    /// <remarks>
    /// <para>
    /// An input says what somebody decided. An outcome says what the world did about it —
    /// which card came off the deck, which face the die landed on — and the two together
    /// determine a transition exactly. That is why <see cref="Result"/> is here rather than
    /// something the caller has to go and compute: enumerating the alternatives and saying
    /// where each one leads is one question, and splitting it would mean applying every
    /// branch twice.
    /// </para>
    /// <para>
    /// <see cref="ToOutcomeDocument"/> is what makes this replayable. Handing that document
    /// back to <c>ApplyToState</c> beside the input it came from produces
    /// <see cref="Result"/> again, exactly, however long afterwards — so a log of inputs and
    /// outcomes is a log the runtime can be made to walk a second time.
    /// </para>
    /// <para>
    /// An input that draws nothing still has one of these, with a probability of one and no
    /// draws in it. That is not a special case dressed up as a general one: it is what makes
    /// the search over a rule set with chance in it and the search over a rule set without
    /// any the same two calls in the same order.
    /// </para>
    /// </remarks>
    public sealed class Outcome
    {
        private readonly string _input;
        private readonly ImmutableArray<RuleValue> _values;

        internal Outcome(
            string input,
            ImmutableArray<RuleValue> values,
            ImmutableArray<string> draws,
            double probability,
            TransitionResult result)
        {
            _input = input;
            _values = values;
            Draws = draws;
            Probability = probability;
            Result = result;
        }

        /// <summary>Gets how likely this outcome is, among the ones this input can have.</summary>
        /// <remarks>
        /// Between zero and one, and the probabilities of a complete set sum to one but for
        /// what floating point loses. An input that draws nothing has a single outcome of
        /// probability one.
        /// </remarks>
        public double Probability { get; }

        /// <summary>Gets what was drawn, in order, rendered as text.</summary>
        /// <remarks>
        /// The readable view, as <see cref="ValidInput.Arguments"/> is for arguments. What
        /// travels in a document is <see cref="ToOutcomeDocument"/>, where a number is a
        /// number.
        /// </remarks>
        public ImmutableArray<string> Draws { get; }

        /// <summary>Gets where this outcome leads.</summary>
        public TransitionResult Result { get; }

        /// <summary>Writes this as an outcome document, ready to replay.</summary>
        /// <returns>A <c>rulealize/outcome/v1</c> document.</returns>
        /// <param name="ruleSet">The rule set identity to stamp on it, as <c>id@version</c>.</param>
        public string ToOutcomeDocument(string ruleSet)
        {
            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("$schema", Internal.Document.OutcomeDocument.SchemaId);
                writer.WriteString("ruleSet", ruleSet);
                writer.WriteString("input", _input);
                writer.WritePropertyName("draws");
                WriteDraws(writer);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string chance = Probability.ToString("0.###", CultureInfo.InvariantCulture);
            return Draws.IsEmpty ? $"certain ({chance})" : $"{string.Join(", ", Draws)} ({chance})";
        }

        internal void WriteTo(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("draws");
            WriteDraws(writer);
            writer.WriteNumber("probability", Probability);
            writer.WriteBoolean("terminal", Result.IsTerminal);
            if (Result.Result is not null)
            {
                writer.WriteString("result", Result.Result);
            }

            writer.WriteEndObject();
        }

        private void WriteDraws(Utf8JsonWriter writer)
        {
            writer.WriteStartArray();
            foreach (RuleValue value in _values)
            {
                switch (value)
                {
                    case NullValue:
                        writer.WriteNullValue();
                        break;

                    case BooleanValue boolean:
                        writer.WriteBooleanValue(boolean.Value);
                        break;

                    case NumberValue number:
                        writer.WriteNumberValue(number.Value);
                        break;

                    default:
                        // Text, and everything opaque, which the value model requires to
                        // have a text form precisely so that it can be written here.
                        writer.WriteStringValue(value.GetCanonicalText());
                        break;
                }
            }

            writer.WriteEndArray();
        }
    }

    /// <summary>Everything that can happen to one input, and how much of it the search saw.</summary>
    /// <remarks>
    /// <para>
    /// Ordered by probability, most likely first. That is what makes a limit useful rather
    /// than arbitrary: what gets dropped is always the least of it, so a caller that stops
    /// early has the part of the distribution that matters.
    /// </para>
    /// <para>
    /// <see cref="Coverage"/> is the reason <see cref="Truncated"/> is not enough on its own.
    /// A truncated list of legal moves is still a set of legal moves; a truncated list of
    /// outcomes is a probability distribution that no longer sums to one, and a search
    /// deciding whether to trust it needs to know how much of the mass it is holding rather
    /// than merely that some is missing.
    /// </para>
    /// </remarks>
    public sealed class OutcomeSet(
        ImmutableArray<Outcome> outcomes,
        double coverage,
        int evaluated,
        bool truncated)
        : IReadOnlyList<Outcome>
    {
        /// <summary>Gets the number of outcomes found. Never zero for an input the rules allow.</summary>
        public int Count => outcomes.Length;

        /// <summary>Gets the probability these outcomes account for between them.</summary>
        /// <remarks>
        /// One when the search ran to the end, but for what floating point loses. Less when
        /// the limit cut it short, and then it says how much less.
        /// </remarks>
        public double Coverage => coverage;

        /// <summary>Gets a value indicating whether the search stopped at the limit.</summary>
        public bool Truncated => truncated;

        /// <summary>Gets how many times the input's effects were run to find these.</summary>
        /// <remarks>
        /// More than <see cref="Count"/> whenever there is a draw, because reaching one means
        /// running the effects up to it: where the draws are is not known until the effects
        /// are evaluated, so each branch is found by replaying from the start.
        /// </remarks>
        public int Evaluated => evaluated;

        /// <inheritdoc />
        public Outcome this[int index] => outcomes[index];

        /// <inheritdoc />
        public IEnumerator<Outcome> GetEnumerator() => ((IEnumerable<Outcome>)outcomes).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Writes the set as an array of outcome descriptions.</summary>
        /// <returns>The JSON array.</returns>
        public string ToJson()
        {
            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartArray();
                foreach (Outcome outcome in outcomes)
                {
                    outcome.WriteTo(writer);
                }

                writer.WriteEndArray();
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}
