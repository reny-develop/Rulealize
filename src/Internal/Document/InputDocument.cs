// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction.Value;
using Rulealize.Internal.RuleSet;

namespace Rulealize.Internal.Document
{
    /// <summary>An input document, read.</summary>
    internal sealed record InputRequest(string Input, IReadOnlyDictionary<string, JsonElement> Arguments);

    /// <summary>Reads the <c>rulealize/input/v1</c> document.</summary>
    /// <remarks>
    /// <para>
    /// Arguments arrive as JSON scalars, so a coordinate comes in as the text <c>"d3"</c>
    /// rather than as the opaque value a parameter's domain produced. Nothing here converts
    /// it — this class only reads what the document says. Matching that against the domain,
    /// and binding what it matched, is <c>RuleContext</c>'s part, and it is what closes the
    /// round trip that <c>GetValidInputs</c> opens.
    /// </para>
    /// <para>
    /// The JSON is kept rather than converted, because which conversion is right depends on
    /// the parameter it turns out to belong to. One with a domain gets the scalar reading
    /// below and is then matched against the domain; one left <c>open</c> is read by its own
    /// schema node, which owns the JSON its values are written as and may therefore accept a
    /// shape — an array, an object — that no domain argument could be.
    /// </para>
    /// </remarks>
    internal static class InputDocument
    {
        public const string SchemaId = "rulealize/input/v1";

        public static InputRequest Read(CompiledRuleSet ruleSet, JsonElement document)
        {
            if (document.ValueKind != JsonValueKind.Object)
            {
                throw new RuleDocumentException("An input document must be a JSON object.");
            }

            DocumentFrame.OnlyTheseKeys(document, "an input", "$schema", "ruleSet", "input", "args");

            if (document.TryGetProperty("ruleSet", out JsonElement declared)
                && declared.ValueKind == JsonValueKind.String
                && !string.Equals(declared.GetString(), ruleSet.Qualified, StringComparison.Ordinal))
            {
                throw new RuleDocumentException(
                    $"This input was written for '{declared.GetString()}', but the context is '{ruleSet.Qualified}'.");
            }

            if (!document.TryGetProperty("input", out JsonElement name) || name.ValueKind != JsonValueKind.String)
            {
                throw new RuleDocumentException("An input document needs an 'input' naming the input to apply.");
            }

            Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal);
            if (document.TryGetProperty("args", out JsonElement args))
            {
                if (args.ValueKind != JsonValueKind.Object)
                {
                    throw new RuleDocumentException("The 'args' of an input document must be an object.");
                }

                foreach (JsonProperty argument in args.EnumerateObject())
                {
                    arguments[argument.Name] = argument.Value;
                }
            }

            return new InputRequest(name.GetString()!, arguments);
        }

        /// <summary>Reads an argument of a parameter that has a domain.</summary>
        /// <param name="element">The JSON the document gave for it.</param>
        /// <param name="path">The argument name, for a message.</param>
        /// <returns>The value, to be matched against the domain.</returns>
        /// <exception cref="RuleDocumentException">The argument is an array.</exception>
        /// <remarks>
        /// Scalars and records, and an array is refused: the value model has no literal for a
        /// sequence, so an array against a domain could only be a mistake. An open parameter
        /// does not come through here.
        /// </remarks>
        public static RuleValue ReadValue(JsonElement element, string path) => element.ValueKind switch
        {
            JsonValueKind.String => RuleValue.Text(element.GetString()!),
            JsonValueKind.Number => RuleValue.Number(element.GetDecimal()),
            JsonValueKind.True => RuleValue.True,
            JsonValueKind.False => RuleValue.False,
            JsonValueKind.Null => RuleValue.Null,
            JsonValueKind.Object => ReadRecord(element, path),
            _ => throw new RuleDocumentException(
                $"The argument '{path}' is an array, and there is no sequence an argument can be written as.")
        };

        private static RuleValue ReadRecord(JsonElement element, string path)
        {
            Dictionary<string, RuleValue> fields = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                fields[property.Name] = ReadValue(property.Value, $"{path}.{property.Name}");
            }

            return RuleValue.Record(fields);
        }
    }
}
