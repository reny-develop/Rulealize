// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize;
using Rulealize.Sample.Deploy;

namespace Rulealize.Tests
{
    /// <summary>A runtime with the standard plugins, and the rule sets built on them.</summary>
    /// <remarks>
    /// <para>
    /// Plugins are loaded from the folder the build dropped them in, by scanning it — the
    /// same path a deployed application takes. Constructing the plugin classes directly
    /// would be simpler and would leave assembly probing, manifest claims and registration
    /// completely untested.
    /// </para>
    /// <para>
    /// Compiling a rule set is not free, and nothing about a context depends on a
    /// particular position, so the collection shares one of each.
    /// </para>
    /// </remarks>
    public sealed class StandardRuntime
    {
        public StandardRuntime()
        {
            Runtime = new RuleRuntime().LoadPluginsFrom(PluginFolder);
            Approval = Runtime.CreateContext(ReadRuleSet("approval.json"));
            Countdown = Runtime.CreateContext(ReadRuleSet("countdown.json"));
            Reversi = Runtime.CreateContext(ReadRuleSet("reversi.json"));
            KitchenSink = Runtime.CreateContext(ReadRuleSet("kitchen-sink.json"));
            Chess = Runtime.CreateContext(ReadRuleSet("chess.json"));
            Shogi = Runtime.CreateContext(ReadRuleSet("shogi.json"));
            Roster = Runtime.CreateContext(ReadRuleSet("roster.json"));
            Blackjack = Runtime.CreateContext(ReadRuleSet("blackjack.json"));
            BlackjackChoice = Runtime.CreateContext(ReadRuleSet("blackjack-choice.json"));

            // The one rule set here whose vocabulary is not entirely on disk. Its own
            // runtime, because AddPlugin adds to the runtime it is called on and the other
            // four have no business seeing acme.* — a context captures the operations
            // available when it was created, and keeping them apart is what proves it.
            DeployRuntime = new RuleRuntime()
                .LoadPluginsFrom(PluginFolder)
                .AddPlugin(new DeployVocabulary(StandardPolicy));

            Deploy = DeployRuntime.CreateContext(ReadRuleSet("deploy.json"));
            ChanceLab = Runtime.CreateContext(ChanceLabDocument);
        }

        /// <summary>Gets the policy the deploy tests are written against.</summary>
        /// <remarks>
        /// Built here rather than read from the sample's Policy folder, because this is the
        /// arrangement the tests are about: the vocabulary's data is a constructor argument,
        /// so a test can state it outright instead of arranging a file to say it. Freezing
        /// Fridays and the whole of December is what the sample's acme.json says.
        /// </remarks>
        public static DeployPolicy StandardPolicy { get; } = new(
            [DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday],
            [new FreezeWindow(new DateOnly(2026, 12, 19), new DateOnly(2027, 1, 4), "year-end change freeze")],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["billing"] = ["ann", "bo", "cy"],
                ["search"] = ["ann", "di"],
                ["web"] = ["bo", "di"],
            });

        /// <summary>Gets where the build put the plugin assemblies.</summary>
        public static string PluginFolder => Path.Combine(AppContext.BaseDirectory, "plugin");

        /// <summary>Every published vocabulary, as a <c>requires</c> a test document can paste in.</summary>
        /// <remarks>
        /// A rule set may use no vocabulary it does not name, so a document written to
        /// demonstrate one fault has to declare everything it happens to touch or it fails on
        /// the declaration instead. Kept here rather than in each test class because there is
        /// one such list and two of them would drift. No version constraints: which version
        /// resolved is <see cref="ResolutionTests"/>'s question, never these.
        /// </remarks>
        public const string Requires = """
              "requires": [
                { "plugin": "Rulealize.Plugin.TypeSchema" },  { "plugin": "Rulealize.Plugin.State" },
                { "plugin": "Rulealize.Plugin.Binding" },     { "plugin": "Rulealize.Plugin.Definition" },
                { "plugin": "Rulealize.Plugin.Sequence" },    { "plugin": "Rulealize.Plugin.Comparison" },
                { "plugin": "Rulealize.Plugin.Logic" },       { "plugin": "Rulealize.Plugin.Branch" },
                { "plugin": "Rulealize.Plugin.Grid" },        { "plugin": "Rulealize.Plugin.Record" },
                { "plugin": "Rulealize.Plugin.Tuple" },       { "plugin": "Rulealize.Plugin.Arithmetic" },
                { "plugin": "Rulealize.Plugin.Chance" }
              ],
            """;

