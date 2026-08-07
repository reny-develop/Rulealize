// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize;

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
            Othello = Runtime.CreateContext(ReadRuleSet("othello.json"));
            KitchenSink = Runtime.CreateContext(ReadRuleSet("kitchen-sink.json"));
        }

        /// <summary>Gets where the build put the plugin assemblies.</summary>
        public static string PluginFolder => Path.Combine(AppContext.BaseDirectory, "plugins");

        public RuleRuntime Runtime { get; }

        /// <summary>Gets the Othello rule set, which the design was worked out on.</summary>
        public RuleContext Othello { get; }

        /// <summary>Gets a rule set that reaches the operations Othello never does.</summary>
        public RuleContext KitchenSink { get; }

        /// <summary>Reads a rule set document the build copied beside the tests.</summary>
        /// <param name="name">The file name.</param>
        /// <returns>The document.</returns>
        public static string ReadRuleSet(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RuleSets", name));

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
                  "ruleSet": "othello@1.0.0",
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
