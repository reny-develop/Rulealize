// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize.Internal.Building
{
    /// <summary>Which of a set of published versions a set of constraints calls for.</summary>
    /// <remarks>
    /// One line, and it is here rather than at either of its two call sites because it is the
    /// rule itself. <see cref="PluginResolution"/> and <see cref="RuleSetRequirement.Choose"/>
    /// exist so that a plugin folder and a document set are not assembled by two readings of
    /// <c>^1.0</c>; a pair that agreed about which versions satisfy a constraint and then
    /// picked different ones out of that set would have moved the disagreement rather than
    /// removed it.
    /// </remarks>
    internal static class VersionChoice
    {
        /// <summary>Takes the lowest published version that satisfies every constraint.</summary>
        /// <param name="published">The versions that exist, in any order.</param>
        /// <param name="satisfies">Whether one of them meets all of the constraints at once.</param>
        /// <returns>The version chosen, or <see langword="null"/> when none of them will do.</returns>
        /// <remarks>Why the lowest is on both callers, which are where somebody reads it.</remarks>
        public static Version? Lowest(IEnumerable<Version> published, Func<Version, bool> satisfies) =>
            published.Order().FirstOrDefault(satisfies);
    }
}