        public RuleRuntime Runtime { get; }

        /// <summary>Gets the rule set the README walks a reader through.</summary>
        /// <remarks>
        /// One of the two here written for somebody who has not decided to use the library
        /// yet — <see cref="Countdown"/> is the other, and smaller. This is the one that has
        /// a reader choosing between inputs rather than only between arguments. It is pinned
        /// down like the rest because a document quoted in a README is a document that has to
        /// keep working, and the one most likely to be read is the worst one to let rot.
        /// </remarks>
        public RuleContext Approval { get; }

        /// <summary>Gets the rule set the quick guide is built around.</summary>
        /// <remarks>
        /// Counting to ten, one to three at a time. It is here for the same reason
        /// <see cref="Approval"/> is — a document printed in full somewhere a beginner will
        /// read it — and it is pinned harder, because <see cref="CountdownTests"/> compares
        /// the copy printed in the guide against the document itself. The README's copy of
        /// approval.json is what that check exists to prevent: it lost a <c>requires</c>
        /// entry, went on compiling, and nothing noticed.
        /// </remarks>
        public RuleContext Countdown { get; }

        /// <summary>Gets the Reversi rule set, which the design was worked out on.</summary>
        public RuleContext Reversi { get; }

        /// <summary>Gets a rule set that reaches the operations Reversi never does.</summary>
        public RuleContext KitchenSink { get; }

        /// <summary>Gets the chess rule set, which the design was tested against.</summary>
        /// <remarks>
        /// Reversi showed the vocabulary was enough to write a game with. Chess asks harder
        /// questions of it: a move whose destination depends on its origin, and a legality
        /// rule about the position the move would produce rather than the one in front of it.
        /// </remarks>
        public RuleContext Chess { get; }

        /// <summary>Gets the shogi rule set, which the compound parameter was measured against.</summary>
        /// <remarks>
        /// Chess showed a compound parameter works. Shogi was written to find where it stops
        /// working: a hand that the state has no shape for, and a rule — a pawn may not be
        /// dropped to give mate — that asks what the opponent could do in a position that
        /// does not exist.
        /// </remarks>
        public RuleContext Shogi { get; }

        /// <summary>Gets a rule set that is not a game at all.</summary>
        /// <remarks>
        /// A week's shift roster. No turn, no opponent, no board — the grid plugin is not
        /// loaded into it — and an outcome that is whether the constraints are satisfied. It
        /// is here because three board games in a row is not evidence that the vocabulary is
        /// general, and because the state of a roster is the shape collections were added for.
        /// </remarks>
        public RuleContext Roster { get; }

        /// <summary>Gets the rule set that has chance in it.</summary>
        /// <remarks>
        /// Blackjack. Every other rule set here is decided entirely by whoever moves; this one
        /// turns on a card nobody picked. <c>hit</c> is one candidate and the thirteen ranks
        /// that could arrive are its outcomes, which is what a rule set with a draw in it
        /// looks like and what <c>GetOutcomes</c> is for.
        /// </remarks>
        public RuleContext Blackjack { get; }

        /// <summary>Gets the same game with the card written as a choice.</summary>
        /// <remarks>
        /// The card is a parameter of the input that draws it, which was the only way to say
        /// it before there was a draw: <c>GetValidInputs</c> lists thirteen hits that differ
        /// only in what the deck produced, all of them reported as the player's. It is here
        /// as the control — the same hands have to come out either way, and holding two
        /// spellings of one game to the same answers is a stronger check than either could
        /// be on its own.
        /// </remarks>
        public RuleContext BlackjackChoice { get; }

