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
    /// <see cref="Arguments"/> is the readable view, everything rendered as text and in the
    /// order the parameters were declared, so that <see cref="ToString"/> writes the same move
    /// the same way in every process. What travels in a document is
    /// <see cref="ToInputDocument"/>.
    /// </para>
    /// <para>
    /// A move whose input leaves a parameter <c>open</c> comes back <b>incomplete</b>: the
    /// value is somebody's to supply, so <see cref="Open"/> names what is missing and
    /// <see cref="IsComplete"/> is false. The round trip above is a promise about a complete
    /// move, which is why <see cref="ToInputDocument"/> refuses an incomplete one rather than
    /// writing a document that would mean something else. A rule set with no open parameter
    /// has no incomplete moves, so nothing that was true before has stopped being true.
    /// </para>
    /// </remarks>
    public sealed class ValidInput
    {
        private readonly ImmutableArray<KeyValuePair<string, RuleValue>> _values;
        private readonly ImmutableArray<string> _parameters;

        internal ValidInput(
            string input,
            ImmutableArray<KeyValuePair<string, RuleValue>> values,
            ArgumentList arguments,
            OpenParameterList open,
            ImmutableArray<string> parameters,
            string? actor)
        {
            Input = input;
            _values = values;
            _parameters = parameters;
            Arguments = arguments;
            Open = open;
            Actor = actor;
        }

        /// <summary>Gets the name of the input.</summary>
        public string Input { get; }

        /// <summary>Gets the arguments rendered as text, one per declared parameter, in that order.</summary>
        /// <remarks>
        /// The readable view. A number appears here as its digits; what goes into an input
        /// document is the number itself. See <see cref="ToInputDocument"/>.
        /// </remarks>
        public ArgumentList Arguments { get; }

        /// <summary>Gets the parameters still waiting for a value, in declared order.</summary>
        public OpenParameterList Open { get; }

        /// <summary>Gets a value indicating whether every parameter of this move has a value.</summary>
        /// <remarks>
        /// Only a move of an input that leaves a parameter <c>open</c> is ever incomplete. A
        /// caller that walks moves and applies them — a solver, a perft count — can ask this
        /// once of the set it was handed rather than of each move.
        /// </remarks>
        public bool IsComplete => Open.IsEmpty;

        /// <summary>Gets whose move this is, or <see langword="null"/> when the rule set does not say.</summary>
        public string? Actor { get; }

        /// <summary>Writes this as an input document, ready to apply.</summary>
        /// <returns>A <c>rulealize/input/v1</c> document.</returns>
        /// <param name="ruleSet">The rule set identity to stamp on it, as <c>id@version</c>.</param>
        /// <exception cref="InvalidOperationException">
        /// This move is incomplete: an open parameter has no value yet. Refused rather than
        /// written with the argument left out, because that document would name a different
        /// move — and a caller walking moves is better stopped here than handed one.
        /// </exception>
        public string ToInputDocument(string ruleSet)
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException(
                    $"'{Input}' leaves {Listed(Open)} open, and a move is not a document until every "
                    + "argument has a value. The value comes from whoever is being asked for it.");
            }

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
        /// <remarks>
        /// Parameters in the order the rule set declared them, whether each has a value or is
        /// still open, so that one move reads the same way everywhere it is written down.
        /// </remarks>
        public override string ToString()
        {
            if (_parameters.IsEmpty)
            {
                return Input;
            }

            IEnumerable<string> rendered = _parameters.Select(name =>
                Arguments.TryGetValue(name, out string? argument)
                    ? $"{name}: {argument}"
                    : Open[name].ToString());

            return $"{Input}({string.Join(", ", rendered)})";
        }

        private static string Listed(OpenParameterList open) =>
            open.Count is 1
                ? $"'{open[0].Name}'"
                : string.Join(", ", open.Take(open.Count - 1).Select(static p => $"'{p.Name}'"))
                  + $" and '{open[^1].Name}'";

        internal void WriteTo(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("input", Input);
            writer.WritePropertyName("args");
            WriteArguments(writer);

            if (!IsComplete)
            {
                writer.WritePropertyName("open");
                writer.WriteStartObject();
                foreach (OpenParameter parameter in Open)
                {
                    writer.WritePropertyName(parameter.Name);
                    writer.WriteStartObject();
                    if (parameter.Op is string op)
                    {
                        writer.WriteString("op", op);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

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

        /// <summary>Gets a value indicating whether any move here is waiting for an argument.</summary>
        /// <remarks>
        /// False for every rule set that leaves no parameter <c>open</c>, which is what lets a
        /// traversal — a solver, a perft count — establish in one question that every move it
        /// is about to walk can be applied as it stands.
        /// </remarks>
        public bool HasOpenParameters => inputs.Any(static input => !input.IsComplete);

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
