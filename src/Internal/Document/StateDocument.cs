// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Value;
using Rulealize.Internal.Building;
using Rulealize.Internal.RuleSet;

namespace Rulealize.Internal.Document
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

            DocumentFrame.OnlyTheseKeys(document, "a state", "$schema", "ruleSet", "data");

            if (document.TryGetProperty("ruleSet", out JsonElement declared)
                && declared.ValueKind == JsonValueKind.String
                && declared.GetString() is string claimed
                && !IsCompatible(claimed, ruleSet))
            {
                throw new RuleDocumentException(
                    $"This state was written for '{claimed}', but the context is '{ruleSet.Qualified}'.");
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

        /// <summary>Whether the identity a state document claims is one this rule set can read.</summary>
        /// <param name="claimed">The <c>ruleSet</c> the document names, as <c>id@version</c>.</param>
        /// <param name="ruleSet">The rule set the context was built from.</param>
        /// <returns><see langword="true"/> when the state may be read.</returns>
        /// <remarks>
        /// <para>
        /// The identifier has to match exactly and the major version has to match. Nothing
        /// else about the version is consulted, which is the same reading a rule set's
        /// <c>requires</c> gives a plugin version with <c>^</c>, and it is the same reason: a
        /// major version is where this project says meaning changed, so it is the only part
        /// of a version that can decide whether a document written earlier still says what it
        /// said.
        /// </para>
        /// <para>
        /// Nothing more is asserted here because nothing more can be. A minor revision is
        /// free to add a state field, and a document written before it will not carry one —
        /// but that is a missing field, and the schema check below already names it. Gating
        /// identity here and shape there keeps each failure readable, instead of collapsing a
        /// shape problem into a version mismatch that says nothing about which field is
        /// wrong.
        /// </para>
        /// <para>
        /// A document naming no rule set at all never reaches this method and is checked
        /// against the schema like any other. Declining to claim an identity is not the same
        /// as claiming the wrong one, and a state written by hand has no reason to be forced
        /// into one.
        /// </para>
        /// </remarks>
        private static bool IsCompatible(string claimed, CompiledRuleSet ruleSet)
        {
            if (string.Equals(claimed, ruleSet.Qualified, StringComparison.Ordinal))
            {
                return true;
            }

            int separator = claimed.LastIndexOf('@');
            if (separator < 0 || !string.Equals(claimed[..separator], ruleSet.Id, StringComparison.Ordinal))
            {
                return false;
            }

            // An unparseable version on either side leaves only the exact match above. A rule
            // set is free to version itself in a scheme this does not understand; it just does
            // not get to be compatible with anything but itself.
            return Version.TryParse(claimed[(separator + 1)..], out Version? written)
                && Version.TryParse(ruleSet.Version, out Version? context)
                && written.Major == context.Major;
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
