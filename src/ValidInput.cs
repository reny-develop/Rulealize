// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Rulealize.Abstraction.Value;

namespace Rulealize
{
    /// <summary>One move that is available in a state.</summary>
    /// <remarks>
    /// <para>
    /// Handing this straight back to
    /// <see cref="RuleContext.ApplyToState(string, string, System.Threading.CancellationToken)"/>
    /// has to produce the move it describes, and that requires each argument to survive the
    /// trip out through JSON and back. So an argument is written in its own JSON form: a
    /// number stays a number, a boolean stays a boolean.
    /// </para>
    /// <para>
    /// Only a value with no JSON form of its own — a coordinate, a direction, anything
    /// opaque — is written as text. That is what the canonical text form in the value model
    /// is for. Coming back it is matched against the domain by that same text and the
    /// domain's value is what gets bound, so a rule reading the argument sees the coordinate
    /// and not its spelling, whichever way the move arrived.
    /// </para>
    /// <para>
    /// <see cref="Arguments"/> is the readable view, everything rendered as text. What
    /// travels in a document is <see cref="ToInputDocument"/>.
    /// </para>
    /// </remarks>
    public sealed class ValidInput
    {
        private readonly ImmutableArray<KeyValuePair<string, RuleValue>> _values;

        internal ValidInput(
            string input,
            ImmutableArray<KeyValuePair<string, RuleValue>> values,
            ImmutableDictionary<string, string> arguments,
            string? actor)
        {
            Input = input;
            _values = values;
            Arguments = arguments;
            Actor = actor;
        }

        /// <summary>Gets the name of the input.</summary>
        public string Input { get; }

        /// <summary>Gets the arguments rendered as text, one per declared parameter.</summary>
        /// <remarks>
        /// The readable view. A number appears here as its digits; what goes into an input
        /// document is the number itself. See <see cref="ToInputDocument"/>.
        /// </remarks>
        public ImmutableDictionary<string, string> Arguments { get; }

        /// <summary>Gets whose move this is, or <see langword="null"/> when the rule set does not say.</summary>
        public string? Actor { get; }

        /// <summary>Writes this as an input document, ready to apply.</summary>
        /// <returns>A <c>rulealize/input/v1</c> document.</returns>
        /// <param name="ruleSet">The rule set identity to stamp on it, as <c>id@version</c>.</param>
        public string ToInputDocument(string ruleSet)
        {
            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("$schema", Internal.Document.InputDocument.SchemaId);
                writer.WriteString("ruleSet", ruleSet);
                writer.WriteString("input", Input);
                writer.WritePropertyName("args");
                WriteArguments(writer);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        /// <inheritdoc />
        public override string ToString() =>
            Arguments.IsEmpty
                ? Input
                : $"{Input}({string.Join(", ", Arguments.Select(static pair => $"{pair.Key}: {pair.Value}"))})";

        internal void WriteTo(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("input", Input);
            writer.WritePropertyName("args");
            WriteArguments(writer);
            if (Actor is not null)
            {
                writer.WriteString("actor", Actor);
            }

            writer.WriteEndObject();
        }

        private void WriteArguments(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            foreach ((string name, RuleValue value) in _values)
            {
                writer.WritePropertyName(name);
                Write(writer, value);
            }

            writer.WriteEndObject();
        }

        private static void Write(Utf8JsonWriter writer, RuleValue value)
        {
            switch (value)
            {
                case NullValue:
                    writer.WriteNullValue();
                    return;

                case BooleanValue boolean:
                    writer.WriteBooleanValue(boolean.Value);
                    return;

                case NumberValue number:
                    writer.WriteNumberValue(number.Value);
                    return;

                default:
                    // Text, and everything opaque, which the value model requires to have a
                    // text form precisely so that it can be written here.
                    writer.WriteStringValue(value.GetCanonicalText());
                    return;
            }
        }
    }

    /// <summary>Everything available in a state, and whether the search saw all of it.</summary>
    /// <remarks>
    /// <para>
    /// Candidates are the product of the parameter domains, sifted by each input's guard.
    /// Reversi produces sixty-five of them — sixty-four squares and a pass — which is
    /// nothing; a move written as <c>from</c>, <c>to</c> and a promotion flag on a shogi
    /// board produces thirteen thousand, which is not.
    /// </para>
    /// <para>
    /// The limit bounds how many guards get evaluated, and <see cref="Truncated"/> says
    /// whether it stopped the search early. A truncated result is a subset of the legal
    /// moves, never a wrong one.
    /// </para>
    /// </remarks>
    public sealed class ValidInputSet(ImmutableArray<ValidInput> inputs, int evaluated, bool truncated)
        : IReadOnlyList<ValidInput>
    {
        /// <summary>Gets the number of available moves found.</summary>
        public int Count => inputs.Length;

        /// <summary>Gets a value indicating whether the search stopped at the limit.</summary>
        public bool Truncated => truncated;

        /// <summary>Gets how many candidates had their guard evaluated.</summary>
        public int Evaluated => evaluated;

        /// <inheritdoc />
        public ValidInput this[int index] => inputs[index];

        /// <inheritdoc />
        public IEnumerator<ValidInput> GetEnumerator() => ((IEnumerable<ValidInput>)inputs).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Writes the set as an array of input descriptions.</summary>
        /// <returns>The JSON array.</returns>
        public string ToJson()
        {
            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartArray();
                foreach (ValidInput input in inputs)
                {
                    input.WriteTo(writer);
                }

                writer.WriteEndArray();
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}
