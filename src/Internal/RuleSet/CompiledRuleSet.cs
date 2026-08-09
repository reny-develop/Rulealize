// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Node;
using Rulealize.Abstraction.Value;
using Rulealize.Internal.Building;

namespace Rulealize.Internal.RuleSet
{
    /// <summary>A rule set document after it has been turned into nodes.</summary>
    /// <remarks>
    /// Immutable and free of anything to do with a particular state, so one of these backs
    /// any number of concurrent transitions.
    /// </remarks>
    internal sealed class CompiledRuleSet
    {
        public required string Id { get; init; }

        public required string Version { get; init; }

        /// <summary>Gets the rule set's identity as a state document spells it, <c>id@version</c>.</summary>
        public string Qualified => $"{Id}@{Version}";

        public required StateSchema Schema { get; init; }

        public required ImmutableArray<RuleValue> InitialState { get; init; }

        public required CompiledDefinitions Definitions { get; init; }

        public required ImmutableArray<CompiledInput> Inputs { get; init; }

        public required CompiledTerminal? Terminal { get; init; }

        /// <summary>Finds an input by the name an input document gives.</summary>
        /// <param name="name">The input name.</param>
        /// <returns>The input, or <see langword="null"/> when the rule set has none by that name.</returns>
        public CompiledInput? FindInput(string name)
        {
            foreach (CompiledInput input in Inputs)
            {
                if (string.Equals(input.Name, name, StringComparison.Ordinal))
                {
                    return input;
                }
            }

            return null;
        }
    }

    /// <summary>The bodies of the <c>definitions</c> section, indexed as their descriptors are.</summary>
    /// <remarks>
    /// The core holds definitions but never evaluates one directly. A plugin asks for a body
    /// through the evaluation context, which is what keeps "having definitions" a structural
    /// fact and "calling one" a vocabulary a rule set can decline to load.
    /// </remarks>
    internal sealed class CompiledDefinitions(
        ImmutableArray<ExpressionNode> bodies,
        ImmutableArray<int> frameSizes,
        ImmutableArray<ImmutableArray<LocalSlot>> parameterSlots)
    {
        public int Count => bodies.Length;

        public ExpressionNode Body(int index) => bodies[index];

        public int FrameSize(int index) => frameSizes[index];

        /// <summary>Gets the slots a definition's parameters were given, in declaration order.</summary>
        public ImmutableArray<LocalSlot> ParameterSlots(int index) => parameterSlots[index];
    }

    /// <summary>One entry of the <c>inputs</c> section.</summary>
    internal sealed class CompiledInput
    {
        public required string Name { get; init; }

        /// <summary>Gets the parameters, in declaration order.</summary>
        /// <remarks>
        /// Their slots are the first ones in the frame, in this order, so that binding a
        /// candidate's arguments is a straight walk.
        /// </remarks>
        public required ImmutableArray<CompiledParameter> Parameters { get; init; }

        /// <summary>Gets the expression naming whose input this is, if the rule set says.</summary>
        public required ExpressionNode? Actor { get; init; }

        /// <summary>Gets the guard, or null when the input is always available.</summary>
        public required ExpressionNode? Guard { get; init; }

        public required ImmutableArray<EffectNode> Effects { get; init; }

        /// <summary>Gets the number of local slots this input's expressions need.</summary>
        public required int FrameSize { get; init; }

        public CompiledParameter? FindParameter(string name)
        {
            foreach (CompiledParameter parameter in Parameters)
            {
                if (string.Equals(parameter.Name, name, StringComparison.Ordinal))
                {
                    return parameter;
                }
            }

            return null;
        }
    }

    /// <summary>One parameter of an input, and the domain its candidates come from.</summary>
    /// <remarks>
    /// The domain is an ordinary expression that yields a sequence, not a node kind of its
    /// own. Its being enumerable is the whole reason <c>GetValidInputs</c> can exist, and it
    /// is the tightest constraint the DSL design works under.
    /// </remarks>
    internal sealed class CompiledParameter
    {
        public required string Name { get; init; }

        public required LocalSlot Slot { get; init; }

        public required ExpressionNode Domain { get; init; }
    }

    /// <summary>The <c>terminal</c> section.</summary>
    internal sealed class CompiledTerminal
    {
        public required ExpressionNode When { get; init; }

        /// <summary>Gets the expression describing the outcome, when the rule set gives one.</summary>
        public required ExpressionNode? Result { get; init; }

        public required int FrameSize { get; init; }
    }
}
