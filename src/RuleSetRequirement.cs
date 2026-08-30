// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using Rulealize.Internal.Building;

namespace Rulealize
{
    /// <summary>One entry of a rule set's <c>uses</c>: a rule set it holds, and under what name.</summary>
    /// <remarks>
    /// <para>
    /// <c>uses</c> stands to documents as <c>requires</c> stands to vocabularies, and is read
    /// by the same two parties for the same two reasons: by the runtime, to compile what a
    /// document holds, and by tooling, to work out which documents to fetch before there is a
    /// runtime at all. <see cref="RuleRuntime.CreateContext(string, IReadOnlyDictionary{string, string})"/>
    /// is handed the documents; this is how a caller knows which ones to hand it.
    /// </para>
    /// <para>
    /// The version constraint is parsed by the same code
    /// <see cref="PluginRequirement"/> uses, and means the same three things.
    /// </para>
    /// </remarks>
    public sealed class RuleSetRequirement
    {
        private readonly VersionRequirement _requirement;

        internal RuleSetRequirement(string ruleSet, string alias, string? constraint, VersionRequirement requirement)
        {
            RuleSet = ruleSet;
            Alias = alias;
            Constraint = constraint;
            _requirement = requirement;
        }

        /// <summary>Gets the identifier of the rule set held, as the document wrote it.</summary>
        public string RuleSet { get; }

        /// <summary>Gets the name the holding document calls it by, and that qualifies its inputs.</summary>
        /// <remarks>The identifier itself where <c>as</c> was not written.</remarks>
        public string Alias { get; }

        /// <summary>Gets the constraint as written, or <see langword="null"/> when the entry named no version.</summary>
        public string? Constraint { get; }

        /// <summary>Determines whether a version of that rule set satisfies this requirement.</summary>
        /// <param name="version">The version a document declares, or one an index has published.</param>
        /// <returns><see langword="true"/> when it satisfies the constraint.</returns>
        public bool IsSatisfiedBy(Version version)
        {
            ArgumentNullException.ThrowIfNull(version);
            return _requirement.IsSatisfiedBy(version);
        }

        /// <summary>Reads the <c>uses</c> of a rule set document.</summary>
        /// <param name="ruleSetDocument">The document.</param>
        /// <returns>
        /// One requirement per entry, in the order written. Empty when the document has no
        /// <c>uses</c>, which is every rule set that holds nothing.
        /// </returns>
        /// <exception cref="Abstraction.RuleSetBuildException">
        /// The document is not valid JSON, or its <c>uses</c> is malformed. Nothing else about
        /// the document is examined, and nothing it holds has to be at hand to call this.
        /// </exception>
        /// <remarks>
        /// <para>
        /// A static method taking a string, for the reason
        /// <see cref="PluginRequirement.ReadFrom(string)"/> is one: the point of the call is to
        /// find out what to go and get, so it cannot need what it is going to get.
        /// </para>
        /// <para>
        /// A document a document holds may hold documents of its own, so a tool assembling a
        /// set walks the graph by calling this again on each one it fetches. Which is also
        /// where it will notice a cycle; the runtime refuses one when it compiles, naming the
        /// documents in it, but a fetcher that walked blindly would not get that far.
        /// </para>
        /// </remarks>
        public static ImmutableArray<RuleSetRequirement> ReadFrom(string ruleSetDocument)
        {
            ArgumentNullException.ThrowIfNull(ruleSetDocument);

            using JsonDocument document = RuleRuntime.Parse(ruleSetDocument);
            return RuleSetCompiler.ReadUses(document.RootElement);
        }

        /// <summary>Works out which published version a rule set's constraints call for.</summary>
        /// <param name="wanted">Every constraint on one rule set, from the <c>uses</c> entries that name it.</param>
        /// <param name="published">The versions of it that exist, in any order.</param>
        /// <returns>
        /// The version chosen, or <see langword="null"/> when nothing published satisfies the
        /// constraints — which is also the answer when nothing is published at all. The
        /// caller passed both, so it can say which of the two happened; there is no shortfall
        /// to report back that it does not already hold.
        /// </returns>
        /// <exception cref="ArgumentException"><paramref name="wanted"/> names more than one rule set.</exception>
        /// <remarks>
        /// <para>
        /// <b>The lowest satisfying version wins</b>, for the reason
        /// <see cref="PluginResolution.Resolve"/> gives and through the same code: a
        /// constraint is a statement of what the document needs, so honouring it exactly is
        /// what makes the same document resolve to the same set of documents next year, when
        /// three more versions have shipped. The rule is not the obvious one — most resolvers
        /// take the newest — which is why <c>requires</c> and <c>uses</c> being restored
        /// reproducibly means them agreeing about it here rather than separately.
        /// </para>
        /// <para>
        /// One identifier at a time, and no counterpart to <see cref="PluginResolution"/>
        /// that answers for a whole document, because <c>uses</c> is not flat. Which version
        /// is taken decides which document arrives, which decides what else is named, so the
        /// set is discovered by fetching and is never complete in one pass. The walk, the
        /// cycle and what to do when a late constraint contradicts an early choice are the
        /// fetcher's; this is the question it asks at each step.
        /// </para>
        /// <para>
        /// What comes back is a version, and the document fetched for it is the thing the
        /// runtime actually checks. <see cref="RuleSetIdentity.ReadFrom"/> is how a fetcher
        /// confirms that what arrived says what the index said it would, without compiling it.
        /// </para>
        /// </remarks>
        public static Version? Choose(
            IEnumerable<RuleSetRequirement> wanted,
            IEnumerable<Version> published)
        {
            ArgumentNullException.ThrowIfNull(wanted);
            ArgumentNullException.ThrowIfNull(published);

            ImmutableArray<RuleSetRequirement> constraints = [.. wanted];
            if (constraints.Select(static requirement => requirement.RuleSet)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any())
            {
                // Silently meeting constraints on two rule sets at once would answer a
                // question nobody asked, and the caller grouping them is the caller that
                // walked the graph to find them.
                throw new ArgumentException(
                    "every constraint must name the same rule set; group them before choosing.",
                    nameof(wanted));
            }

            return VersionChoice.Lowest(
                published,
                version => constraints.All(requirement => requirement.IsSatisfiedBy(version)));
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string needed = Constraint is null ? RuleSet : $"{RuleSet} {Constraint}";
            return string.Equals(Alias, RuleSet, StringComparison.Ordinal) ? needed : $"{needed} as {Alias}";
        }
    }
}
