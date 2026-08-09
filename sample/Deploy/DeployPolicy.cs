// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace Rulealize.Sample.Deploy
{
    /// <summary>A change-freeze window, and why it is there.</summary>
    /// <param name="From">The first frozen day, inclusive.</param>
    /// <param name="To">The last frozen day, inclusive.</param>
    /// <param name="Why">What to tell someone who is turned away.</param>
    public sealed record FreezeWindow(DateOnly From, DateOnly To, string Why);

    /// <summary>
    /// The organisation's deployment policy: when it will not ship, and who may sign off on
    /// what.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the data behind three of the four operations in <see cref="DeployVocabulary"/>,
    /// and the reason that vocabulary is a class in this host rather than a plugin on a feed.
    /// A plugin assembly is discovered by scanning a folder and constructed through its
    /// parameterless constructor, so it has nowhere to put any of this. An in-process plugin
    /// takes it as a constructor argument.
    /// </para>
    /// <para>
    /// Immutable, and deliberately so. A rule set's operations must be pure — the runtime
    /// evaluates a guard once per candidate in a parameter's domain and expects the same
    /// answer every time — so what they read has to be a snapshot taken before compilation,
    /// not a live view of anything.
    /// </para>
    /// <para>
    /// Note also what is not here: today's date. Whether a deployment is frozen depends on
    /// it, but it arrives as a state field and is passed to <c>acme.frozen</c> as an
    /// argument. A clock read inside an operation would make the same question return
    /// different answers within a single call to <c>GetValidInputs</c>.
    /// </para>
    /// </remarks>
    public sealed class DeployPolicy
    {
        private readonly ImmutableHashSet<DayOfWeek> _frozenDays;
        private readonly ImmutableArray<FreezeWindow> _windows;
        private readonly ImmutableDictionary<string, ImmutableArray<string>> _approvers;

        /// <summary>Initializes a new instance of the <see cref="DeployPolicy"/> class.</summary>
        /// <param name="frozenDays">Days of the week nothing goes out on.</param>
        /// <param name="windows">Dated freeze windows.</param>
        /// <param name="approvers">Who may sign off, per service.</param>
        public DeployPolicy(
            IEnumerable<DayOfWeek> frozenDays,
            IEnumerable<FreezeWindow> windows,
            IReadOnlyDictionary<string, IReadOnlyList<string>> approvers)
        {
            ArgumentNullException.ThrowIfNull(frozenDays);
            ArgumentNullException.ThrowIfNull(windows);
            ArgumentNullException.ThrowIfNull(approvers);

            _frozenDays = [.. frozenDays];
            _windows = [.. windows];
            _approvers = approvers.ToImmutableDictionary(
                static entry => entry.Key,
                static entry => ImmutableArray.CreateRange(entry.Value),
                StringComparer.Ordinal);

            People =
            [
                .. _approvers.Values.SelectMany(static names => names).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ];
        }

        /// <summary>Gets everyone who may sign off on something, in order and without repeats.</summary>
        /// <remarks>
        /// This is where <c>approve</c>'s <c>by</c> parameter gets its domain. A domain has
        /// to be a finite set the runtime can enumerate, and the set of people is not in the
        /// rule set and not in the state, so the vocabulary supplies it.
        /// </remarks>
        public ImmutableArray<string> People { get; }

        /// <summary>Reads a policy document.</summary>
        /// <param name="path">The file.</param>
        /// <returns>The policy.</returns>
        /// <exception cref="FormatException">The document is not a policy this host understands.</exception>
        public static DeployPolicy Read(string path)
        {
            // Comments allowed, for the same reason a rule set allows them: a policy of any
            // size needs somewhere to say why a window is there.
            JsonDocumentOptions options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), options);
            JsonElement root = document.RootElement;
            JsonElement freeze = root.GetProperty("freeze");

            List<DayOfWeek> days =
            [
                .. freeze.GetProperty("days").EnumerateArray()
                    .Select(static day => Enum.Parse<DayOfWeek>(day.GetString()!, ignoreCase: true)),
            ];

            List<FreezeWindow> windows =
            [
                .. freeze.GetProperty("windows").EnumerateArray().Select(static window => new FreezeWindow(
                    Day(window.GetProperty("from").GetString()!),
                    Day(window.GetProperty("to").GetString()!),
                    window.GetProperty("why").GetString()!)),
            ];

            Dictionary<string, IReadOnlyList<string>> approvers = root.GetProperty("approvers")
                .EnumerateObject()
                .ToDictionary(
                    static service => service.Name,
                    static service => (IReadOnlyList<string>)
                        [.. service.Value.EnumerateArray().Select(static name => name.GetString()!)],
                    StringComparer.Ordinal);

            return new DeployPolicy(days, windows, approvers);
        }

        /// <summary>Parses a date in the form the state documents and the policy both write.</summary>
        /// <param name="text">The date.</param>
        /// <returns>The date.</returns>
        /// <exception cref="FormatException">The text is not a date in that form.</exception>
        public static DateOnly Day(string text) =>
            DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Determines whether nothing may go out on a date.</summary>
        /// <param name="date">The date.</param>
        /// <returns><see langword="true"/> when the date is frozen.</returns>
        public bool IsFrozen(DateOnly date) =>
            _frozenDays.Contains(date.DayOfWeek) || _windows.Any(window => date >= window.From && date <= window.To);

        /// <summary>Explains why a date is frozen, for a host that wants to say so.</summary>
        /// <param name="date">The date.</param>
        /// <returns>The reason, or <see langword="null"/> when the date is not frozen.</returns>
        /// <remarks>
        /// Not part of the vocabulary. A rule set decides what is legal and has no use for
        /// prose; this is here because the screen showing the refusal does.
        /// </remarks>
        public string? WhyFrozen(DateOnly date)
        {
            if (_frozenDays.Contains(date.DayOfWeek))
            {
                return $"nothing ships on a {date.DayOfWeek}";
            }

            return _windows.FirstOrDefault(window => date >= window.From && date <= window.To)?.Why;
        }

        /// <summary>Gets who may sign off on a service.</summary>
        /// <param name="service">The service.</param>
        /// <returns>The approvers, or an empty list for a service nobody owns.</returns>
        /// <remarks>
        /// An unknown service is empty rather than an error. The rule set asks this of every
        /// service in the state while working out what is legal, and a service the ownership
        /// map has not caught up with is a thing nobody can approve — which is the right
        /// answer, and a duller failure than an exception out of the middle of a guard.
        /// </remarks>
        public ImmutableArray<string> ApproversOf(string service) =>
            _approvers.TryGetValue(service, out ImmutableArray<string> names) ? names : [];
    }
}
