// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Internal.RuleSet;

namespace Rulealize.Internal.Document
{
    /// <summary>A commit document, read.</summary>
    /// <param name="Input">The input the commit belongs to.</param>
    /// <param name="Created">The key the store assigned each temporary key, by temporary key.</param>
    internal sealed record CommitRequest(string Input, IReadOnlyDictionary<string, string> Created);

    /// <summary>Reads and writes the <c>rulealize/commit/v1</c> document.</summary>
    /// <remarks>
    /// <para>
    /// What the store did about a transition, recorded beside the input as an outcome is beside
    /// a move with chance in it. So far that is only the keys it assigned: a new instance is
    /// written under a temporary key, and the store's key is the one thing about the transition
    /// the rules could not have known.
    /// </para>
    /// <para>
    /// It names its input for the reason an outcome does: a commit paired with the wrong input
    /// would replay into an instance nobody created.
    /// </para>
    /// </remarks>
    internal static class CommitDocument
    {
        public const string SchemaId = "rulealize/commit/v1";

        public static CommitRequest Read(CompiledRuleSet ruleSet, JsonElement document)
        {
            if (document.ValueKind != JsonValueKind.Object)
            {
                throw new RuleDocumentException("A commit document must be a JSON object.");
            }

            DocumentFrame.OnlyTheseKeys(document, "a commit", "$schema", "ruleSet", "input", "created");

            if (document.TryGetProperty("ruleSet", out JsonElement declared)
                && declared.ValueKind == JsonValueKind.String
                && !string.Equals(declared.GetString(), ruleSet.Qualified, StringComparison.Ordinal))
            {
                throw new RuleDocumentException(
                    $"This commit was written for '{declared.GetString()}', but the context is '{ruleSet.Qualified}'.");
            }

            if (!document.TryGetProperty("input", out JsonElement name) || name.ValueKind != JsonValueKind.String)
            {
                throw new RuleDocumentException("A commit document needs an 'input' naming the input it commits.");
            }

            Dictionary<string, string> created = new(StringComparer.Ordinal);
            if (document.TryGetProperty("created", out JsonElement assigned))
            {
                if (assigned.ValueKind != JsonValueKind.Object)
                {
                    throw new RuleDocumentException(
                        "The 'created' of a commit document must be an object giving each temporary key its key.");
                }

                foreach (JsonProperty key in assigned.EnumerateObject())
                {
                    if (key.Value.ValueKind != JsonValueKind.String || key.Value.GetString()!.Length == 0)
                    {
                        throw new RuleDocumentException($"'created.{key.Name}' must be the key the store assigned, as text.");
                    }

                    created[key.Name] = key.Value.GetString()!;
                }
            }

            return new CommitRequest(name.GetString()!, created);
        }

        /// <summary>Writes the commit of one transition: its input, and the key each new instance was assigned.</summary>
        public static string Write(CompiledRuleSet ruleSet, string input, IReadOnlyDictionary<string, string> created)
        {
            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("$schema", SchemaId);
                writer.WriteString("ruleSet", ruleSet.Qualified);
                writer.WriteString("input", input);
                writer.WriteStartObject("created");
                foreach ((string temporary, string key) in created.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(temporary, key);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}
