// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Evaluation;
using Rulealize.Abstraction.Value;

namespace Rulealize.Internal.Evaluation
{
    /// <summary>What a node can see while it evaluates.</summary>
    /// <remarks>
    /// <para>
    /// Immutable: <see cref="Bind"/> copies the frame rather than writing into it. Frames
    /// are a handful of slots, so the copy is cheap, and it buys something that would
    /// otherwise be very hard to get right — a lazy sequence may capture the context it was
    /// created under and be enumerated long after the loop that made it has moved on. With a
    /// mutable frame it would see whatever the loop reached last.
    /// </para>
    /// <para>
    /// Slots are positions in the frame, resolved when the rule set was built, so reading a
    /// local is an array index. Sibling scopes reuse slot numbers; that is safe precisely
    /// because contexts are copied rather than amended.
    /// </para>
    /// </remarks>
    internal sealed class EvaluationContext(EvaluationSession session, RuleValue[] frame) : IEvaluationContext
    {
        /// <inheritdoc />
        public CancellationToken CancellationToken => session.CancellationToken;

        /// <inheritdoc />
        public RuleValue GetState(StatePath path)
        {
            ArgumentNullException.ThrowIfNull(path);
            return session.Snapshot[path.FieldIndex];
        }

        /// <inheritdoc />
        public RuleValue GetLocal(LocalSlot slot) => frame[slot.Index];

        /// <inheritdoc />
        public IEvaluationContext Bind(LocalSlot slot, RuleValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            RuleValue[] extended = (RuleValue[])frame.Clone();
            extended[slot.Index] = value;
            return new EvaluationContext(session, extended);
        }

        /// <inheritdoc />
        public RuleValue Invoke(DefinitionDescriptor definition, ReadOnlySpan<RuleValue> arguments) =>
            session.Invoke(definition, arguments);

        /// <summary>Writes a value into the frame before evaluation starts.</summary>
        /// <param name="slot">The slot.</param>
        /// <param name="value">The value.</param>
        /// <remarks>
        /// For binding an input's arguments to its parameters, where the frame is still
        /// private to the caller and no node has had a chance to capture it.
        /// </remarks>
        public void Seed(LocalSlot slot, RuleValue value) => frame[slot.Index] = value;
    }
}
