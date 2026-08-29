// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Node;
using Rulealize.Abstraction.Value;
using Rulealize.Internal.Document;
using Rulealize.Internal.RuleSet;

namespace Rulealize.Internal.Building
{
    /// <summary>The schema of the field a held rule set's state occupies.</summary>
    /// <remarks>
    /// <para>
    /// A composite's case is still one state document. What makes that affordable is that a
    /// held rule set's state is an ordinary field of it, and this is the schema of that
    /// field: a record whose keys are the component's own fields, each checked, read and
    /// written by the schema the component declared for it.
    /// </para>
    /// <para>
    /// The core builds this rather than a plugin, and it is not written in
    /// <c>state.schema</c> — <c>uses</c> declares it. That is what keeps composition from
    /// costing the core a vocabulary: reading into a component's state is
    /// <c>rec.at</c> over a record, which is a plugin's business and already exists.
    /// </para>
    /// <para>
    /// The JSON form carries the component's <c>ruleSet</c> beside its <c>data</c>, and it
    /// is checked on the way in. A composite whose component was revised across a major
    /// version has stored states that no longer mean what they said, and the version each
    /// one was written against is the only thing that can say so — the composite's own
    /// identity cannot, because a component may be revised without the composite being
    /// touched at all.
    /// </para>
    /// </remarks>
    internal sealed class HeldStateSchema(CompiledRuleSet held) : SchemaNode
    {
        /// <summary>Gets the rule set whose state this field holds.</summary>
        public CompiledRuleSet Held => held;

        /// <inheritdoc />
        public override bool IsNullable => false;

        /// <summary>Reads a component's fields out of the record this field holds.</summary>
        /// <param name="value">The field value.</param>
        /// <returns>The component's field values, in its schema order.</returns>
        /// <remarks>
        /// Total, because the value has already been through <see cref="Validate"/> — a field
        /// the record does not carry reads as null and fails the component's own schema at
        /// the next commit rather than here.
        /// </remarks>
        public ImmutableArray<RuleValue> Unpack(RuleValue value)
        {
            RecordValue? record = value as RecordValue;
            return
            [
                .. held.Schema.Fields.Select(field =>
                    record is null ? RuleValue.Null : record[field.Text])
            ];
        }

        /// <summary>Puts a component's fields back into the record this field holds.</summary>
        /// <param name="fields">The component's field values, in its schema order.</param>
        /// <returns>The field value.</returns>
        public RuleValue Pack(ImmutableArray<RuleValue> fields)
        {
            Dictionary<string, RuleValue> record = new(StringComparer.Ordinal);
            for (int i = 0; i < held.Schema.Fields.Length && i < fields.Length; i++)
            {
                record[held.Schema.Fields[i].Text] = fields[i];
            }

            return RuleValue.Record(record);
        }

        /// <inheritdoc />
        public override void Validate(RuleValue value, ISchemaValidationSink sink)
        {
            ArgumentNullException.ThrowIfNull(sink);

            if (value is not RecordValue record)
            {
                sink.Violation($"Expected the state of '{held.Qualified}'.");
                return;
            }

            foreach (StatePath field in held.Schema.Fields)
            {
                field.Schema.Validate(record[field.Text], new Nested(sink, field.Text));
            }

            foreach (string key in record.Fields.Keys)
            {
                if (!held.Schema.TryResolve(key, out _))
                {
                    sink.Violation(key, $"is not a field of '{held.Qualified}'.");
                }
            }
        }

        /// <inheritdoc />
        public override RuleValue Normalize(RuleValue value)
        {
            if (value is not RecordValue record)
            {
                return value;
            }

            Dictionary<string, RuleValue> normalized = new(StringComparer.Ordinal);
            foreach (StatePath field in held.Schema.Fields)
            {
                normalized[field.Text] = field.Schema.Normalize(record[field.Text]);
            }

            return RuleValue.Record(normalized);
        }

        /// <inheritdoc />
        public override RuleValue ReadJson(JsonElement element, ISchemaValidationSink sink)
        {
            ArgumentNullException.ThrowIfNull(sink);

            if (element.ValueKind != JsonValueKind.Object)
            {
                sink.Violation($"Expected the state of '{held.Qualified}'.");
                return RuleValue.Null;
            }

            if (element.TryGetProperty("ruleSet", out JsonElement declared)
                && declared.ValueKind == JsonValueKind.String
                && declared.GetString() is string claimed
                && !StateDocument.IsCompatible(claimed, held))
            {
                sink.Violation($"was written for '{claimed}', and this composite holds '{held.Qualified}'.");
                return RuleValue.Null;
            }

            if (!element.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
            {
                sink.Violation("needs a 'data' object.");
                return RuleValue.Null;
            }

            Dictionary<string, RuleValue> record = new(StringComparer.Ordinal);
            foreach (StatePath field in held.Schema.Fields)
            {
                if (data.TryGetProperty(field.Text, out JsonElement value))
                {
                    record[field.Text] = field.Schema.ReadJson(value, new Nested(sink, field.Text));
                }
                else
                {
                    sink.Violation(field.Text, "is missing.");
                    record[field.Text] = RuleValue.Null;
                }
            }

            foreach (JsonProperty property in data.EnumerateObject())
            {
                if (!held.Schema.TryResolve(property.Name, out _))
                {
                    sink.Violation(property.Name, $"is not a field of '{held.Qualified}'.");
                }
            }

            return RuleValue.Record(record);
        }

        /// <inheritdoc />
        public override void WriteJson(Utf8JsonWriter writer, RuleValue value)
        {
            ArgumentNullException.ThrowIfNull(writer);

            RecordValue? record = value as RecordValue;

            writer.WriteStartObject();
            writer.WriteString("ruleSet", held.Qualified);
            writer.WritePropertyName("data");
            writer.WriteStartObject();
            foreach (StatePath field in held.Schema.Fields)
            {
                writer.WritePropertyName(field.Text);
                field.Schema.WriteJson(writer, record is null ? RuleValue.Null : record[field.Text]);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        /// <summary>A sink that prefixes what it is given with the component field it came from.</summary>
        private sealed class Nested(ISchemaValidationSink outer, string field) : ISchemaValidationSink
        {
            public bool HasViolations => outer.HasViolations;

            public void Violation(string message) => outer.Violation(field, message);

            public void Violation(string relativePath, string message) =>
                outer.Violation($"{field}.{relativePath}", message);
        }
    }
}
