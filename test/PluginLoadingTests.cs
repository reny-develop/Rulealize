// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Plugins;

namespace Rulealize.Tests
{
    /// <summary>What a runtime accepts as a set of plugins, and what it refuses.</summary>
    /// <remarks>
    /// The refusals all happen when a plugin is added, not when a rule set first reaches for
    /// a contested name. A namespace collision is a fault in how the application is
    /// configured and has nothing to do with any document.
    /// </remarks>
    public class PluginLoadingTests
    {
        [Fact]
        public void ScanningTheFolderFindsTheTenStandardPlugins()
        {
            RuleRuntime runtime = new RuleRuntime().LoadPluginsFrom(StandardRuntime.PluginFolder);

            Assert.Equal(10, runtime.Plugins.Length);
            Assert.Equal(
                ["bind", "branch", "cmp", "def", "grid", "logic", "math", "seq", "state", "type"],
                runtime.Plugins.Select(static plugin => plugin.Namespace).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void EveryStandardPluginDeclaresVersionOne() =>
            Assert.All(
                new RuleRuntime().LoadPluginsFrom(StandardRuntime.PluginFolder).Plugins,
                static plugin => Assert.Equal(new Version(1, 0, 0), plugin.Version));

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
        public void TwoPluginsCannotShareANamespace()
        {
            RuleRuntime runtime = new RuleRuntime().AddPlugin(new StubPlugin("A", "same"));

            PluginLoadException exception =
                Assert.Throws<PluginLoadException>(() => runtime.AddPlugin(new StubPlugin("B", "same")));

            Assert.Contains("same", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void TwoPluginsCannotShareAShorthand()
        {
            RuleRuntime runtime = new RuleRuntime().AddPlugin(new StubPlugin("A", "one", '%'));

            PluginLoadException exception =
                Assert.Throws<PluginLoadException>(() => runtime.AddPlugin(new StubPlugin("B", "two", '%')));

            Assert.Contains("'%'", exception.Message, StringComparison.Ordinal);
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
            public Abstraction.Nodes.ExpressionNode Expand(IBuildContext context, string text) =>
                throw new NotSupportedException();
        }
    }
}
