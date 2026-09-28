// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text;
using System.Text.Json;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Value;

namespace Rulealize.Internal.Document
{
    /// <summary>Writes what a projection worked out, as JSON.</summary>
    /// <remarks>
    /// <para>
    /// Not one of the documents that travel. A state, an input and an outcome each go out and
    /// come back, and every rule about how they are written is a rule about surviving that
    /// trip. A projection only ever goes out, so what it is written as is decided by what
    /// reads it rather than by what has to recognise it again: the plain JSON of the value
    /// model, and no envelope naming the rule set it came from.
    /// </para>
    /// <para>
    /// An opaque value is written as its canonical text, the same concession an input argument
    /// gets and for the same reason: a value the value model has no JSON for has to be written
    /// as something. One with no text form is a fault rather than a silent omission, because a
    /// caller reading a field that quietly vanished has no way to tell it apart from a field
    /// the projection meant to leave out.
    /// </para>
    /// </remarks>
    internal static class ProjectionDocument
    {
        public static string Write(string name, RuleValue value)
        {
            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
            {
                Write(writer, value, $"projections.{name}");
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        private static void Write(Utf8JsonWriter writer, RuleValue value, string origin)
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

                case TextValue text:
                    writer.WriteStringValue(text.Value);
                    return;

                case SequenceValue sequence:
                    writer.WriteStartArray();
                    foreach (RuleValue item in sequence)
                    {
                        Write(writer, item, origin);
                    }

                    writer.WriteEndArray();
                    return;

                case RecordValue record:
                    writer.WriteStartObject();
                    foreach ((string key, RuleValue field) in record.Fields)
                    {
                        writer.WritePropertyName(key);
                        Write(writer, field, origin);
                    }

                    writer.WriteEndObject();
                    return;

                default:
                    writer.WriteStringValue(
                        value.GetCanonicalText()
                        ?? throw new RuleEvaluationException(
                            origin,
                            $"{RuleValue.Describe(value)} has no text form, so it cannot be written out."));
                    return;
            }
        }
    }
}
