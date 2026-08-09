// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Globalization;

namespace Rulealize.Sample.Deploy
{
    /// <summary>A version, ordered the way semantic versioning says to order one.</summary>
    /// <remarks>
    /// <para>
    /// The whole reason <c>acme.newer</c> exists. Deciding whether one version supersedes
    /// another is an algorithm — three numbers compared in order, then a pre-release tag
    /// that makes a version <em>lower</em> than the same version without one — and no
    /// arrangement of the standard vocabulary computes it. <c>cmp.lt</c> would compare the
    /// text and put <c>"2.4.0-rc.1"</c> after <c>"2.4.0"</c>, which is exactly backwards and
    /// would ship a release candidate over a release.
    /// </para>
    /// <para>
    /// That is the shape of thing worth adding a vocabulary for. A lookup table can usually
    /// be pushed into the state document instead; an algorithm cannot be pushed anywhere.
    /// </para>
    /// <para>
    /// Build metadata is parsed and then ignored, which is what the specification says to do
    /// with it.
    /// </para>
    /// </remarks>
    internal readonly struct SemanticVersion
    {
        private readonly int _major;
        private readonly int _minor;
        private readonly int _patch;
        private readonly ImmutableArray<string> _prerelease;

        private SemanticVersion(int major, int minor, int patch, ImmutableArray<string> prerelease)
        {
            _major = major;
            _minor = minor;
            _patch = patch;
            _prerelease = prerelease;
        }

        /// <summary>Reads a version.</summary>
        /// <param name="text">The version as written.</param>
        /// <param name="version">Receives the version.</param>
        /// <returns><see langword="true"/> when the text is a version.</returns>
        public static bool TryParse(string text, out SemanticVersion version)
        {
            version = default;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            // Build metadata takes no part in precedence, so it is discarded here rather
            // than carried around and then ignored at every comparison.
            int plus = text.IndexOf('+', StringComparison.Ordinal);
            string withoutBuild = plus < 0 ? text : text[..plus];

            int dash = withoutBuild.IndexOf('-', StringComparison.Ordinal);
            string core = dash < 0 ? withoutBuild : withoutBuild[..dash];
            string tag = dash < 0 ? string.Empty : withoutBuild[(dash + 1)..];

            string[] parts = core.Split('.');
            if (parts.Length != 3
                || !TryNumber(parts[0], out int major)
                || !TryNumber(parts[1], out int minor)
                || !TryNumber(parts[2], out int patch))
            {
                return false;
            }

            ImmutableArray<string> prerelease = [];
            if (dash >= 0)
            {
                string[] identifiers = tag.Split('.');
                if (identifiers.Any(static identifier => !IsIdentifier(identifier)))
                {
                    return false;
                }

                prerelease = [.. identifiers];
            }

            version = new SemanticVersion(major, minor, patch, prerelease);
            return true;
        }

        /// <summary>Orders two versions by precedence.</summary>
        /// <param name="left">The left version.</param>
        /// <param name="right">The right version.</param>
        /// <returns>Negative, zero or positive, as comparisons go.</returns>
        public static int Compare(SemanticVersion left, SemanticVersion right)
        {
            int core = left._major.CompareTo(right._major);
            if (core != 0)
            {
                return core;
            }

            core = left._minor.CompareTo(right._minor);
            if (core != 0)
            {
                return core;
            }

            core = left._patch.CompareTo(right._patch);
            if (core != 0)
            {
                return core;
            }

            // A pre-release is behind the release it leads to. This is the clause text
            // ordering gets wrong.
            if (left._prerelease.IsEmpty || right._prerelease.IsEmpty)
            {
                return right._prerelease.Length.CompareTo(left._prerelease.Length) switch
                {
                    0 => 0,
                    < 0 => -1,
                    _ => 1
                };
            }

            for (int at = 0; at < Math.Min(left._prerelease.Length, right._prerelease.Length); at++)
            {
                int identifier = CompareIdentifiers(left._prerelease[at], right._prerelease[at]);
                if (identifier != 0)
                {
                    return identifier;
                }
            }

            // rc.1 comes before rc.1.2: everything they share is equal, so the longer one wins.
            return left._prerelease.Length.CompareTo(right._prerelease.Length);
        }

        private static int CompareIdentifiers(string left, string right)
        {
            bool leftIsNumber = TryNumber(left, out int leftNumber);
            bool rightIsNumber = TryNumber(right, out int rightNumber);

            return (leftIsNumber, rightIsNumber) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left, right)
            };
        }

        private static bool TryNumber(string text, out int value) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

        private static bool IsIdentifier(string text) =>
            text.Length > 0 && text.All(static character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }
}
