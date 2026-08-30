// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;

namespace Rulealize.Tests
{
    /// <summary>Reading what a rule set needs, and working out what would satisfy it.</summary>
    /// <remarks>
    /// <para>
    /// These answer the questions a tool asks before there is a runtime at all: which
    /// vocabulary this document needs, which documents it holds, and which versions of
    /// either to fetch. The point of them living in this library is that resolving and
    /// running cannot then read <c>^1.0</c> differently.
    /// </para>
    /// <para>
    /// <c>requires</c> is flat and answers in one call. <c>uses</c> is a graph discovered by
    /// fetching, so the walk stays the fetcher's and only the choice at each step is here —
    /// followed by <see cref="RuleSetIdentity"/>, which is how a fetcher checks that what
    /// arrived is what it chose.
    /// </para>
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

        [Fact]
        public void TheLowestSatisfyingVersionWinsForUsesToo()
        {
            // The same rule as above, and through the same code. A restore reproducible for
            // `requires` and not for `uses` would be one command that is half reproducible,
            // which is worse than neither because nothing about it looks wrong.
            ImmutableArray<RuleSetRequirement> uses = RuleSetRequirement.ReadFrom(
                """{ "uses": [ { "ruleSet": "counter", "version": "^1.0" } ] }""");

            Assert.Equal(
                new Version(1, 0, 0),
                RuleSetRequirement.Choose(uses, Published("1.0.0", "1.1.0", "1.2.0", "2.0.0")));
        }

        [Fact]
        public void TwoUsesEntriesForOneRuleSetAreMetTogether()
        {
            // A composite may hold one rule set twice, under two aliases and two constraints.
            // There is still one document to fetch for it, so both are met or neither is.
            ImmutableArray<RuleSetRequirement> uses = RuleSetRequirement.ReadFrom(
                """
                { "uses": [ { "ruleSet": "counter", "version": "^1.0", "as": "left" },
                            { "ruleSet": "counter", "version": ">=1.2", "as": "right" } ] }
                """);

            Assert.Equal(
                new Version(1, 2, 0),
                RuleSetRequirement.Choose(uses, Published("1.0.0", "1.1.0", "1.2.0", "1.3.0")));
        }

        [Fact]
        public void WhenNothingPublishedWillDoThereIsNoVersionRatherThanAShortfall()
        {
            // The caller passed the constraints and the versions, so an empty index and an
            // index with nothing suitable in it are its own to tell apart. That is the whole
            // difference from PluginResolution, and it is why this answers with a version.
            ImmutableArray<RuleSetRequirement> uses = RuleSetRequirement.ReadFrom(
                """{ "uses": [ { "ruleSet": "counter", "version": "^2.0" } ] }""");

            Assert.Null(RuleSetRequirement.Choose(uses, Published("1.0.0", "1.9.9")));
            Assert.Null(RuleSetRequirement.Choose(uses, []));
        }

        [Fact]
        public void ConstraintsOnTwoRuleSetsAreNotOneQuestion()
        {
            // Meeting them at once would answer about a version nothing asked for. The caller
            // walked the graph that produced them, so grouping them is its job.
            ImmutableArray<RuleSetRequirement> uses = RuleSetRequirement.ReadFrom(
                """{ "uses": [ { "ruleSet": "counter" }, { "ruleSet": "clock" } ] }""");

            Assert.Throws<ArgumentException>(
                () => RuleSetRequirement.Choose(uses, Published("1.0.0")));
        }

        [Fact]
        public void ADocumentSaysWhatItIsWithoutBeingCompiled()
        {
            // The other end of a fetch. This document does not compile — it holds one whose
            // document is not here — and it still answers what it is, which is the point: an
            // index answered about a package, the runtime will read the document, and
            // somebody has to be able to compare the two.
            RuleSetIdentity identity = RuleSetIdentity.ReadFrom("""
                { "id": "counter", "version": "1.2.0", "uses": [ { "ruleSet": "nowhere" } ] }
                """);

            Assert.Equal("counter", identity.Id);
            Assert.Equal("1.2.0", identity.Version);
            Assert.Equal("counter@1.2.0", identity.RuleSet);
        }

