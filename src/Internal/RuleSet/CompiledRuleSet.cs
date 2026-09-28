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

        /// <summary>Gets the rule sets this one holds, in the order <c>uses</c> declares them.</summary>
        /// <remarks>
        /// Empty for the great majority of rule sets, and everything that reads it is written
        /// so that empty costs nothing: a rule set holding nothing runs the path it ran before
        /// composition existed.
        /// </remarks>
        public ImmutableArray<HeldRuleSet> Held { get; init; } = [];

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

        /// <summary>Finds a held rule set by its alias.</summary>
        /// <param name="alias">The alias, as <c>uses</c> gave it.</param>
        /// <returns>The held rule set, or <see langword="null"/> when nothing is held by that name.</returns>
        public HeldRuleSet? FindHeld(string alias)
        {
            foreach (HeldRuleSet held in Held)
            {
                if (string.Equals(held.Alias, alias, StringComparison.Ordinal))
                {
                    return held;
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

        /// <summary>Gets what the arguments are checked against once they arrive, in written order.</summary>
        /// <remarks>
        /// Empty unless the input leaves a parameter open, which is the only case the document
        /// is allowed to write one for. A guard answers before the input is offered and a
        /// clause here answers after a value has been supplied, and keeping them apart is what
        /// stops a rule set from turning a legal move into one that is merely likely.
        /// </remarks>
        public ImmutableArray<CompiledValidation> Validations { get; init; } = [];

        public required ImmutableArray<EffectNode> Effects { get; init; }

        /// <summary>Gets the held rule sets' inputs this one drives, in the order written.</summary>
        /// <remarks>
        /// A list rather than an effect, and not reachable from inside a branch, so which
        /// component inputs an input drives can be read off the document without running it —
        /// the property a literal <c>path</c> buys for a write. It is also what lets
        /// <c>GetValidInputs</c> decide a candidate by asking each of them, instead of the
        /// author writing that guard a second time and writing it differently.
        /// </remarks>
        public ImmutableArray<CompiledFire> Fires { get; init; } = [];

        /// <summary>Gets whether anything in this input's effects resolves a chance event.</summary>
        /// <remarks>
        /// Settled while the document is compiled, which is what lets the two-document
        /// <c>ApplyToState</c> refuse an input it cannot resolve on its own before it has
        /// evaluated a thing. An input without one has exactly one outcome, so
        /// <c>GetOutcomes</c> can answer it without enumerating anything.
        /// </remarks>
        public required bool HasDraw { get; init; }

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

    /// <summary>One parameter of an input, and where its value is allowed to come from.</summary>
    /// <remarks>
    /// <para>
    /// Exactly one of <see cref="Domain"/> and <see cref="Open"/> is set, which is the
    /// difference between the two things a parameter can be.
    /// </para>
    /// <para>
    /// A domain is an ordinary expression that yields a sequence, not a node kind of its own.
    /// Its being enumerable is what lets <c>GetValidInputs</c> answer per value: every
    /// candidate is formed and put to the guard, so what comes back is a complete move that
    /// the rules have already allowed.
    /// </para>
    /// <para>
    /// An open parameter gives that up deliberately. Where a value comes from outside — text
    /// somebody typed — there is nothing to enumerate, and what stands in its place is a
    /// schema node saying which values would be admissible. The input is then offered with the
    /// argument still missing, and whether this particular value is allowed is asked when it
    /// arrives rather than before.
    /// </para>
    /// </remarks>
    internal sealed class CompiledParameter
    {
        public required string Name { get; init; }

        public required LocalSlot Slot { get; init; }

        /// <summary>Gets the expression the candidates are enumerated from, or null when open.</summary>
        public required ExpressionNode? Domain { get; init; }

        /// <summary>Gets the schema a value from outside is admitted by, or null when this has a domain.</summary>
        public required SchemaNode? Open { get; init; }

        /// <summary>Gets the <c>op</c> the open schema node was written as, or null when this has a domain.</summary>
        /// <remarks>
        /// Carried because it is what a host reads <c>SchemaNode.Describe</c> against:
        /// the core does not interpret the record, so what names its keys is the operation the
        /// rule set wrote.
        /// </remarks>
        public required string? OpenOp { get; init; }

        /// <summary>Gets the state field the open schema was taken from, or null where one was written out.</summary>
        /// <remarks>
        /// Where a parameter names a field, it uses that field's own schema node, so the two
        /// cannot disagree about what is admissible. What the name buys besides is the label:
        /// a screen asking for a value already has wording for the field it is edited into.
        /// </remarks>
        public required string? OpenField { get; init; }

        /// <summary>Gets a value indicating whether this parameter takes a value from outside.</summary>
        public bool IsOpen => Open is not null;
    }

    /// <summary>One clause of an input's <c>validate</c>.</summary>
    /// <remarks>
    /// The wording belongs to a label document rather than to the rule set, so what is carried
    /// is the code: a rule set that held a sentence would hold it in one language, and a
    /// <c>requires</c> naming the vocabulary only that sentence used would be false.
    /// </remarks>
    internal sealed class CompiledValidation
    {
        /// <summary>Gets what has to be true of the arguments.</summary>
        public required ExpressionNode Require { get; init; }

        /// <summary>Gets the code naming this refusal, unique within the input.</summary>
        public required string Code { get; init; }

        /// <summary>Gets the one open parameter this clause reads, or null where it reads several.</summary>
        /// <remarks>
        /// Inferred from what the clause resolved while it was built, so a host can put the
        /// refusal against the field it is about without the document saying so twice.
        /// </remarks>
        public required string? Parameter { get; init; }
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
