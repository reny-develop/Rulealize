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

        /// <inheritdoc />
        public override string ToString()
        {
            string needed = Constraint is null ? RuleSet : $"{RuleSet} {Constraint}";
            return string.Equals(Alias, RuleSet, StringComparison.Ordinal) ? needed : $"{needed} as {Alias}";
        }
    }
}
