// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize.Internal.Building;

namespace Rulealize
{
    /// <summary>Why a requirement could not be met.</summary>
    public enum RequirementShortfall
    {
        /// <summary>Nothing published goes by that identifier.</summary>
        UnknownPlugin,

        /// <summary>The plugin is published, and no released version satisfies the constraint.</summary>
        NoSatisfyingVersion
    }

    /// <summary>A plugin, and the version chosen for it.</summary>
    public sealed class ResolvedPlugin
    {
        internal ResolvedPlugin(string plugin, Version version)
        {
            Plugin = plugin;
            Version = version;
        }

        /// <summary>Gets the plugin identifier.</summary>
        public string Plugin { get; }

        /// <summary>Gets the version that satisfies every constraint the document put on it.</summary>
        public Version Version { get; }

        /// <inheritdoc />
        public override string ToString() => $"{Plugin} {Version}";
    }

    /// <summary>A plugin the document asked for that nothing published can supply.</summary>
    public sealed class UnsatisfiedRequirement
    {
        internal UnsatisfiedRequirement(
            string plugin,
            ImmutableArray<PluginRequirement> requirements,
            ImmutableArray<Version> published,
            RequirementShortfall shortfall)
        {
            Plugin = plugin;
            Requirements = requirements;
            Published = published;
            Shortfall = shortfall;
        }

        /// <summary>Gets the plugin identifier, as the document wrote it.</summary>
        public string Plugin { get; }

        /// <summary>Gets every constraint the document put on that plugin.</summary>
        public ImmutableArray<PluginRequirement> Requirements { get; }

        /// <summary>Gets the versions that are published, which is empty when nothing is.</summary>
        public ImmutableArray<Version> Published { get; }

        /// <summary>Gets which way it fell short.</summary>
        public RequirementShortfall Shortfall { get; }

        /// <inheritdoc />
        public override string ToString() => Shortfall is RequirementShortfall.UnknownPlugin
            ? $"'{Plugin}' is not published."
            : $"{Plugin} {string.Join(" and ", Requirements.Select(static requirement => requirement.Constraint))} "
                + $"is not published; {string.Join(", ", Published)} are.";
    }

    /// <summary>What a rule set's <c>requires</c> comes to, against a set of published versions.</summary>
    /// <remarks>
    /// <para>
    /// This is the pure half of restoring a plugin folder: it decides which version of each
    /// plugin the document calls for, and touches nothing. Fetching the versions it names is
    /// the other half, and it belongs outside this library — evaluation here is computation
    /// over documents already in memory, and a package feed is not that.
    /// </para>
    /// <para>
    /// It exists so that resolving and running cannot disagree. Both read the constraint
    /// through <see cref="PluginRequirement"/>, so a version this chooses is one
    /// <see cref="RuleRuntime.CreateContext(string)"/> will accept, and a document this
    /// cannot resolve is one no folder would have satisfied.
    /// </para>
    /// </remarks>
    public sealed class PluginResolution
    {
        private PluginResolution(
            ImmutableArray<ResolvedPlugin> plugins,
            ImmutableArray<UnsatisfiedRequirement> unsatisfied)
        {
            Plugins = plugins;
            Unsatisfied = unsatisfied;
        }

        /// <summary>Gets whether every requirement was met.</summary>
        public bool IsComplete => Unsatisfied.IsEmpty;

        /// <summary>Gets the version chosen for each plugin, ordered by identifier.</summary>
        /// <remarks>At most one entry per plugin: a folder cannot hold two versions of one assembly.</remarks>
        public ImmutableArray<ResolvedPlugin> Plugins { get; }

        /// <summary>Gets the requirements nothing published could meet, ordered by identifier.</summary>
        public ImmutableArray<UnsatisfiedRequirement> Unsatisfied { get; }

        /// <summary>Works out which versions a document's requirements call for.</summary>
        /// <param name="requirements">What the document requires, from <see cref="PluginRequirement.ReadFrom"/>.</param>
        /// <param name="published">
        /// The released versions of each plugin, by identifier. Identifiers are matched
        /// without regard to case, the way a runtime matches the <c>plugin</c> of a
        /// <c>requires</c> entry against a loaded plugin's manifest.
        /// </param>
        /// <returns>The resolution, complete or not. Nothing here throws on an unmet requirement.</returns>
        /// <remarks>
        /// <para>
        /// <b>The lowest satisfying version wins.</b> A constraint is a statement of what the
        /// document needs, so honouring it exactly is what makes a restore reproducible: the
        /// same document resolves to the same folder next year, when three more versions have
        /// shipped. Moving to a newer one means changing what the document asks for, and
        /// changing a document is not something restoring it should do.
        /// </para>
        /// <para>
        /// Two entries naming one plugin are resolved together, to a version satisfying both.
        /// A resolution offering two versions of one plugin would name a folder that cannot
        /// be built.
        /// </para>
        /// </remarks>
        public static PluginResolution Resolve(
            IEnumerable<PluginRequirement> requirements,
            IReadOnlyDictionary<string, IReadOnlyCollection<Version>> published)
        {
            ArgumentNullException.ThrowIfNull(requirements);
            ArgumentNullException.ThrowIfNull(published);

            Dictionary<string, IReadOnlyCollection<Version>> feed = new(published, StringComparer.OrdinalIgnoreCase);

            ImmutableArray<ResolvedPlugin>.Builder resolved = ImmutableArray.CreateBuilder<ResolvedPlugin>();
            ImmutableArray<UnsatisfiedRequirement>.Builder unsatisfied =
                ImmutableArray.CreateBuilder<UnsatisfiedRequirement>();

            IEnumerable<IGrouping<string, PluginRequirement>> byPlugin = requirements
                .GroupBy(static requirement => requirement.Plugin, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static group => group.Key, StringComparer.Ordinal);

            foreach (IGrouping<string, PluginRequirement> group in byPlugin)
            {
                ImmutableArray<PluginRequirement> wanted = [.. group];

                if (!feed.TryGetValue(group.Key, out IReadOnlyCollection<Version>? versions))
                {
                    unsatisfied.Add(new UnsatisfiedRequirement(
                        group.Key, wanted, [], RequirementShortfall.UnknownPlugin));
                    continue;
                }

                ImmutableArray<Version> ordered = [.. versions.Order()];
                Version? chosen = VersionChoice.Lowest(
                    ordered, version => wanted.All(requirement => requirement.IsSatisfiedBy(version)));

                if (chosen is null)
                {
                    unsatisfied.Add(new UnsatisfiedRequirement(
                        group.Key, wanted, ordered, RequirementShortfall.NoSatisfyingVersion));
                    continue;
                }

                resolved.Add(new ResolvedPlugin(group.Key, chosen));
            }

            return new PluginResolution(resolved.ToImmutable(), unsatisfied.ToImmutable());
        }
    }
}