        /// <summary>Gets a runtime whose vocabulary is the standard plugins and one local class.</summary>
        public RuleRuntime DeployRuntime { get; }

        /// <summary>Gets the rule set that draws on a vocabulary nobody publishes.</summary>
        /// <remarks>
        /// A deployment pipeline. It is here because every other rule set in this suite is
        /// written against plugins alone, which is not the shape a project using the library
        /// for its own rules will be in: it will have operations worth writing and not worth
        /// packaging. This one has four of them.
        /// </remarks>
        public RuleContext Deploy { get; }

        /// <summary>Gets the rule set every outcome is measured on.</summary>
        /// <remarks>
        /// Not in <c>ruleset/</c> with the others, because it demonstrates nothing and is not
        /// a document anybody would want to read. It is a bench: four inputs, one of which
        /// draws nothing, one that draws evenly, one that draws weighted, and one whose
        /// second draw depends on what its first produced.
        /// </remarks>
        public RuleContext ChanceLab { get; }

        /// <summary>The document behind <see cref="ChanceLab"/>.</summary>
        public const string ChanceLabDocument = """
            {
              "$schema": "rulealize/ruleset/v1",
              "id": "chance-lab",
              "version": "1.0.0",
              "requires": [
                { "plugin": "Rulealize.Plugin.TypeSchema", "version": "^1.1" },
                { "plugin": "Rulealize.Plugin.State", "version": "^1.0" },
                { "plugin": "Rulealize.Plugin.Sequence", "version": "^1.3" },
                { "plugin": "Rulealize.Plugin.Arithmetic", "version": "^1.0" },
                { "plugin": "Rulealize.Plugin.Binding", "version": "^1.0" },
                { "plugin": "Rulealize.Plugin.Record", "version": "^1.0" },
                { "plugin": "Rulealize.Plugin.Chance", "version": "^1.0" }
              ],
              "state": {
                "schema": {
                  "total": { "op": "type.int", "min": 0 },
                  "label": { "op": "type.string", "nullable": true },
                  "bag": { "op": "rec.map", "keys": ["r", "g", "b"],
                           "value": { "op": "type.int", "min": 0 } }
                },
                "initial": { "total": 0, "label": null, "bag": { "r": 3, "g": 1, "b": 0 } }
              },
              "inputs": {
                "wait": {
                  "effects": [
                    { "op": "state.set", "path": "total", "value": { "op": "math.add", "of": ["$total", 1] } }
                  ]
                },
                "roll": {
                  "effects": [
                    { "op": "state.set", "path": "total", "value": { "op": "math.add", "of": ["$total",
                      { "op": "chance.pick", "of": { "op": "seq.of", "of": [1, 2, 3] } }] } }
                  ]
                },
                "grab": {
                  "effects": [
                    { "op": "state.set", "path": "label", "value": {
                      "op": "chance.pick", "of": { "op": "rec.keys", "of": "$bag" }, "as": "k",
                      "weight": { "op": "rec.at", "record": "$bag", "key": "@k" } } }
                  ]
                },
                "cascade": {
                  "effects": [
                    { "op": "state.set", "path": "label", "value": {
                      "op": "bind.let",
                      "bind": {
                        "n": { "op": "chance.pick", "of": { "op": "seq.of", "of": [1, 2, 3] } },
                        "c": { "op": "chance.pick", "of": {
                          "op": "seq.take", "count": "@n",
                          "source": { "op": "seq.of", "of": ["a", "b", "c"] } } }
                      },
                      "in": "@c" } }
                  ]
                },
                "risky": {
                  "effects": [
                    { "op": "state.set", "path": "total", "value": { "op": "math.add", "of": ["$total",
                      { "op": "chance.pick", "of": { "op": "seq.of", "of": [1, -1] } }] } }
                  ]
                },
                "vanish": {
                  "effects": [
                    { "op": "state.set", "path": "label",
                      "value": { "op": "chance.pick", "of": { "op": "seq.empty" } } }
                  ]
                }
              }
            }
            """;

