// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using Rulealize.Internal.Building;

namespace Rulealize
{
    /// <summary>One entry of a rule set's <c>requires</c>: a plugin, and which of its versions will do.</summary>
    /// <remarks>
    /// <para>
    /// <c>requires</c> is read by the runtime to refuse a document it cannot run, and by
    /// tooling to work out what to fetch before there is a runtime at all. Those two have to
    /// agree about what <c>^1.0</c> means, or a tool assembles a folder the runtime then
    /// rejects — so the constraint is parsed here, once, by the same code
    /// <see cref="RuleRuntime.CreateContext(string)"/> uses.
    /// </para>
    /// <para>
    /// Three forms, and deliberately few: <c>^1.0</c> for anything compatible with 1.0,
    /// <c>&gt;=1.2</c> for a floor, and <c>1.0.0</c> for an exact match. An entry may also
    /// name no version at all, which any version satisfies.
    /// </para>
    /// </remarks>
    public sealed class PluginRequirement
    {
        private readonly VersionRequirement _requirement;

        internal PluginRequirement(string plugin, string? constraint, VersionRequirement requirement)
        {
            Plugin = plugin;
            Constraint = constraint;
            _requirement = requirement;
        }

        /// <summary>Gets the plugin identifier, as the document wrote it.</summary>
        public string Plugin { get; }

        /// <summary>Gets the constraint as written, or <see langword="null"/> when the entry named no version.</summary>
        public string? Constraint { get; }

        /// <summary>Determines whether a version of that plugin satisfies this requirement.</summary>
        /// <param name="version">The version a plugin declares, or one a feed has published.</param>
        /// <returns><see langword="true"/> when it satisfies the constraint.</returns>
        public bool IsSatisfiedBy(Version version)
        {
            ArgumentNullException.ThrowIfNull(version);
            return _requirement.IsSatisfiedBy(version);
        }

        /// <summary>Reads the <c>requires</c> of a rule set document.</summary>
        /// <param name="ruleSetDocument">The document.</param>
        /// <returns>
        /// One requirement per entry, in the order written. Empty when the document has no
        /// <c>requires</c>, which is a rule set that draws on no vocabulary it names.
        /// </returns>
        /// <exception cref="Abstraction.RuleSetBuildException">
        /// The document is not valid JSON, or its <c>requires</c> is malformed. Nothing else
        /// about the document is examined, and no plugin has to be loaded to call this.
        /// </exception>
        /// <remarks>
        /// This is the whole of what a tool needs to read before it has anything to run the
        /// document with — which is why it is a static method taking a string rather than
        /// anything on <see cref="RuleRuntime"/>. A runtime is a vocabulary, and the point of
        /// this call is to find out which vocabulary to go and get.
        /// </remarks>
        public static ImmutableArray<PluginRequirement> ReadFrom(string ruleSetDocument)
        {
            ArgumentNullException.ThrowIfNull(ruleSetDocument);

            using JsonDocument document = RuleRuntime.Parse(ruleSetDocument);
            return RuleSetCompiler.ReadRequirements(document.RootElement);
        }

        /// <inheritdoc />
        public override string ToString() => Constraint is null ? Plugin : $"{Plugin} {Constraint}";
    }
}
