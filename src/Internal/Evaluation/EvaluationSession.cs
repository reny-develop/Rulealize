// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Value;
using Rulealize.Internal.RuleSet;

namespace Rulealize.Internal.Evaluation
{
    /// <summary>Everything shared by the expressions evaluated against one snapshot.</summary>
    /// <remarks>
    /// <para>
    /// One session covers a whole call: a single transition, or an entire
    /// <c>GetValidInputs</c> sweep over hundreds of candidates. Because the snapshot does
    /// not move underneath it, the definition results it caches stay valid for the whole
    /// sweep.
    /// </para>
    /// <para>
    /// That cache is what keeps Reversi's flip computation from being repeated. It is
    /// reached from the placement guard and from the effect that follows, with the same
    /// coordinate, for each of sixty-four candidates — and each evaluation walks eight rays.
    /// </para>
    /// </remarks>
    internal sealed class EvaluationSession(
        CompiledDefinitions definitions,
        StateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        private readonly Dictionary<DefinitionCall, RuleValue> _memo = [];

        public StateSnapshot Snapshot => snapshot;

        public CancellationToken CancellationToken => cancellationToken;

        /// <summary>Creates a context over a fresh frame.</summary>
        /// <param name="frameSize">How many local slots the frame needs.</param>
        /// <returns>The context.</returns>
        public EvaluationContext CreateContext(int frameSize)
        {
            RuleValue[] frame = new RuleValue[frameSize];
            Array.Fill(frame, RuleValue.Null);
            return new EvaluationContext(this, frame);
        }

        /// <summary>Evaluates a definition body.</summary>
        /// <param name="definition">The definition.</param>
        /// <param name="arguments">One value per parameter, in declaration order.</param>
        /// <returns>The value of the body.</returns>
        public RuleValue Invoke(DefinitionDescriptor definition, ReadOnlySpan<RuleValue> arguments)
        {
            ArgumentNullException.ThrowIfNull(definition);

            cancellationToken.ThrowIfCancellationRequested();

            DefinitionCall call = new(definition.Index, arguments.ToArray());
            if (_memo.TryGetValue(call, out RuleValue? cached))
            {
                return cached;
            }

            // A fresh frame holding the arguments and nothing else. The body cannot see the
            // caller's locals, which is what fixes a definition's meaning independently of
            // where it is used.
            RuleValue[] frame = new RuleValue[definitions.FrameSize(definition.Index)];
            Array.Fill(frame, RuleValue.Null);

            ImmutableArray<LocalSlot> slots = definitions.ParameterSlots(definition.Index);
            for (int i = 0; i < arguments.Length && i < slots.Length; i++)
            {
                frame[slots[i].Index] = arguments[i];
            }

            RuleValue result = definitions.Body(definition.Index).Evaluate(new EvaluationContext(this, frame));
            _memo[call] = result;
            return result;
        }

        /// <summary>A definition and the arguments it was given, as a cache key.</summary>
        /// <remarks>
        /// Equality is the value model's, so two calls with equal coordinates hit the same
        /// entry. Hashing a sequence argument walks it, so a rule set that passes large
        /// sequences to definitions pays for the cache rather than gaining from it.
        /// </remarks>
        private readonly struct DefinitionCall(int index, RuleValue[] arguments) : IEquatable<DefinitionCall>
        {
            public bool Equals(DefinitionCall other)
            {
                if (other.Index != index || other.Arguments.Length != arguments.Length)
                {
                    return false;
                }

                for (int i = 0; i < arguments.Length; i++)
                {
                    if (!arguments[i].Equals(other.Arguments[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            public override bool Equals(object? obj) => obj is DefinitionCall other && Equals(other);

            public override int GetHashCode()
            {
                HashCode hash = default;
                hash.Add(index);
                foreach (RuleValue argument in arguments)
                {
                    hash.Add(argument);
                }

                return hash.ToHashCode();
            }

            private int Index => index;

            private RuleValue[] Arguments => arguments;
        }
    }
}
