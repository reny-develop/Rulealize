// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize;
using Rulealize.Sample.Deploy;

namespace Rulealize.Tests
{
    /// <summary>A runtime with the ten standard plugins, and the rule sets built on them.</summary>
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
            Reversi = Runtime.CreateContext(ReadRuleSet("reversi.json"));
            KitchenSink = Runtime.CreateContext(ReadRuleSet("kitchen-sink.json"));
            Chess = Runtime.CreateContext(ReadRuleSet("chess.json"));
            Shogi = Runtime.CreateContext(ReadRuleSet("shogi.json"));
            Roster = Runtime.CreateContext(ReadRuleSet("roster.json"));

            // The one rule set here whose vocabulary is not entirely on disk. Its own
            // runtime, because AddPlugin adds to the runtime it is called on and the other
            // four have no business seeing acme.* — a context captures the operations
            // available when it was created, and keeping them apart is what proves it.
            DeployRuntime = new RuleRuntime()
                .LoadPluginsFrom(PluginFolder)
                .AddPlugin(new DeployVocabulary(StandardPolicy));

            Deploy = DeployRuntime.CreateContext(ReadRuleSet("deploy.json"));
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

        public RuleRuntime Runtime { get; }

        /// <summary>Gets the rule set the README walks a reader through.</summary>
        /// <remarks>
        /// The smallest one here, and the only one whose audience is somebody who has not
        /// decided to use the library yet. It is pinned down like the rest because a
        /// document quoted in a README is a document that has to keep working, and the one
        /// most likely to be read is the worst one to let rot.
        /// </remarks>
        public RuleContext Approval { get; }

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

        /// <summary>Gets a runtime whose vocabulary is twelve plugins and one local class.</summary>
        public RuleRuntime DeployRuntime { get; }

        /// <summary>Gets the rule set that draws on a vocabulary nobody publishes.</summary>
        /// <remarks>
        /// A deployment pipeline. It is here because every other rule set in this suite is
        /// written against plugins alone, which is not the shape a project using the library
        /// for its own rules will be in: it will have operations worth writing and not worth
        /// packaging. This one has four of them.
        /// </remarks>
        public RuleContext Deploy { get; }

        /// <summary>Reads a rule set document the build copied beside the tests.</summary>
        /// <param name="name">The file name.</param>
        /// <returns>The document.</returns>
        public static string ReadRuleSet(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RuleSet", name));

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
