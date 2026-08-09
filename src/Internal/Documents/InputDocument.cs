// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction.Values;
using Rulealize.Internal.RuleSet;

namespace Rulealize.Internal.Documents
{
    /// <summary>An input document, read.</summary>
    internal sealed record InputRequest(string Input, IReadOnlyDictionary<string, RuleValue> Arguments);

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
    /// Arrays are refused. The value model has no literal for a sequence, so an array in
    /// argument position could only be a mistake.
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

            Dictionary<string, RuleValue> arguments = new(StringComparer.Ordinal);
            if (document.TryGetProperty("args", out JsonElement args))
            {
                if (args.ValueKind != JsonValueKind.Object)
                {
                    throw new RuleDocumentException("The 'args' of an input document must be an object.");
                }

                foreach (JsonProperty argument in args.EnumerateObject())
                {
                    arguments[argument.Name] = ReadValue(argument.Value, argument.Name);
                }
            }

            return new InputRequest(name.GetString()!, arguments);
        }

        private static RuleValue ReadValue(JsonElement element, string path) => element.ValueKind switch
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
