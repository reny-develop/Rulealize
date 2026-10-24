// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using Rulealize.Abstraction.Value;
using Rulealize.Internal.RuleSet;

namespace Rulealize.Internal.Document
{
    /// <summary>A commit document, read.</summary>
    /// <param name="Input">The input the commit belongs to.</param>
    /// <param name="Created">The key the store assigned each temporary key, by temporary key.</param>
    /// <param name="Draws">What was drawn, in order; empty where the commit records none.</param>
    internal sealed record CommitRequest(
        string Input,
        IReadOnlyDictionary<string, string> Created,
        ImmutableArray<RuleValue> Draws);

    /// <summary>Reads and writes the <c>rulealize/commit/v1</c> document.</summary>
    /// <remarks>
    /// <para>
    /// What the input alone does not settle about a transition: the keys the store assigned,
    /// because a new instance is written under a temporary key and the store's key is the one
    /// thing the rules could not have known; and, for an input that can draw, what was drawn,
    /// as an outcome document records it. The input and its commit are the whole of a
    /// transition, so a history of the pairs replays.
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

            DocumentFrame.OnlyTheseKeys(document, "a commit", "$schema", "ruleSet", "input", "created", "draws");

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

            ImmutableArray<RuleValue>.Builder draws = ImmutableArray.CreateBuilder<RuleValue>();
            if (document.TryGetProperty("draws", out JsonElement drawn))
            {
                if (drawn.ValueKind != JsonValueKind.Array)
                {
                    throw new RuleDocumentException(
                        "The 'draws' of a commit document must be an array, in the order they were drawn.");
                }

                int index = 0;
                foreach (JsonElement draw in drawn.EnumerateArray())
                {
                    draws.Add(OutcomeDocument.ReadValue(draw, index++));
                }
            }

            return new CommitRequest(name.GetString()!, created, draws.ToImmutable());
        }

        /// <summary>Writes the commit of one transition: its input, the key each new instance was assigned, and what was drawn.</summary>
        /// <param name="ruleSet">The rule set the transition is of.</param>
        /// <param name="input">The input's name.</param>
        /// <param name="created">The key each new instance was assigned, by temporary key.</param>
        /// <param name="draws">What was drawn, for an input that can draw; null writes no <c>draws</c>.</param>
        public static string Write(
            CompiledRuleSet ruleSet,
            string input,
            IReadOnlyDictionary<string, string> created,
            ImmutableArray<RuleValue>? draws)
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
                if (draws is ImmutableArray<RuleValue> drawn)
                {
                    writer.WritePropertyName("draws");
                    OutcomeDocument.WriteValues(writer, drawn);
                }

                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}
