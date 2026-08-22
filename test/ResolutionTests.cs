// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;

namespace Rulealize.Tests
{
    /// <summary>Reading a rule set's <c>requires</c>, and working out what would satisfy it.</summary>
    /// <remarks>
    /// These two answer the question a tool asks before there is a runtime at all: which
    /// vocabulary does this document need, and which versions of it should be fetched. The
    /// point of them living in this library is that resolving and running cannot then read
    /// <c>^1.0</c> differently — the last test here is the one that says so.
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class ResolutionTests(StandardRuntime standard)
    {
        [Fact]
        public void RequirementsAreReadableWithNoPluginLoadedAndNoRuntimeAtAll()
        {
            // The whole reason this is a static method taking a string. A tool reads the
            // document to find out what to go and get, which is necessarily before it has it.
            ImmutableArray<PluginRequirement> required = PluginRequirement.ReadFrom("""
                { "requires": [ { "plugin": "Acme.Nothing.Here", "version": "^3.1" } ] }
                """);

            PluginRequirement only = Assert.Single(required);
            Assert.Equal("Acme.Nothing.Here", only.Plugin);
            Assert.Equal("^3.1", only.Constraint);
            Assert.Equal("Acme.Nothing.Here ^3.1", only.ToString());
        }

        [Fact]
        public void NothingBesidesRequiresIsExamined() =>
            // Not laxity: a document is worth fetching plugins for before it compiles, and
            // deciding it is malformed is CreateContext's job, done against a full vocabulary.
            Assert.Empty(PluginRequirement.ReadFrom("""{ "inputs": "not even the right kind" }"""));

        [Fact]
        public void AnEntryNamingNoVersionIsSatisfiedByAnything()
        {
            PluginRequirement only = Assert.Single(
                PluginRequirement.ReadFrom("""{ "requires": [ { "plugin": "P" } ] }"""));

            Assert.Null(only.Constraint);
            Assert.True(only.IsSatisfiedBy(new Version(0, 1, 0)));
            Assert.True(only.IsSatisfiedBy(new Version(9, 9, 9)));
        }

        [Fact]
        public void AMalformedConstraintIsRefusedTheWayCompilingRefusesIt()
        {
            const string document = """
                { "id": "t", "version": "1.0.0",
                  "requires": [ { "plugin": "Rulealize.Plugin.Logic", "version": "~1.0" } ],
                  "state": { "schema": {}, "initial": {} }, "inputs": {} }
                """;

            string reading = Assert.Throws<Abstraction.RuleSetBuildException>(
                () => PluginRequirement.ReadFrom(document)).Message;
            string compiling = Assert.Throws<Abstraction.RuleSetBuildException>(
                () => standard.Runtime.CreateContext(document)).Message;

            Assert.Equal(compiling, reading);
            Assert.Contains("^1.0, >=1.0 or 1.0.0", reading, StringComparison.Ordinal);
        }

        [Fact]
        public void TheLowestSatisfyingVersionWins()
        {
            // A constraint says what the document needs, so honouring it exactly is what
            // makes the same document resolve to the same folder after three more releases.
            PluginResolution resolution = Resolve(
                """{ "requires": [ { "plugin": "P", "version": "^1.0" } ] }""",
                ["1.0.0", "1.1.0", "1.2.0", "2.0.0"]);

            Assert.True(resolution.IsComplete);
            Assert.Equal(new Version(1, 0, 0), Assert.Single(resolution.Plugins).Version);
        }

        [Fact]
        public void TwoEntriesForOnePluginResolveTogetherToOneVersion()
        {
            // A resolution offering two versions of one plugin would name a folder nobody
            // can build, so the constraints on a plugin are met at once or not at all.
            PluginResolution resolution = Resolve(
                """
                { "requires": [ { "plugin": "P", "version": "^1.0" },
                                { "plugin": "P", "version": ">=1.2" } ] }
                """,
                ["1.0.0", "1.1.0", "1.2.0", "1.3.0"]);

            ResolvedPlugin only = Assert.Single(resolution.Plugins);
            Assert.Equal(new Version(1, 2, 0), only.Version);
        }

        [Fact]
        public void APluginNothingPublishesIsNamedRatherThanThrown()
        {
            PluginResolution resolution = PluginResolution.Resolve(
                PluginRequirement.ReadFrom("""{ "requires": [ { "plugin": "Acme.Deploy.Rules" } ] }"""),
                new Dictionary<string, IReadOnlyCollection<Version>>());

            Assert.False(resolution.IsComplete);
            Assert.Empty(resolution.Plugins);

            UnsatisfiedRequirement missing = Assert.Single(resolution.Unsatisfied);
            Assert.Equal(RequirementShortfall.UnknownPlugin, missing.Shortfall);
            Assert.Contains("Acme.Deploy.Rules", missing.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void WhenNothingSatisfiesTheConstraintTheVersionsThatExistAreReported()
        {
            PluginResolution resolution = Resolve(
                """{ "requires": [ { "plugin": "P", "version": "^2.0" } ] }""",
                ["1.0.0", "1.1.0"]);

            UnsatisfiedRequirement missing = Assert.Single(resolution.Unsatisfied);
            Assert.Equal(RequirementShortfall.NoSatisfyingVersion, missing.Shortfall);

            // Projected to text so this compares as a sequence. Assert.Equal of two
            // ImmutableArrays binds to the overload that asks the struct, and that one
            // compares the underlying array by reference.
            Assert.Equal(["1.0.0", "1.1.0"], missing.Published.Select(static version => version.ToString()));
        }

        [Fact]
        public void WhatResolvingChoosesIsWhatTheRuntimeAccepts()
        {
            // The test the whole arrangement exists for. Reversi's requires, resolved against
            // exactly the versions this runtime has loaded, has to come out complete and pick
            // those same versions — because standard.Reversi is that document, compiled by
            // that runtime, and it did not object.
            Dictionary<string, IReadOnlyCollection<Version>> published = standard.Runtime.Plugins
                .ToDictionary(
                    static plugin => plugin.Id,
                    static plugin => (IReadOnlyCollection<Version>)[plugin.Version],
                    StringComparer.OrdinalIgnoreCase);

            PluginResolution resolution = PluginResolution.Resolve(
                PluginRequirement.ReadFrom(StandardRuntime.ReadRuleSet("reversi.json")),
                published);

            Assert.True(resolution.IsComplete);
            Assert.Equal(10, resolution.Plugins.Length);
            Assert.All(
                resolution.Plugins,
                plugin => Assert.Equal(published[plugin.Plugin].Single(), plugin.Version));
        }

        [Fact]
        public void ADocumentThatDrawsAsksForTheVocabularyThatDraws()
        {
            // What a tool assembling a plugin folder has to be told, before there is a runtime
            // and before anything is loaded: a rule set with chance in it names the vocabulary
            // that provides it in `requires`, like any other, and resolving it picks up the
            // draw alongside the rest.
            Dictionary<string, IReadOnlyCollection<Version>> published = standard.Runtime.Plugins
                .ToDictionary(
                    static plugin => plugin.Id,
                    static plugin => (IReadOnlyCollection<Version>)[plugin.Version],
                    StringComparer.OrdinalIgnoreCase);

            PluginResolution resolution = PluginResolution.Resolve(
                PluginRequirement.ReadFrom(StandardRuntime.ReadRuleSet("blackjack.json")),
                published);

            Assert.True(resolution.IsComplete);
            Assert.Contains(resolution.Plugins, plugin => plugin.Plugin == "Rulealize.Plugin.Chance");

            // And the same game without a draw in it asks for one vocabulary less.
            PluginResolution choice = PluginResolution.Resolve(
                PluginRequirement.ReadFrom(StandardRuntime.ReadRuleSet("blackjack-choice.json")),
                published);

            Assert.True(choice.IsComplete);
            Assert.Equal(resolution.Plugins.Length - 1, choice.Plugins.Length);
            Assert.DoesNotContain(choice.Plugins, plugin => plugin.Plugin == "Rulealize.Plugin.Chance");
        }

        [Fact]
        public void ADocumentWhoseVocabularyIsNotAllPublishedResolvesAsFarAsItCan()
        {
            // sample/Deploy names Acme.Deploy.Rules, which is correctly on no feed. That the
            // rest resolves is the useful answer: what is left is what somebody has to supply.
            Dictionary<string, IReadOnlyCollection<Version>> published = standard.Runtime.Plugins
                .ToDictionary(
                    static plugin => plugin.Id,
                    static plugin => (IReadOnlyCollection<Version>)[plugin.Version],
                    StringComparer.OrdinalIgnoreCase);

            PluginResolution resolution = PluginResolution.Resolve(
                PluginRequirement.ReadFrom(StandardRuntime.ReadRuleSet("deploy.json")),
                published);

            Assert.False(resolution.IsComplete);
            Assert.Equal("Acme.Deploy.Rules", Assert.Single(resolution.Unsatisfied).Plugin);
            Assert.NotEmpty(resolution.Plugins);
        }

        private static PluginResolution Resolve(string document, string[] versions) =>
            PluginResolution.Resolve(
                PluginRequirement.ReadFrom(document),
                new Dictionary<string, IReadOnlyCollection<Version>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["P"] = [.. versions.Select(Version.Parse)]
                });
    }
}