        [Fact]
        public void WhatChoosingPicksIsWhatCompilingAccepts()
        {
            // The test the arrangement exists for, one requirement over. The version chosen
            // against an index is the version the document declares, the document that
            // declares it satisfies the entry that asked for it, and CreateContext agrees.
            RuleSetRequirement entry = Assert.Single(RuleSetRequirement.ReadFrom(Composite));

            Version? chosen = RuleSetRequirement.Choose([entry], Published("1.0.0", "1.2.0", "2.0.0"));
            Assert.Equal(new Version(1, 2, 0), chosen);

            RuleSetIdentity identity = RuleSetIdentity.ReadFrom(Component);
            Assert.Equal(chosen!.ToString(), identity.Version);
            Assert.True(identity.Satisfies(entry));

            RuleContext context = standard.Runtime.CreateContext(Composite, Held(Component));
            Assert.Equal("tally@1.0.0", context.RuleSet);
        }

        [Fact]
        public void AnIdentityThatFallsShortIsTheDocumentCreateContextRefuses()
        {
            // And the other way. What this catches is a document fetched under a version it
            // does not itself declare, which a fetcher trusting an index has no way to see
            // until it compiles — by which point it has assembled the whole set.
            RuleSetRequirement entry = Assert.Single(RuleSetRequirement.ReadFrom(Composite));
            string stale = Component.Replace(
                "\"version\": \"1.2.0\"", "\"version\": \"1.0.0\"", StringComparison.Ordinal);

            Assert.False(RuleSetIdentity.ReadFrom(stale).Satisfies(entry));
            Assert.Throws<Abstraction.RuleSetBuildException>(
                () => standard.Runtime.CreateContext(Composite, Held(stale)));
        }

        [Fact]
        public void AVersionThatSatisfiesSaysNothingAboutWhoseItIs()
        {
            // Both halves are checked, because either alone is answered too easily.
            RuleSetRequirement entry = Assert.Single(RuleSetRequirement.ReadFrom(Composite));

            Assert.True(entry.IsSatisfiedBy(new Version(1, 2, 0)));
            Assert.False(
                RuleSetIdentity.ReadFrom("""{ "id": "clock", "version": "1.2.0" }""").Satisfies(entry));
        }

        /// <summary>A component, published at 1.2.0.</summary>
        private const string Component = $$"""
            {
              "id": "counter", "version": "1.2.0",
            {{StandardRuntime.Requires}}
              "state": {
                "schema": { "n": { "op": "type.int", "min": 0 } },
                "initial": { "n": 0 }
              },
              "inputs": {
                "bump": {
                  "effects": [
                    { "op": "state.set", "path": "n",
                      "value": { "op": "math.add", "of": ["$n", 1] } } ]
                }
              }
            }
            """;

        /// <summary>A composite that holds it, and says which versions of it will do.</summary>
        private const string Composite = $$"""
            {
              "id": "tally", "version": "1.0.0",
            {{StandardRuntime.Requires}}
              "uses": [ { "ruleSet": "counter", "version": "^1.1" } ]
            }
            """;

        private static Dictionary<string, string> Held(string component) =>
            new(StringComparer.Ordinal) { ["counter"] = component };

        private static ImmutableArray<Version> Published(params string[] versions) =>
            [.. versions.Select(Version.Parse)];

        private static PluginResolution Resolve(string document, string[] versions) =>
            PluginResolution.Resolve(
                PluginRequirement.ReadFrom(document),
                new Dictionary<string, IReadOnlyCollection<Version>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["P"] = [.. versions.Select(Version.Parse)]
                });
    }
}
