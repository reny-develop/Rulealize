// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text;
using System.Text.Json;

namespace Rulealize
{
    /// <summary>Where a transition arrived.</summary>
    /// <remarks>
    /// <see cref="State"/> is a complete state document and is what gets handed back in for
    /// the next move. <see cref="ToJson"/> is the shorter shape a caller usually wants to
    /// send onward: the new data, whether the game is over, and what the outcome was.
    /// </remarks>
    public sealed class TransitionResult
    {
        private readonly string _data;
        private readonly string _ruleSet;

        internal TransitionResult(string ruleSet, string data, bool isTerminal, string? result)
        {
            _ruleSet = ruleSet;
            _data = data;
            IsTerminal = isTerminal;
            Result = result;
        }

        /// <summary>Gets a value indicating whether the rule set considers the new state final.</summary>
        public bool IsTerminal { get; }

        /// <summary>
        /// Gets the outcome the rule set reports, or <see langword="null"/> when the state is
        /// not terminal or the rule set declares no result.
        /// </summary>
        /// <remarks>
        /// The canonical text of whatever <c>terminal.result</c> evaluated to — for a board
        /// game, typically the winner.
        /// </remarks>
        public string? Result { get; }

        /// <summary>Gets the state the transition arrived at, as a state document.</summary>
        public string State => BuildStateDocument();

        /// <summary>Writes the result as the document a caller passes on.</summary>
        /// <returns>An object with <c>data</c>, <c>terminal</c>, and <c>result</c> when there is one.</returns>
        public string ToJson()
        {
            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("data");
                using (JsonDocument data = JsonDocument.Parse(_data))
                {
                    data.RootElement.WriteTo(writer);
                }

                writer.WriteBoolean("terminal", IsTerminal);
                if (Result is not null)
                {
                    writer.WriteString("result", Result);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        /// <inheritdoc />
        public override string ToString() =>
            IsTerminal ? $"terminal{(Result is null ? string.Empty : $" ({Result})")}" : "ongoing";

        private string BuildStateDocument()
        {
            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("$schema", Internal.Documents.StateDocument.SchemaId);
                writer.WriteString("ruleSet", _ruleSet);
                writer.WritePropertyName("data");
                using (JsonDocument data = JsonDocument.Parse(_data))
                {
                    data.RootElement.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}