        /// <summary>Reads a rule set document the build copied beside the tests.</summary>
        /// <param name="name">The file name.</param>
        /// <returns>The document.</returns>
        public static string ReadRuleSet(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RuleSet", name));

        /// <summary>Reads a document a Markdown file prints, out of the fence under a heading.</summary>
        /// <remarks>
        /// A document printed in prose is a second copy, and a second copy drifts. The
        /// README’s copy of approval.json is what that looks like: it quietly lost a
        /// <c>requires</c> entry, went on compiling because the shorthand it dropped was still
        /// reachable, and answered the same moves. Reading the block back out is what lets a
        /// test compare it.
        /// </remarks>
        /// <param name="markdown">The file’s text.</param>
        /// <param name="heading">The heading the block sits under, or null for the first one in the file.</param>
        /// <returns>The document, comments and all.</returns>
        public static string PrintedDocument(string markdown, string? heading = null)
        {
            ArgumentNullException.ThrowIfNull(markdown);

            string[] lines = markdown.ReplaceLineEndings("\n").Split('\n');

            int from = 0;
            if (heading is not null)
            {
                from = Array.FindIndex(lines, line => string.Equals(line, heading, StringComparison.Ordinal));
                Assert.True(from >= 0, $"the file no longer has a heading '{heading}'.");
            }

            int open = Array.FindIndex(
                lines,
                from,
                static line => line.StartsWith("```json", StringComparison.Ordinal));
            Assert.True(open >= 0, "the file no longer prints a rule set document.");

            int close = Array.FindIndex(lines, open + 1, static line => line.StartsWith("```", StringComparison.Ordinal));
            Assert.True(close > open, "the block the file opens is never closed.");

            return string.Join('\n', lines[(open + 1)..close]);
        }

        /// <summary>Writes a document back out with its comments and its formatting gone.</summary>
        /// <remarks>
        /// Comments are what two copies of one document are entitled to differ in — the one in
        /// <c>ruleset/</c> explains itself to somebody reading the repository, and the printed
        /// one to somebody who has never seen a rule set. Everything else has to be the same,
        /// down to the order of the keys, because a reordering is a diff somebody should have
        /// to look at.
        /// </remarks>
        /// <param name="document">The document.</param>
        /// <returns>Its canonical text.</returns>
        public static string Canonical(string document)
        {
            using JsonDocument parsed = JsonDocument.Parse(
                document,
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            return JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>Builds a position in which neither player can move.</summary>
        /// <remarks>
        /// Every square is white but a1. A ray out of a1 is white all the way to the edge in
        /// every direction, so it is never closed by a stone of either colour and neither
        /// player has a legal placement. Both must pass, and the second pass ends the game —
        /// which is the only way to reach a terminal state other than filling the board.
        /// </remarks>
        /// <param name="turn">Whose move it is.</param>
        /// <param name="passes">How many passes have already been made.</param>
        /// <returns>A state document.</returns>
        public static string Deadlocked(string turn = "white", int passes = 0)
        {
            List<string> squares = [];
            for (char file = 'a'; file <= 'h'; file++)
            {
                for (int rank = 1; rank <= 8; rank++)
                {
                    if (file != 'a' || rank != 1)
                    {
                        squares.Add($"\"{file}{rank}\": \"white\"");
                    }
                }
            }

            return $$"""
                {
                  "$schema": "rulealize/state/v1",
                  "ruleSet": "reversi@1.0.0",
                  "data": {
                    "board": { {{string.Join(", ", squares)}} },
                    "turn": "{{turn}}",
                    "passes": {{passes}}
                  }
                }
                """;
        }
    }

    /// <summary>Shares one <see cref="StandardRuntime"/> across every test class that asks for it.</summary>
    [CollectionDefinition(Name)]
    public sealed class StandardCollection : ICollectionFixture<StandardRuntime>
    {
        public const string Name = "standard runtime";
    }
}
