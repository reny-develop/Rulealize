// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Node;
using Rulealize.Internal.Building;

namespace Rulealize.Internal.RuleSet
{
    /// <summary>One entry of the <c>uses</c> section: a rule set a composite holds.</summary>
    /// <remarks>
    /// <para>
    /// Holding is nesting and nothing else. The component's state is a field of the
    /// composite's, its inputs are offered under <c>alias.input</c>, and it moves only by its
    /// own inputs under its own rules — so whatever a walk of the component alone found is
    /// still an upper bound on what it does inside any composite that holds it.
    /// </para>
    /// <para>
    /// What a composite may add is refusal. <see cref="Constraints"/> is the whole of it.
    /// </para>
    /// </remarks>
    internal sealed class HeldRuleSet
    {
        /// <summary>Gets the name this composite calls it by, and the one that qualifies its inputs.</summary>
        public required string Alias { get; init; }

        /// <summary>Gets the component, compiled.</summary>
        public required CompiledRuleSet Rules { get; init; }

        /// <summary>Gets the composite state field its state occupies.</summary>
        public required StatePath Field { get; init; }

        /// <summary>Gets the schema of that field, which packs and unpacks the component's state.</summary>
        public HeldStateSchema Schema => (HeldStateSchema)Field.Schema;

        /// <summary>Gets what the composite refuses, by component input name.</summary>
        public required ImmutableArray<HeldConstraint> Constraints { get; init; }

        /// <summary>Finds what the composite says about one of the component's inputs.</summary>
        /// <param name="input">The component's input name, unqualified.</param>
        /// <returns>The constraint, or <see langword="null"/> where the composite says nothing.</returns>
        public HeldConstraint? FindConstraint(string input)
        {
            foreach (HeldConstraint constraint in Constraints)
            {
                if (string.Equals(constraint.Input, input, StringComparison.Ordinal))
                {
                    return constraint;
                }
            }

            return null;
        }
    }

    /// <summary>One held rule set's input, driven by an input of the composite.</summary>
    /// <remarks>
    /// <para>
    /// This is how a composite makes several things one thing. The merged document that
    /// composition replaces has a <c>grant</c> that also performs the assignment; two
    /// components cannot, unless one composite input drives both of their inputs in a single
    /// transition, and the intermediate state — granted, not yet assigned — becomes a state
    /// the process never occupies rather than one it passes through.
    /// </para>
    /// <para>
    /// It writes nothing itself. What runs is the component's own input, with its own
    /// domains, its own guard and its own effects, so the upper bound a walk of the component
    /// alone gives still holds. The composite chooses <em>when</em> and with <em>what</em>,
    /// and the component decides whether that is allowed.
    /// </para>
    /// </remarks>
    internal sealed class CompiledFire
    {
        /// <summary>Gets the alias of the rule set whose input this is.</summary>
        public required string Alias { get; init; }

        /// <summary>Gets the qualified name, as a diagnostic writes it.</summary>
        public required string Name { get; init; }

        /// <summary>Gets the component's input.</summary>
        public required CompiledInput Declared { get; init; }

        /// <summary>Gets one argument expression per parameter, in the input's declaration order.</summary>
        /// <remarks>
        /// Evaluated in the composite — against the state the transition found, with the
        /// composite input's own parameters in scope.
        /// </remarks>
        public required ImmutableArray<ExpressionNode> Arguments { get; init; }
    }

    /// <summary>What a composite adds to one of a held rule set's inputs.</summary>
    /// <remarks>
    /// <para>
    /// A guard and nothing else. It is evaluated in the composite — over the whole composed
    /// state, and against the composite's definitions — with the component input's parameters
    /// in scope, which is the guard that could not be written while the two halves were two
    /// documents.
    /// </para>
    /// <para>
    /// It may only narrow. The component's own <c>when</c> still has to hold, and this is
    /// asked afterwards, so writing <c>false</c> here hides an input rather than granting
    /// one.
    /// </para>
    /// </remarks>
    internal sealed class HeldConstraint
    {
        public required string Input { get; init; }

        /// <summary>Gets the guard, in the composite's terms.</summary>
        public required ExpressionNode When { get; init; }

        /// <summary>Gets whether the guard is the literal <c>false</c>, and so refuses everywhere.</summary>
        /// <remarks>
        /// The way an input is hidden, which is what a composite does to one it means to drive
        /// from <c>fires</c> and nowhere else. Known when the rule set is built, so the
        /// candidate search skips the input rather than enumerating a domain to discard all of
        /// it — a hidden input would otherwise spend the caller's <c>validationLimit</c> on
        /// candidates that cannot be offered.
        /// </remarks>
        public required bool Never { get; init; }

        /// <summary>Gets the local slot each of the component input's parameters was given.</summary>
        /// <remarks>
        /// One per parameter of the component's input, in that input's declaration order, so
        /// binding a candidate's arguments for this guard is the same straight walk it is for
        /// the component's own.
        /// </remarks>
        public required ImmutableArray<LocalSlot> ParameterSlots { get; init; }

        public required int FrameSize { get; init; }
    }
}
