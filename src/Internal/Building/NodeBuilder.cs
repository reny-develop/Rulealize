// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Node;
using Rulealize.Abstraction.Plugin;
using Rulealize.Abstraction.Value;
using Rulealize.Internal.Plugin;

namespace Rulealize.Internal.Building
{
    /// <summary>Turns JSON into nodes, and is where node placement is enforced.</summary>
    /// <remarks>
    /// <para>
    /// The core recognises exactly one thing about a node: it is an object with an
    /// <c>op</c>. The value of <c>op</c> selects a factory from the operation table, and
    /// everything past that point belongs to the plugin that registered it. Nothing here
    /// knows what any operation means.
    /// </para>
    /// <para>
    /// Which table is consulted depends on where the JSON was found, so asking for an
    /// expression and finding <c>grid.set</c> fails now, with the document in hand, rather
    /// than on the forty-first candidate of a <c>GetValidInputs</c> call.
    /// </para>
    /// </remarks>
    internal sealed class NodeBuilder
    {
        private readonly OperationTable _operations;
        private SourcePath _path;
        private bool _insideEffect;

        public NodeBuilder(OperationTable operations, StateSchema state, DefinitionTable definitions)
        {
            _operations = operations;
            State = state;
            Definitions = definitions;
            Scope = new ScopeBuilder(() => _path);
        }

        public StateSchema State { get; }

        public DefinitionTable Definitions { get; }

        /// <summary>Gets the scope in force where the current node is being built.</summary>
        public ScopeBuilder Scope { get; }

        /// <summary>Gets how many draw nodes have been built so far.</summary>
        /// <remarks>
        /// Read either side of an input to find out whether that input draws, which is what
        /// lets <c>ApplyToState</c> refuse one it cannot resolve alone before it evaluates
        /// anything. A count rather than a flag only because a counter needs no resetting.
        /// </remarks>
        public int Draws { get; private set; }

        /// <summary>Builds an expression from JSON found at a location.</summary>
        /// <param name="element">The JSON.</param>
        /// <param name="path">Where it is in the document.</param>
        /// <returns>The node.</returns>
        public ExpressionNode BuildExpression(JsonElement element, SourcePath path)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (!TryReadOperation(element, path, out string? op))
                    {
                        return BuildRecord(element, path);
                    }

                    if (_operations.TryGetExpression(op!, out ExpressionNodeFactory? factory))
                    {
                        NodeBuildContext context = new(this, path, op!, element);
                        return Enter(path, () => factory!(context));
                    }

                    // A draw produces a value, so this is where it is written. What it is
                    // not allowed to do is be written anywhere the runtime evaluates while
                    // sifting candidates or memoizing a result, and that is every expression
                    // position outside an input's effects.
                    if (_operations.TryGetDraw(op!, out DrawNodeFactory? draw))
                    {
                        if (!_insideEffect)
                        {
                            throw new RuleSetBuildException(
                                path,
                                $"'{op}' is a draw, and a draw may only appear inside an input's 'effects'. "
                                + "A guard, a parameter domain, an actor, 'terminal' and a definition body are all "
                                + "evaluated where there is no outcome to draw for.");
                        }

                        NodeBuildContext drawContext = new(this, path, op!, element);
                        Draws++;
                        return Enter(path, () => draw!(drawContext));
                    }

                    throw WrongKind(op!, path, "an expression");

                case JsonValueKind.String:
                    return BuildString(element.GetString()!, path);

                case JsonValueKind.Number:
                    return new LiteralNode(RuleValue.Number(element.GetDecimal()));

                case JsonValueKind.True:
                    return new LiteralNode(RuleValue.True);

                case JsonValueKind.False:
                    return new LiteralNode(RuleValue.False);

                case JsonValueKind.Null:
                    return new LiteralNode(RuleValue.Null);

                default:
                    throw new RuleSetBuildException(
                        path,
                        "an array is not an expression. Sequences are produced by operations, never written down.");
            }
        }

        /// <summary>Builds an effect from JSON found at a location.</summary>
        /// <param name="element">The JSON.</param>
        /// <param name="path">Where it is in the document.</param>
        /// <returns>The node.</returns>
        public EffectNode BuildEffect(JsonElement element, SourcePath path)
        {
            if (element.ValueKind != JsonValueKind.Object || !TryReadOperation(element, path, out string? op))
            {
                throw new RuleSetBuildException(path, "an effect must be an object with an 'op'.");
            }

            if (_operations.TryGetEffect(op!, out EffectNodeFactory? factory))
            {
                NodeBuildContext context = new(this, path, op!, element);

                // Everything built from here down is inside an effect, however deep, which
                // is the one place a draw belongs. Saved and restored rather than simply set,
                // because an effect may build a child effect.
                bool outside = _insideEffect;
                _insideEffect = true;
                try
                {
                    return Enter(path, () => factory!(context));
                }
                finally
                {
                    _insideEffect = outside;
                }
            }

            throw WrongKind(op!, path, "an effect");
        }

        /// <summary>Builds a schema node from JSON found at a location.</summary>
        /// <param name="element">The JSON.</param>
        /// <param name="path">Where it is in the document.</param>
        /// <returns>The node.</returns>
        public SchemaNode BuildSchema(JsonElement element, SourcePath path)
        {
            if (element.ValueKind != JsonValueKind.Object || !TryReadOperation(element, path, out string? op))
            {
                throw new RuleSetBuildException(path, "a schema must be an object with an 'op'.");
            }

            if (_operations.TryGetSchema(op!, out SchemaNodeFactory? factory))
            {
                NodeBuildContext context = new(this, path, op!, element);
                return Enter(path, () => factory!(context));
            }

            throw WrongKind(op!, path, "a schema");
        }

        private TNode Enter<TNode>(SourcePath path, Func<TNode> build)
        {
            SourcePath previous = _path;
            _path = path;
            try
            {
                return build();
            }
            finally
            {
                _path = previous;
            }
        }

        private ExpressionNode BuildString(string text, SourcePath path)
        {
            // A leading character some plugin reserved makes this a shorthand. The core
            // never learns what any of them expand to.
            if (text.Length > 0 && _operations.TryGetSugar(text[0], out ISugarExpander? expander))
            {
                return Enter(path, () => expander!.Expand(new SugarBuildContext(this, path), text));
            }

            return new LiteralNode(RuleValue.Text(text));
        }

        private ExpressionNode BuildRecord(JsonElement element, SourcePath path)
        {
            List<KeyValuePair<string, ExpressionNode>> fields = [];
            foreach (JsonProperty property in element.EnumerateObject())
            {
                fields.Add(new KeyValuePair<string, ExpressionNode>(
                    property.Name,
                    BuildExpression(property.Value, path.Append(property.Name))));
            }

            return new RecordLiteralNode(fields);
        }

        private static bool TryReadOperation(JsonElement element, SourcePath path, out string? op)
        {
            op = null;
            if (!element.TryGetProperty("op", out JsonElement value))
            {
                return false;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                throw new RuleSetBuildException(path.Append("op"), "must be the literal name of an operation.");
            }

            op = value.GetString();
            return true;
        }

        private RuleSetBuildException WrongKind(string op, SourcePath path, string wanted)
        {
            string? actual = _operations.DescribeKind(op);
            return actual is null
                ? new RuleSetBuildException(
                    path,
                    $"'{op}' is not an operation any loaded plugin provides. Check the rule set's 'requires'.")
                : new RuleSetBuildException(path, $"'{op}' is {actual} and cannot appear where {wanted} is expected.");
        }
    }
}
