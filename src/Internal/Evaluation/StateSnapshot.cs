// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Evaluation;
using Rulealize.Abstraction.Value;

namespace Rulealize.Internal.Evaluation
{
    /// <summary>The state as it stood when a transition began.</summary>
    /// <remarks>
    /// Fixed for the duration of a transition, which is what every expression in an input's
    /// effects reads and what makes the memoisation of definition results sound.
    /// </remarks>
    internal sealed class StateSnapshot(ImmutableArray<RuleValue> fields)
    {
        /// <summary>Gets the value of a field.</summary>
        /// <param name="index">The field's position in the schema.</param>
        /// <returns>The value.</returns>
        public RuleValue this[int index] => fields[index];

        /// <summary>Gets the field values, in schema order.</summary>
        public ImmutableArray<RuleValue> Fields => fields;
    }

    /// <summary>Collects the writes of an input's effects.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="Get"/> answers with what has been written so far, falling back to the
    /// snapshot. That is how two effects can both edit one board and have their changes add
    /// up, while the expressions inside them still read the position as it was before either
    /// ran.
    /// </para>
    /// <para>
    /// Writes to the same field overwrite one another; the last one wins.
    /// </para>
    /// </remarks>
    internal sealed class StateDraft(StateSnapshot snapshot, ImmutableArray<StatePath> fields) : IStateDraft
    {
        private readonly RuleValue?[] _writes = new RuleValue?[fields.Length];

        /// <inheritdoc />
        public RuleValue Get(StatePath path)
        {
            ArgumentNullException.ThrowIfNull(path);
            return _writes[path.FieldIndex] ?? snapshot[path.FieldIndex];
        }

        /// <inheritdoc />
        public void Set(StatePath path, RuleValue value)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(value);

            _writes[path.FieldIndex] = value;
        }

        /// <summary>Produces the state the transition arrives at.</summary>
        /// <returns>Every field, written or not.</returns>
        /// <remarks>
        /// A field that was written is handed to its schema to settle before it is stored.
        /// Nothing is asked of a field nobody touched: it came out of a state document or out
        /// of an earlier commit, and either way it has been settled once already.
        /// </remarks>
        public ImmutableArray<RuleValue> Commit()
        {
            ImmutableArray<RuleValue>.Builder committed = ImmutableArray.CreateBuilder<RuleValue>(fields.Length);
            for (int i = 0; i < fields.Length; i++)
            {
                committed.Add(_writes[i] is RuleValue written
                    ? fields[i].Schema.Normalize(written)
                    : snapshot[i]);
            }

            return committed.MoveToImmutable();
        }
    }
}
