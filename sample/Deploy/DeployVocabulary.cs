// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Evaluation;
using Rulealize.Abstraction.Node;
using Rulealize.Abstraction.Plugin;
using Rulealize.Abstraction.Value;

namespace Rulealize.Sample.Deploy
{
    /// <summary>
    /// The four operations <c>deploy.json</c> needs that no published plugin provides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An ordinary plugin. It implements the same interface as the standard ones,
    /// declares the same manifest, claims a namespace the same way, and would be found by
    /// <c>LoadPluginsFrom</c> if it were compiled into an assembly of its own. What is
    /// different is only how it reaches the runtime: <c>runtime.AddPlugin(new
    /// DeployVocabulary(policy))</c>, from the host that already has the policy in hand.
    /// </para>
    /// <para>
    /// Two conventions matter for a vocabulary that is never published, and both are about
    /// not spending something that belongs to everyone. The identifier and the namespace are
    /// vendor-qualified — <c>Acme.Deploy.Rules</c> and <c>acme</c> — so they cannot collide
    /// with a plugin someone releases later. And no reserved prefix is claimed: there is one
    /// character per plugin and only a handful that can ever be used, so a vocabulary with an
    /// audience of one has no business taking one.
    /// </para>
    /// <para>
    /// Every operation here answers from its arguments and from the immutable policy this
    /// instance was built with, which is a choice this sample makes rather than a rule the
    /// runtime imposes. <c>GetValidInputs</c> evaluates a guard once per candidate in a
    /// parameter's domain, so an operation reading a clock or a database would read it once
    /// per candidate and could answer the same question differently within one call.
    /// </para>
    /// </remarks>
    /// <param name="policy">The freeze calendar and ownership map to answer from.</param>
    public sealed class DeployVocabulary(DeployPolicy policy) : IRulealizePlugin
    {
        private readonly DeployPolicy _policy = policy
            ?? throw new ArgumentNullException(nameof(policy));

        /// <inheritdoc />
        public PluginManifest Manifest { get; } = new("Acme.Deploy.Rules", new Version(1, 0, 0), "acme");

        /// <inheritdoc />
        public void Register(IPluginRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            // acme.newer needs nothing but its arguments, so its factory is a method group
            // exactly as a standard plugin's would be.
            registry.AddExpression("newer", NewerNode.Build);

            // The other three close over the policy. This is the part a folder-scanned
            // plugin cannot do — it is constructed through a parameterless constructor and
            // has nowhere to receive this from.
            registry.AddExpression("frozen", context => new FrozenNode(_policy, context.RequireExpression("date")));
            registry.AddExpression("approvers", context => new ApproversNode(_policy, context.RequireExpression("service")));
            registry.AddExpression("people", _ => new PeopleNode(_policy));
        }
    }

    /// <summary>Whether one version supersedes another.</summary>
    /// <remarks>
    /// Null is answered rather than refused, which is a departure from how <c>cmp.lt</c>
    /// treats it — and a deliberate one. A null right-hand side here means an empty stage,
    /// and "is this an upgrade on nothing at all" has an obvious answer where "does an
    /// absent value sort high or low" does not.
    /// </remarks>
    internal sealed class NewerNode(ExpressionNode left, ExpressionNode right) : ExpressionNode
    {
        public static ExpressionNode Build(INodeBuildContext context) =>
            new NewerNode(context.RequireExpression("left"), context.RequireExpression("right"));

        public override RuleValue Evaluate(IEvaluationContext context)
        {
            RuleValue newer = left.Evaluate(context);
            RuleValue older = right.Evaluate(context);

            if (newer.IsNull)
            {
                return RuleValue.False;
            }

            if (older.IsNull)
            {
                return RuleValue.True;
            }

            return RuleValue.Boolean(
                SemanticVersion.Compare(Read(newer, "acme.newer.left"), Read(older, "acme.newer.right")) > 0);
        }

        private static SemanticVersion Read(RuleValue value, string origin)
        {
            string text = value.AsText(origin);

            return SemanticVersion.TryParse(text, out SemanticVersion version)
                ? version
                : throw new RuleEvaluationException(origin, $"'{text}' is not a version.");
        }
    }

    /// <summary>Whether a date falls inside a change freeze.</summary>
    /// <remarks>
    /// The date is an argument. An operation reading <c>DateTime.Today</c> would need no
    /// argument at all and would work; what it would give up is in the remarks on
    /// <see cref="DeployPolicy"/>.
    /// </remarks>
    internal sealed class FrozenNode(DeployPolicy policy, ExpressionNode date) : ExpressionNode
    {
        public override RuleValue Evaluate(IEvaluationContext context)
        {
            string text = date.Evaluate(context).AsText("acme.frozen.date");

            try
            {
                return RuleValue.Boolean(policy.IsFrozen(DeployPolicy.Day(text)));
            }
            catch (FormatException)
            {
                throw new RuleEvaluationException("acme.frozen.date", $"'{text}' is not a date.");
            }
        }
    }

    /// <summary>Who may sign off on a service.</summary>
    internal sealed class ApproversNode(DeployPolicy policy, ExpressionNode service) : ExpressionNode
    {
        public override RuleValue Evaluate(IEvaluationContext context)
        {
            string name = service.Evaluate(context).AsText("acme.approvers.service");

            return RuleValue.Sequence(
                ImmutableArray.CreateRange(policy.ApproversOf(name), static who => RuleValue.Text(who)));
        }
    }

    /// <summary>Everyone the ownership map names.</summary>
    /// <remarks>
    /// Takes no arguments, and is a parameter domain rather than a guard: <c>approve</c>'s
    /// <c>by</c> ranges over this, and the guard then narrows it to the people who own the
    /// service in question.
    /// </remarks>
    internal sealed class PeopleNode(DeployPolicy policy) : ExpressionNode
    {
        private readonly SequenceValue _people =
            RuleValue.Sequence(ImmutableArray.CreateRange(policy.People, static who => RuleValue.Text(who)));

        public override RuleValue Evaluate(IEvaluationContext context) => _people;
    }
}
