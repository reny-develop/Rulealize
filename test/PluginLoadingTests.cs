// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Plugin;

namespace Rulealize.Tests
{
    /// <summary>What a runtime accepts as a set of plugins, what it refuses, and what it then reports.</summary>
    /// <remarks>
    /// The refusals all happen when a plugin is added, not when a rule set first reaches for
    /// a contested name. A namespace collision is a fault in how the application is
    /// configured and has nothing to do with any document. A shared shorthand character is
    /// the one thing here that is not a fault at all: nothing about the folder decides it,
    /// so it is left to the document that writes one.
    /// </remarks>
    public class PluginLoadingTests
    {
        [Fact]
        public void ScanningTheFolderFindsTheStandardPlugins()
        {
            RuleRuntime runtime = new RuleRuntime().LoadPluginsFrom(StandardRuntime.PluginFolder);

            Assert.Equal(
                ["bind", "branch", "chance", "cmp", "def", "grid", "logic", "math", "rec", "seq", "state", "tuple",
                 "type"],
                runtime.Plugins.Select(static plugin => plugin.Namespace).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void EveryStandardPluginDeclaresAVersionInTheFirstMajor() =>
            // Chess and shogi each needed vocabulary Reversi never asked for — a sequence
            // written out element by element, a board updated as a value, a list, a record —
            // so several of these are past 1.0. All of it was addition, which is what the
            // major staying at one says.
            Assert.All(
                new RuleRuntime().LoadPluginsFrom(StandardRuntime.PluginFolder).Plugins,
                static plugin => Assert.Equal(1, plugin.Version.Major));

        [Fact]
        public void OnlyThreePluginsReserveAShorthand()
        {
            RuleRuntime runtime = new RuleRuntime().LoadPluginsFrom(StandardRuntime.PluginFolder);

            Dictionary<char, string> reserved = runtime.Plugins
                .Where(static plugin => plugin.ReservedPrefix is not null)
                .ToDictionary(static plugin => plugin.ReservedPrefix!.Value, static plugin => plugin.Namespace);

            Assert.Equal(
                new Dictionary<char, string> { ['$'] = "state", ['@'] = "bind", ['#'] = "def" },
                reserved);
        }

        [Fact]
        public void ARuntimeWithNoPluginsProvidesNoOperations() =>
            // The claim the whole design rests on, stated as an assertion: the core provides
            // no operations at all, not even booleans.
            Assert.Empty(new RuleRuntime().Operations);

        [Fact]
        public void EveryOperationIsQualifiedByTheNamespaceOfThePluginThatRegisteredIt()
        {
            // A plugin registers an unqualified name and the namespace is taken from its
            // manifest, so an operation cannot be reported under a namespace its plugin does
            // not own — the same property that makes the collision check at load time worth
            // anything.
            RuleRuntime runtime = new RuleRuntime().LoadPluginsFrom(StandardRuntime.PluginFolder);

            Assert.NotEmpty(runtime.Operations);
            Assert.All(
                runtime.Operations,
                static operation => Assert.StartsWith(
                    $"{operation.Plugin.Namespace}.", operation.Op, StringComparison.Ordinal));
        }

        [Fact]
        public void EachKindIsReportedApart()
        {
            // A draw is the one that has to be reported, rather than merely being nice to
            // report. It builds an expression node like anything else that produces a value,
            // so its kind is the whole of how the runtime knows to refuse it outside an
            // input's effects — there is nothing about the node itself to go on.
            RuleRuntime runtime = new RuleRuntime().LoadPluginsFrom(StandardRuntime.PluginFolder);

            Assert.Contains(
                runtime.Operations,
                static operation => operation.Op == "grid.at"
                    && operation.Kind == OperationKind.Expression
                    && operation.Plugin.Id == "Rulealize.Plugin.Grid");
            Assert.Contains(
                runtime.Operations,
                static operation => operation.Op == "state.set" && operation.Kind == OperationKind.Effect);
            Assert.Contains(
                runtime.Operations,
                static operation => operation.Op == "type.int" && operation.Kind == OperationKind.Schema);
            Assert.Contains(
                runtime.Operations,
                static operation => operation.Op == "chance.pick"
                    && operation.Kind == OperationKind.Draw
                    && operation.Plugin.Id == "Rulealize.Plugin.Chance");
        }

        [Fact]
        public void OneNameRegisteredAsTwoKindsIsTwoOperations()
        {
            // The tables are separate on purpose: where a node is written is what tells an
            // expression from an effect, never the name. Reported once, whichever kind
            // happened to be found first, a caller could not learn that both exist.
            StubPlugin plugin = new("A", "one")
            {
                Registration = static registry =>
                {
                    registry.AddExpression("thing", static context => throw new NotSupportedException());
                    registry.AddEffect("thing", static context => throw new NotSupportedException());
                }
            };

            RuleRuntime runtime = new RuleRuntime().AddPlugin(plugin);

            Assert.Equal(
                [OperationKind.Expression, OperationKind.Effect],
                runtime.Operations
                    .Where(static operation => operation.Op == "one.thing")
                    .Select(static operation => operation.Kind));
        }

        [Fact]
        public void TwoPluginsCannotShareANamespace()
        {
            RuleRuntime runtime = new RuleRuntime().AddPlugin(new StubPlugin("A", "same"));

            PluginLoadException exception =
                Assert.Throws<PluginLoadException>(() => runtime.AddPlugin(new StubPlugin("B", "same")));

            Assert.Contains("same", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void TwoPluginsMayShareAShorthand()
        {
            // A namespace is one plugin's or the other's, and a character is not. Which of
            // them a rule set meant is a question about that document, asked where the
            // literal is read — see ShorthandTests.
            RuleRuntime runtime = new RuleRuntime()
                .AddPlugin(new StubPlugin("A", "one", '%'))
                .AddPlugin(new StubPlugin("B", "two", '%'));

            Assert.Equal(
                ["one", "two"],
                runtime.Plugins.Select(static plugin => plugin.Namespace).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void OnePluginCannotBeLoadedTwice()
        {
            RuleRuntime runtime = new RuleRuntime().AddPlugin(new StubPlugin("A", "one"));

            Assert.Throws<PluginLoadException>(() => runtime.AddPlugin(new StubPlugin("A", "two")));
        }

        [Fact]
        public void AnOperationCannotBeRegisteredTwice()
        {
            StubPlugin plugin = new("A", "one")
            {
                Registration = static registry =>
                {
                    registry.AddExpression("thing", static context => throw new NotSupportedException());
                    registry.AddExpression("thing", static context => throw new NotSupportedException());
                }
            };

            PluginLoadException exception = Assert.Throws<PluginLoadException>(() => new RuleRuntime().AddPlugin(plugin));

            Assert.IsType<ArgumentException>(exception.InnerException);
        }

        [Fact]
        public void ThePrefixComesFromTheManifestNotFromTheCall()
        {
            // A plugin that never declared a prefix cannot register a shorthand, so it
            // cannot claim a character out from under the plugin that did declare it.
            StubPlugin plugin = new("A", "one")
            {
                Registration = static registry => registry.AddSugar(new StubExpander())
            };

            PluginLoadException exception = Assert.Throws<PluginLoadException>(() => new RuleRuntime().AddPlugin(plugin));

            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        [Fact]
        public void ANamespaceAndAnOperationCombineIntoTheQualifiedName()
        {
            RuleRuntime runtime = new RuleRuntime().LoadPluginsFrom(StandardRuntime.PluginFolder);

            // grid.at exists; at does not, and neither does grid.nope.
            Assert.Contains("'at'", BuildFailure(runtime, "at"), StringComparison.Ordinal);
            Assert.Contains("'grid.nope'", BuildFailure(runtime, "grid.nope"), StringComparison.Ordinal);
        }

        [Fact]
        public void AMissingFolderIsRejected() =>
            Assert.Throws<PluginLoadException>(
                () => new RuleRuntime().LoadPluginsFrom(Path.Combine(AppContext.BaseDirectory, "no-such-folder")));

        [Fact]
        public void AFolderWithNoPluginsInItLoadsNothing()
        {
            // The test output folder itself: full of assemblies, none of them plugins.
            RuleRuntime runtime = new RuleRuntime().LoadPluginsFrom(AppContext.BaseDirectory);

            Assert.Empty(runtime.Plugins);
        }

        private static string BuildFailure(RuleRuntime runtime, string op)
        {
            string ruleSet = $$"""
                {
                  "id": "t", "version": "1.0.0",
                  "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
                  "inputs": { "go": { "effects": [
                    { "op": "state.set", "path": "n", "value": { "op": "{{op}}" } } ] } }
                }
                """;

            return Assert.Throws<Abstraction.RuleSetBuildException>(() => runtime.CreateContext(ruleSet)).Message;
        }

        private sealed class StubPlugin(string id, string @namespace, char? prefix = null) : IRulealizePlugin
        {
            public PluginManifest Manifest { get; } = new(id, new Version(1, 0, 0), @namespace, prefix);

            public Action<IPluginRegistry>? Registration { get; init; }

            public void Register(IPluginRegistry registry) => Registration?.Invoke(registry);
        }

        private sealed class StubExpander : ISugarExpander
        {
            public Abstraction.Node.ExpressionNode Expand(IBuildContext context, string text) =>
                throw new NotSupportedException();
        }
    }
}
