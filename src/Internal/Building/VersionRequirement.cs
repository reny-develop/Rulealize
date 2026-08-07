// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics.CodeAnalysis;

namespace Rulealize.Internal.Building
{
    /// <summary>A version constraint written in a rule set's <c>requires</c>.</summary>
    /// <remarks>
    /// <para>
    /// Three forms, deliberately few: <c>^1.0</c> for anything compatible with 1.0,
    /// <c>&gt;=1.2</c> for a floor, and <c>1.0.0</c> for an exact match. A constraint
    /// language rich enough to be interesting is a constraint language rich enough to
    /// surprise, and a rule set's dependencies are not the place for surprises.
    /// </para>
    /// <para>
    /// Caret means the same major version and no older than what was asked for, which is the
    /// usual reading and matches how a plugin's operations may be added but not removed
    /// within a major.
    /// </para>
    /// </remarks>
    internal readonly struct VersionRequirement(VersionRequirement.Comparison comparison, Version version)
    {
        internal enum Comparison
        {
            Any,
            Compatible,
            AtLeast,
            Exact
        }

        /// <summary>Gets a requirement that any version satisfies.</summary>
        public static VersionRequirement Any => new(Comparison.Any, new Version(0, 0));

        /// <summary>Reads a constraint.</summary>
        /// <param name="text">The constraint as written.</param>
        /// <param name="requirement">Receives the parsed constraint.</param>
        /// <returns><see langword="true"/> when the text is a constraint this runtime understands.</returns>
        public static bool TryParse(string text, [NotNullWhen(true)] out VersionRequirement? requirement)
        {
            requirement = null;

            string trimmed = text.Trim();
            Comparison comparison = Comparison.Exact;

            if (trimmed.StartsWith('^'))
            {
                comparison = Comparison.Compatible;
                trimmed = trimmed[1..];
            }
            else if (trimmed.StartsWith(">=", StringComparison.Ordinal))
            {
                comparison = Comparison.AtLeast;
                trimmed = trimmed[2..];
            }

            if (!Version.TryParse(Pad(trimmed), out Version? version))
            {
                return false;
            }

            requirement = new VersionRequirement(comparison, version);
            return true;
        }

        /// <summary>Determines whether a version satisfies this constraint.</summary>
        /// <param name="candidate">The version a loaded plugin declares.</param>
        /// <returns><see langword="true"/> when it satisfies the constraint.</returns>
        public bool IsSatisfiedBy(Version candidate) => comparison switch
        {
            Comparison.Any => true,
            Comparison.Compatible => candidate.Major == version.Major && candidate >= version,
            Comparison.AtLeast => candidate >= version,
            _ => candidate == version
        };

        /// <inheritdoc />
        public override string ToString() => comparison switch
        {
            Comparison.Any => "any version",
            Comparison.Compatible => $"^{version}",
            Comparison.AtLeast => $">={version}",
            _ => version.ToString()
        };

        private static string Pad(string text)
        {
            int parts = text.Count(static character => character == '.') + 1;
            return parts switch
            {
                1 => $"{text}.0.0",
                2 => $"{text}.0",
                _ => text
            };
        }
    }
}
