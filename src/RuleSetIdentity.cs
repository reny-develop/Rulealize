// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Internal.Building;

namespace Rulealize
{
    /// <summary>What a rule set document calls itself: its <c>id</c> and its <c>version</c>.</summary>
    /// <remarks>
    /// <para>
    /// The other half of fetching. <see cref="RuleSetRequirement.ReadFrom"/> says which
    /// documents to go and get and <see cref="RuleSetRequirement.Choose"/> says which version
    /// of each, but both of those answer from an index, and an index is not what the runtime
    /// reads. A <c>uses</c> entry is met by the version written *inside* the document
    /// supplied for it — <see cref="RuleRuntime.CreateContext(string, IReadOnlyDictionary{string, string})"/>
    /// refuses one that says something else, naming both — so a fetcher that never looks at
    /// what arrived has assembled a set on a proxy and finds out at compile time.
    /// </para>
    /// <para>
    /// This is that look, and it costs a parse rather than a compilation. Nothing but
    /// <c>id</c> and <c>version</c> is examined, for the reason <c>requires</c> is readable
    /// on its own: a document worth fetching is often one that does not compile yet, because
    /// what it holds has not been fetched.
    /// </para>
    /// </remarks>
    public sealed class RuleSetIdentity
    {
        internal RuleSetIdentity(string id, string version)
        {
            Id = id;
            Version = version;
        }

        /// <summary>Gets the rule set's identifier, as the document wrote it.</summary>
        public string Id { get; }

        /// <summary>Gets the rule set's version, as the document wrote it.</summary>
        /// <remarks>
        /// A string, and not a <see cref="System.Version"/>, because the document's is one:
        /// the runtime carries it into a state document's <c>id@version</c> unaltered, and
        /// asks it to be a version number only where something compares it to a constraint.
        /// <see cref="Satisfies"/> is that comparison.
        /// </remarks>
        public string Version { get; }

        /// <summary>Gets the identity a state document carries, <c>id@version</c>.</summary>
        public string RuleSet => $"{Id}@{Version}";

        /// <summary>Determines whether this document is the one a <c>uses</c> entry asked for.</summary>
        /// <param name="requirement">The entry, from <see cref="RuleSetRequirement.ReadFrom"/>.</param>
        /// <returns>
        /// <see langword="true"/> when the identifier is the one named and the version
        /// satisfies the constraint — which is what compiling would check, checked before
        /// there is anything to compile with.
        /// </returns>
        /// <remarks>
        /// Both halves, because either alone is answered too easily. An identifier that
        /// matches says nothing about which revision arrived, and a version that satisfies
        /// says nothing about whose it is.
        /// </remarks>
        public bool Satisfies(RuleSetRequirement requirement)
        {
            ArgumentNullException.ThrowIfNull(requirement);

            return string.Equals(Id, requirement.RuleSet, StringComparison.Ordinal)
                && System.Version.TryParse(Version, out System.Version? declared)
                && requirement.IsSatisfiedBy(declared);
        }

        /// <summary>Reads what a rule set document calls itself.</summary>
        /// <param name="ruleSetDocument">The document.</param>
        /// <returns>Its identifier and version.</returns>
        /// <exception cref="Abstraction.RuleSetBuildException">
        /// The document is not valid JSON, or has no <c>id</c> or no <c>version</c>. Both are
        /// required of every rule set, so a document without them is one nothing could have
        /// published.
        /// </exception>
        /// <remarks>
        /// A static method taking a string, for the reason
        /// <see cref="RuleSetRequirement.ReadFrom(string)"/> is one — except reversed. That
        /// call is made before a fetch, to find out what to get; this one is made after, to
        /// find out what was got.
        /// </remarks>
        public static RuleSetIdentity ReadFrom(string ruleSetDocument)
        {
            ArgumentNullException.ThrowIfNull(ruleSetDocument);

            using JsonDocument document = RuleRuntime.Parse(ruleSetDocument);
            return RuleSetCompiler.ReadIdentity(document.RootElement);
        }

        /// <inheritdoc />
        public override string ToString() => RuleSet;
    }
}
