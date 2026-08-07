// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Evaluation;
using Rulealize.Abstraction.Values;

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
    internal sealed class StateDraft(StateSnapshot snapshot, int fieldCount) : IStateDraft
    {
        private readonly RuleValue?[] _writes = new RuleValue?[fieldCount];

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
        public ImmutableArray<RuleValue> Commit()
        {
            ImmutableArray<RuleValue>.Builder committed = ImmutableArray.CreateBuilder<RuleValue>(fieldCount);
            for (int i = 0; i < fieldCount; i++)
            {
                committed.Add(_writes[i] ?? snapshot[i]);
            }

            return committed.MoveToImmutable();
        }
    }
}
