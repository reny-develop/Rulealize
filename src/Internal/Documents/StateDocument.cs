// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Values;
using Rulealize.Internal.Building;
using Rulealize.Internal.RuleSet;

namespace Rulealize.Internal.Documents
{
    /// <summary>Reads and writes the <c>rulealize/state/v1</c> document.</summary>
    /// <remarks>
    /// <para>
    /// The frame — <c>$schema</c>, <c>ruleSet</c>, <c>data</c> — is all the core fixes. How
    /// each field inside <c>data</c> becomes JSON is decided by the schema node that
    /// declared it, so a board is written as a sparse coordinate map because a grid plugin
    /// says so, and switching it to a dense array would not touch this file.
    /// </para>
    /// </remarks>
    internal static class StateDocument
    {
        public const string SchemaId = "rulealize/state/v1";

        /// <summary>Reads a state document against a rule set's schema.</summary>
        /// <param name="ruleSet">The rule set.</param>
        /// <param name="document">The parsed document.</param>
        /// <returns>The field values, in schema order.</returns>
        /// <exception cref="RuleDocumentException">The document is not one this rule set accepts.</exception>
        public static ImmutableArray<RuleValue> Read(CompiledRuleSet ruleSet, JsonElement document)
        {
            if (document.ValueKind != JsonValueKind.Object)
            {
                throw new RuleDocumentException("A state document must be a JSON object.");
            }

            if (document.TryGetProperty("ruleSet", out JsonElement declared)
                && declared.ValueKind == JsonValueKind.String
                && !string.Equals(declared.GetString(), ruleSet.Qualified, StringComparison.Ordinal))
            {
                throw new RuleDocumentException(
                    $"This state was written for '{declared.GetString()}', but the context is '{ruleSet.Qualified}'.");
            }

            if (!document.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
            {
                throw new RuleDocumentException("A state document needs a 'data' object.");
            }

            SchemaViolations violations = new();
            ImmutableArray<RuleValue>.Builder values =
                ImmutableArray.CreateBuilder<RuleValue>(ruleSet.Schema.FieldCount);

            foreach (StatePath field in ruleSet.Schema.Fields)
            {
                if (data.TryGetProperty(field.Text, out JsonElement value))
                {
                    values.Add(field.Schema.ReadJson(value, violations.For(field.Text)));
                }
                else
                {
                    violations.Add($"{field.Text}: is missing.");
                    values.Add(RuleValue.Null);
                }
            }

            foreach (JsonProperty property in data.EnumerateObject())
            {
                if (!ruleSet.Schema.TryResolve(property.Name, out _))
                {
                    violations.Add($"{property.Name}: is not a field of this rule set's state.");
                }
            }

            if (violations.Any)
            {
                throw new RuleDocumentException("The state does not satisfy state.schema.", violations.Messages);
            }

            return values.MoveToImmutable();
        }

        /// <summary>Writes a state document.</summary>
        /// <param name="ruleSet">The rule set.</param>
        /// <param name="fields">The field values, in schema order.</param>
        /// <returns>The document, ready to be handed back in.</returns>
        public static string Write(CompiledRuleSet ruleSet, ImmutableArray<RuleValue> fields)
        {
            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("$schema", SchemaId);
                writer.WriteString("ruleSet", ruleSet.Qualified);
                writer.WritePropertyName("data");
                WriteData(writer, ruleSet, fields);
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }

        /// <summary>Writes just the <c>data</c> object.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="ruleSet">The rule set.</param>
        /// <param name="fields">The field values, in schema order.</param>
        public static void WriteData(Utf8JsonWriter writer, CompiledRuleSet ruleSet, ImmutableArray<RuleValue> fields)
        {
            writer.WriteStartObject();
            foreach (StatePath field in ruleSet.Schema.Fields)
            {
                writer.WritePropertyName(field.Text);
                field.Schema.WriteJson(writer, fields[field.FieldIndex]);
            }

            writer.WriteEndObject();
        }
    }
}
