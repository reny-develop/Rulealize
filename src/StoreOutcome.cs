// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Rulealize.Abstraction.Store;
using Rulealize.Abstraction.Value;

namespace Rulealize
{
    /// <summary>One thing that can happen when an input is applied to the documents a store keeps, and where it leads.</summary>
    /// <remarks>
    /// <para>
    /// What <see cref="Outcome"/> is for a state document, without the state: the documents are
    /// the store's, so this says what <see cref="CommittedTransition"/> would say once the
    /// outcome is kept — whether the rule set is final, and which instance the input was about —
    /// and nothing is written until the host hands it to
    /// <see cref="RuleContext.ApplyAsync(string, StoreOutcome, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// An input that draws nothing still has one of these, with a probability of one and no
    /// draws in it, so a host applies every input through its outcomes whether the rule set has
    /// chance in it or not.
    /// </para>
    /// </remarks>
    public sealed class StoreOutcome
    {
        internal StoreOutcome(
            string ruleSet,
            string input,
            ImmutableArray<RuleValue> script,
            ImmutableArray<string> draws,
            double probability,
            bool isTerminal,
            string? result,
            StateAddress at,
            TerminalStatus atStatus)
        {
            RuleSet = ruleSet;
            Input = input;
            Script = script;
            Draws = draws;
            Probability = probability;
            IsTerminal = isTerminal;
            Result = result;
            At = at;
            AtStatus = atStatus;
        }

        /// <summary>Gets how likely this outcome is, among the ones this input can have.</summary>
        /// <remarks>Between zero and one; the probabilities of a complete set sum to one but for what floating point loses.</remarks>
        public double Probability { get; }

        /// <summary>Gets what is drawn, in order, rendered as text.</summary>
        public ImmutableArray<string> Draws { get; }

        /// <summary>Gets whether the rule set's own document would be final, by its <c>terminal</c>.</summary>
        public bool IsTerminal { get; }

        /// <summary>Gets the rule set's own outcome there, when it would be final and its <c>terminal</c> names one.</summary>
        public string? Result { get; }

        /// <summary>Gets the document the input is about: the instance it creates or is at, or the rule set's own.</summary>
        /// <remarks>
        /// An instance the outcome would create is under its temporary key, <c>~1</c>, because
        /// no store has assigned it one; <see cref="CommittedTransition.At"/> gives the key it was
        /// kept under.
        /// </remarks>
        public StateAddress At { get; }

        /// <summary>Gets whether the document at <see cref="At"/> would be final, by its own rule set's <c>terminal</c>.</summary>
        public TerminalStatus AtStatus { get; }

        /// <summary>Gets the rule set the outcome is of, as <c>id@version</c>.</summary>
        internal string RuleSet { get; }

        /// <summary>Gets the input the outcome is of.</summary>
        internal string Input { get; }

        /// <summary>Gets what is drawn, in order, as values.</summary>
        internal ImmutableArray<RuleValue> Script { get; }

        /// <summary>Writes what is drawn as an outcome document, to record or show beside the input.</summary>
        /// <returns>A <c>rulealize/outcome/v1</c> document, stamped with the rule set the outcome is of.</returns>
        /// <remarks>
        /// The document <see cref="Outcome.ToOutcomeDocument(string)"/> writes, where a number is a
        /// number. What replays a kept transition is its commit document, which records the same
        /// draws beside the keys the store assigned.
        /// </remarks>
        public string ToOutcomeDocument()
        {
            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("$schema", Internal.Document.OutcomeDocument.SchemaId);
                writer.WriteString("ruleSet", RuleSet);
                writer.WriteString("input", Input);
                writer.WritePropertyName("draws");
                Internal.Document.OutcomeDocument.WriteValues(writer, Script);
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
    }

    /// <summary>Everything that can happen to one input applied through a store, and how much of it the search saw.</summary>
    /// <remarks>
    /// Most likely first, and <see cref="Coverage"/> says how much of the distribution survived a
    /// limit, as <see cref="OutcomeSet"/> does.
    /// </remarks>
    /// <param name="outcomes">The outcomes, most likely first.</param>
    /// <param name="coverage">The total probability of the outcomes listed.</param>
    /// <param name="evaluated">How many times the effects were run to find them.</param>
    /// <param name="truncated">Whether the limit stopped the search.</param>
    public sealed class StoreOutcomeSet(
        ImmutableArray<StoreOutcome> outcomes,
        double coverage,
        int evaluated,
        bool truncated) : IReadOnlyList<StoreOutcome>
    {
        /// <summary>Gets the number of outcomes listed.</summary>
        public int Count => outcomes.Length;

        /// <summary>Gets the total probability of the outcomes listed: one unless the limit cut the search short.</summary>
        public double Coverage => coverage;

        /// <summary>Gets whether the limit stopped the search before every outcome was found.</summary>
        public bool Truncated => truncated;

        /// <summary>Gets how many times the input's effects were run to find the outcomes.</summary>
        public int Evaluated => evaluated;

        /// <summary>Gets an outcome by position, most likely first.</summary>
        /// <param name="index">The position.</param>
        public StoreOutcome this[int index] => outcomes[index];

        /// <inheritdoc />
        public IEnumerator<StoreOutcome> GetEnumerator() => ((IEnumerable<StoreOutcome>)outcomes).GetEnumerator();

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
