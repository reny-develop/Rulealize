// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction.Evaluation;
using Rulealize.Abstraction.Node;
using Rulealize.Abstraction.Value;

namespace Rulealize.Internal.Building
{
    /// <summary>A JSON scalar written where an expression was expected.</summary>
    /// <remarks>
    /// The kinds that have a JSON form — null, booleans, numbers and text — read as
    /// themselves. Sequences and opaque values have no literal form and are only ever
    /// produced by evaluating something.
    /// </remarks>
    internal sealed class LiteralNode(RuleValue value) : ExpressionNode
    {
        /// <summary>Gets the value, which is the same one however it is evaluated.</summary>
        /// <remarks>
        /// Read while the rule set is built, by the one caller that can act on knowing a guard
        /// before there is a state: a <c>held</c> constraint written <c>false</c> offers
        /// nothing in any state, so its input need not be enumerated at all.
        /// </remarks>
        public RuleValue Value => value;

        public override RuleValue Evaluate(IEvaluationContext context) => value;
    }

    /// <summary>A JSON object with no <c>op</c> key, read as a record.</summary>
    /// <remarks>
    /// <para>
    /// The core's only structural rule about nodes is that they are objects carrying an
    /// <c>op</c>. An object without one is therefore not a node, and the value model has
    /// exactly one thing it can be.
    /// </para>
    /// <para>
    /// Field values are built as expressions rather than read as literals, so a record can
    /// be assembled out of computed parts.
    /// </para>
    /// </remarks>
    internal sealed class RecordLiteralNode(IReadOnlyList<KeyValuePair<string, ExpressionNode>> fields) : ExpressionNode
    {
        public override RuleValue Evaluate(IEvaluationContext context)
        {
            Dictionary<string, RuleValue> values = new(fields.Count, StringComparer.Ordinal);
            foreach ((string name, ExpressionNode node) in fields)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                values[name] = node.Evaluate(context);
            }

            return RuleValue.Record(values);
        }
    }
}
