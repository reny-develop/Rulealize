// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Nodes;

namespace Rulealize.Internal.Building
{
    /// <summary>What every builder needs: where it is, and how to build children.</summary>
    internal abstract class BuildContextBase(NodeBuilder builder, SourcePath path) : IBuildContext
    {
        /// <inheritdoc />
        public SourcePath Path => path;

        /// <inheritdoc />
        public IScopeBuilder Scope => builder.Scope;

        /// <inheritdoc />
        public IDefinitionResolver Definitions => builder.Definitions;

        /// <inheritdoc />
        public IStateSchemaResolver State => builder.State;

        /// <summary>Gets the builder these contexts delegate to.</summary>
        protected NodeBuilder Builder => builder;

        /// <inheritdoc />
        public ExpressionNode BuildExpression(JsonElement element, string label) =>
            builder.BuildExpression(element, path.Append(label));

        /// <inheritdoc />
        public EffectNode BuildEffect(JsonElement element, string label) =>
            builder.BuildEffect(element, path.Append(label));

        /// <inheritdoc />
        public SchemaNode BuildSchema(JsonElement element, string label) =>
            builder.BuildSchema(element, path.Append(label));

        /// <inheritdoc />
        public RuleSetBuildException Error(string message) => new(path, message);

        /// <inheritdoc />
        public RuleSetBuildException Error(string propertyName, string message) =>
            new(path.Append(propertyName), message);
    }

    /// <summary>What a shorthand expander is handed.</summary>
    /// <remarks>
    /// It has no node of its own — the shorthand it is expanding is a bare string — so it
    /// gets the surrounding build state and nothing more.
    /// </remarks>
    internal sealed class SugarBuildContext(NodeBuilder builder, SourcePath path) : BuildContextBase(builder, path);

    /// <summary>What a node factory is handed: its JSON, and the surrounding build state.</summary>
    /// <remarks>
    /// The <c>Require</c> and <c>Optional</c> helpers are where the distinction between a
    /// static key and an expression key is enforced. A literal is read here, now — the width
    /// of a board, the members of an enumeration, the name a sequence binds its element to —
    /// and writing an expression in one of those places is a build error rather than
    /// something that would have to be decided at evaluation time.
    /// </remarks>
    internal sealed class NodeBuildContext(NodeBuilder builder, SourcePath path, string op, JsonElement node)
        : BuildContextBase(builder, path), INodeBuildContext
    {
        /// <inheritdoc />
        public string Op => op;

        /// <inheritdoc />
        public JsonElement Node => node;

        /// <inheritdoc />
        public bool TryGetProperty(string name, out JsonElement value) => node.TryGetProperty(name, out value);

        /// <inheritdoc />
        public JsonElement GetRequiredProperty(string name) =>
            node.TryGetProperty(name, out JsonElement value) ? value : throw Missing(name);

        /// <inheritdoc />
        public ExpressionNode RequireExpression(string propertyName) =>
            BuildExpression(GetRequiredProperty(propertyName), propertyName);

        /// <inheritdoc />
        public ExpressionNode? OptionalExpression(string propertyName) =>
            node.TryGetProperty(propertyName, out JsonElement value)
                ? BuildExpression(value, propertyName)
                : null;

        /// <inheritdoc />
        public ImmutableArray<ExpressionNode> RequireExpressionArray(string propertyName)
        {
            JsonElement element = GetRequiredProperty(propertyName);
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw Error(propertyName, "must be an array.");
            }

            ImmutableArray<ExpressionNode>.Builder nodes = ImmutableArray.CreateBuilder<ExpressionNode>();
            SourcePath arrayPath = Path.Append(propertyName);
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                nodes.Add(Builder.BuildExpression(item, arrayPath.Append(index)));
                index++;
            }

            return nodes.ToImmutable();
        }

        /// <inheritdoc />
        public EffectNode RequireEffect(string propertyName) =>
            BuildEffect(GetRequiredProperty(propertyName), propertyName);

        /// <inheritdoc />
        public SchemaNode RequireSchema(string propertyName) =>
            BuildSchema(GetRequiredProperty(propertyName), propertyName);

        /// <inheritdoc />
        public string RequireString(string propertyName) => ReadString(propertyName, GetRequiredProperty(propertyName));

        /// <inheritdoc />
        public string? OptionalString(string propertyName) =>
            node.TryGetProperty(propertyName, out JsonElement value) ? ReadString(propertyName, value) : null;

        /// <inheritdoc />
        public ImmutableArray<string> RequireStringArray(string propertyName)
        {
            JsonElement element = GetRequiredProperty(propertyName);
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw Error(propertyName, "must be an array of literal strings.");
            }

            ImmutableArray<string>.Builder values = ImmutableArray.CreateBuilder<string>();
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw Error(propertyName, "must contain literal strings only.");
                }

                values.Add(item.GetString()!);
            }

            return values.ToImmutable();
        }

        /// <inheritdoc />
        public int RequireInt32(string propertyName) => ReadInt32(propertyName, GetRequiredProperty(propertyName));

        /// <inheritdoc />
        public int OptionalInt32(string propertyName, int defaultValue) =>
            node.TryGetProperty(propertyName, out JsonElement value) ? ReadInt32(propertyName, value) : defaultValue;

        /// <inheritdoc />
        public bool OptionalBoolean(string propertyName, bool defaultValue)
        {
            if (!node.TryGetProperty(propertyName, out JsonElement value))
            {
                return defaultValue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw Error(propertyName, Literal("true or false", value))
            };
        }

        private string ReadString(string propertyName, JsonElement value) =>
            value.ValueKind == JsonValueKind.String
                ? value.GetString()!
                : throw Error(propertyName, Literal("a literal string", value));

        private int ReadInt32(string propertyName, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out decimal number))
            {
                throw Error(propertyName, Literal("a literal whole number", value));
            }

            if (decimal.Truncate(number) != number || number < int.MinValue || number > int.MaxValue)
            {
                throw Error(
                    propertyName,
                    $"must be a whole number in range, but is {number.ToString(CultureInfo.InvariantCulture)}.");
            }

            return (int)number;
        }

        private RuleSetBuildException Missing(string name) => Error($"'{op}' needs a '{name}'.");

        private static string Literal(string wanted, JsonElement value) =>
            value.ValueKind == JsonValueKind.Object && value.TryGetProperty("op", out _)
                ? $"must be {wanted}, not an expression. This key is read when the rule set is built."
                : $"must be {wanted}.";
    }
}
