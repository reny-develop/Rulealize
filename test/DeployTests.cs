// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction;
using Rulealize.Abstraction.Plugin;
using Rulealize.Sample.Deploy;

namespace Rulealize.Tests
{
    /// <summary>A vocabulary that is not a plugin assembly, and a rule set written against it.</summary>
    /// <remarks>
    /// <para>
    /// Everything else in this suite is written against the standard plugins, found
    /// by scanning a folder. That is the right picture of a deployment and the wrong picture
    /// of a library: a project holding its own business rules will have operations worth
    /// writing and not worth publishing, and reaches them by implementing
    /// <c>IRulealizePlugin</c> in its own assembly and calling <c>AddPlugin</c>.
    /// </para>
    /// <para>
    /// That path was already reachable and already half tested — <c>PluginLoadingTests</c>
    /// hands <c>AddPlugin</c> stubs, but only to watch the manifest claims collide. Nothing
    /// established that a vocabulary arriving that way compiles a rule set and evaluates it.
    /// These tests are that, and they are written against the two things the folder-scanned
    /// route cannot do: an operation carrying constructor-injected data, and a host naming
    /// the plugin's type.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class DeployTests(StandardRuntime standard)
    {
        private RuleContext Pipeline => standard.Deploy;

        /// <summary>A Friday. The freeze calendar's only quarrel with the initial state.</summary>
        private string OnFriday => Pipeline.InitialState.Replace("2026-08-12", "2026-08-14", StringComparison.Ordinal);

        [Fact]
        public void ALocalVocabularyIsAnOrdinaryEntryInTheTable()
        {
            PluginManifest acme = Assert.Single(
                standard.DeployRuntime.Plugins, manifest => manifest.Id == "Acme.Deploy.Rules");

            Assert.Equal("acme", acme.Namespace);

            // The convention for a vocabulary with an audience of one. There is a single
            // character per plugin and only a handful that can ever be used, so the private
            // ones leave them for the published ones.
            Assert.Null(acme.ReservedPrefix);
        }

        [Fact]
        public void TheRuleSetStillDeclaresWhatItDrawsOn()
        {
            // Compiled on a runtime holding only the twelve. `requires` is what makes an
            // unpublished vocabulary a declared dependency rather than a private
            // arrangement, and this is the failure that proves it.
            RuleSetBuildException rejected = Assert.Throws<RuleSetBuildException>(
                () => standard.Runtime.CreateContext(StandardRuntime.ReadRuleSet("deploy.json")));

            Assert.Contains("Acme.Deploy.Rules", rejected.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void APrereleaseDoesNotSupersedeTheReleaseItLeadsTo()
        {
            // billing's first stage holds 2.4.0-rc.1, and three builds are in flight.
            string[] offered = Deployable("billing");

            // Ordering the text would answer this the other way round — "2.4.0" sorts before
            // "2.4.0-rc.1" — and would leave a release candidate sitting on top of a release.
            // No arrangement of the standard vocabulary gets it right, which is why
            // acme.newer exists.
            Assert.Equal(["2.4.0"], offered);
        }

        [Fact]
        public void AStageWithNothingOnItTakesAnything() =>
            Assert.Equal(["2.3.5", "2.4.0", "2.4.0-rc.1"], Deployable("web").Order(StringComparer.Ordinal));

        [Fact]
        public void TheApproverDomainIsNarrowedByTheOwnershipMap()
        {
            // `by` ranges over everyone the map names — the domain cannot depend on another
            // parameter — and the guard is what cuts it down to the people who own the
            // service. Both halves come from data the rule set has never seen.
            Assert.Equal(["ann", "bo", "cy"], Signatories("billing"));
            Assert.Equal(["bo", "di"], Signatories("web"));
        }

        [Fact]
        public void AFreezeIsTheOnlyThingBetweenSearchAndProduction()
        {
            Assert.Contains(Promotions(Pipeline.InitialState), move => move == "search prod");

            // Same document, one field different, and the reason it is now refused is in
            // neither the rule set nor the state.
            Assert.DoesNotContain(Promotions(OnFriday), move => move == "search prod");
        }

        [Fact]
        public void AFrozenPipelineWithNothingLeftToDoIsBlocked()
        {
            string frozen = Exhaust(OnFriday);

            TerminalStatus status = Pipeline.GetTerminalStatus(frozen);

            Assert.True(status.IsTerminal);
            Assert.Equal("blocked", status.Result);
        }

        [Fact]
        public void AnUnfrozenPipelineShips()
        {
            string finished = Exhaust(Pipeline.InitialState);

            Assert.Equal("shipped", Pipeline.GetTerminalStatus(finished).Result);
        }

        [Fact]
        public void ASignatureTravelsWithTheBuildItWasGivenTo()
        {
            // billing needs two signatures to reach prod, and they are collected while the
            // build is still on dev. Approvals are keyed on the artifact, so promoting twice
            // does not lose them.
            string state = Apply(Pipeline.InitialState, "approve", ("service", "billing"), ("stage", "dev"), ("by", "ann"));
            state = Apply(state, "approve", ("service", "billing"), ("stage", "dev"), ("by", "bo"));
            state = Apply(state, "promote", ("service", "billing"), ("to", "staging"));

            Assert.Contains(Promotions(state), move => move == "billing prod");
        }

        [Fact]
        public void TheSamePipelineUnderADifferentPolicyIsADifferentSetOfMoves()
        {
            // The axis a folder-scanned plugin does not have. Nothing about the rule set or
            // the state changes; the vocabulary is constructed with other tables.
            RuleContext lockdown = new RuleRuntime()
                .LoadPluginsFrom(StandardRuntime.PluginFolder)
                .AddPlugin(new DeployVocabulary(new DeployPolicy(
                    [DayOfWeek.Saturday, DayOfWeek.Sunday],
                    [new FreezeWindow(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "incident lockdown")],
                    new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                    {
                        ["billing"] = ["ann"],
                        ["search"] = ["ann"],
                        ["web"] = ["ann"],
                    })))
                .CreateContext(StandardRuntime.ReadRuleSet("deploy.json"));

            ValidInputSet moves = lockdown.GetValidInputs(lockdown.InitialState, 300);

            // August is closed, so search cannot go out on the Wednesday that the standard
            // policy was happy with.
            Assert.DoesNotContain(moves, move => move.Input == "promote" && move.Arguments["to"] == "prod");

            // And a smaller crew is a smaller candidate space, not merely a smaller answer:
            // `by` gets its domain from the same map.
            Assert.True(
                moves.Evaluated < Pipeline.GetValidInputs(Pipeline.InitialState, 300).Evaluated,
                "a one-person ownership map should shrink the domain approve ranges over");
        }

        [Fact]
        public void ThereIsNoActorAnywhereInIt() =>
            Assert.All(Pipeline.GetValidInputs(Pipeline.InitialState, 300), static move => Assert.Null(move.Actor));

        [Fact]
        public void ThePolicyDocumentsTheSampleShipsAreReadable()
        {
            DeployPolicy policy = DeployPolicy.Read(
                Path.Combine(AppContext.BaseDirectory, "Policy", "acme.json"));

            Assert.True(policy.IsFrozen(new DateOnly(2026, 8, 14)));       // a Friday
            Assert.False(policy.IsFrozen(new DateOnly(2026, 8, 12)));      // the Wednesday before it
            Assert.True(policy.IsFrozen(new DateOnly(2026, 12, 25)));      // inside the window
            // Materialized, because an ImmutableArray compared against an array takes the
            // single-value overload and fails on the types rather than the contents.
            Assert.Equal(["ann", "bo", "cy", "di"], policy.People.ToArray());
            Assert.Equal(["bo", "di"], policy.ApproversOf("web").ToArray());

            // A service the map has not caught up with is one nobody can approve, rather
            // than an exception out of the middle of a guard.
            Assert.Empty(policy.ApproversOf("nothing-owns-this"));
        }

        // ── The readable view ──────────────────────────────────────────────────────

        /// <summary>The order arguments read back in, which is the rule set's and nothing else's.</summary>
        /// <remarks>
        /// Everything else in this suite reads an argument by name, and by name a wrong order
        /// is invisible. It is not invisible to a host that renders a move and matches its own
        /// rendering back — which is what a command line does — and that is what this pins.
        /// <c>Arguments</c> was an <c>ImmutableDictionary</c> once, so it enumerated in hash
        /// order, and .NET reseeds string hashing per process: the same move came out
        /// <c>deploy(service: …, version: …)</c> in one run and the other way round in the
        /// next. Two parameters made it a coin flip and one parameter hid it entirely, which
        /// is why the rule sets with a single argument caught nothing.
        /// </remarks>
        [Fact]
        public void ArgumentsReadBackInDeclaredParameterOrder()
        {
            ValidInput deploy = Legal(Pipeline.InitialState).First(static move => move.Input == "deploy");
            ValidInput approve = Legal(Pipeline.InitialState).First(static move => move.Input == "approve");

            Assert.Equal(["service", "version"], deploy.Arguments.Keys);
            Assert.Equal(["service", "stage", "by"], approve.Arguments.Keys);

            // Positions and names are two ways at one thing, not two things.
            Assert.Equal("service", deploy.Arguments[0].Key);
            Assert.Equal(deploy.Arguments["version"], deploy.Arguments[1].Value);
        }

        [Fact]
        public void TheTextFormFollowsThatOrder()
        {
            ValidInput move = Legal(Pipeline.InitialState).First(static m => m.Input == "approve");

            Assert.Equal(
                $"approve(service: {move.Arguments["service"]}, "
                    + $"stage: {move.Arguments["stage"]}, by: {move.Arguments["by"]})",
                move.ToString());
        }

        [Fact]
        public void AParameterTheInputDoesNotDeclareIsNotThere()
        {
            ValidInput move = Legal(Pipeline.InitialState).First(static m => m.Input == "deploy");

            Assert.False(move.Arguments.ContainsKey("stage"));
            Assert.False(move.Arguments.TryGetValue("stage", out _));
            Assert.Throws<KeyNotFoundException>(() => _ = move.Arguments["stage"]);
        }

        // ── Reading the answer ─────────────────────────────────────────────────────

        private string[] Deployable(string service) =>
        [
            .. Legal(Pipeline.InitialState)
                .Where(move => move.Input == "deploy" && move.Arguments["service"] == service)
                .Select(static move => move.Arguments["version"])
                .Order(StringComparer.Ordinal),
        ];

        private string[] Signatories(string service) =>
        [
            .. Legal(Pipeline.InitialState)
                .Where(move => move.Input == "approve" && move.Arguments["service"] == service)
                .Select(static move => move.Arguments["by"])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        private string[] Promotions(string state) =>
        [
            .. Legal(state)
                .Where(static move => move.Input == "promote")
                .Select(static move => $"{move.Arguments["service"]} {move.Arguments["to"]}"),
        ];

        private ValidInputSet Legal(string state) => Pipeline.GetValidInputs(state, 300);

        // ── Driving it ─────────────────────────────────────────────────────────────

        /// <summary>Applies legal inputs, promotions first, until nothing is left.</summary>
        /// <remarks>
        /// Not a search. Every input this rule set offers either moves the pipeline forward
        /// or is harmless, so there is nothing to back out of — which is what makes
        /// <c>blocked</c> worth asserting: reaching it means the pipeline really had nowhere
        /// to go, not that a greedy walk took a wrong turn.
        /// </remarks>
        private string Exhaust(string from)
        {
            string current = from;

            for (int step = 0; step < 100; step++)
            {
                if (Pipeline.GetTerminalStatus(current).IsTerminal)
                {
                    return current;
                }

                ValidInputSet legal = Legal(current);
                ValidInput next = legal.FirstOrDefault(static move => move.Input == "promote")
                    ?? legal.FirstOrDefault(static move => move.Input == "deploy")
                    ?? legal.First();

                current = Pipeline.ApplyToState(next.ToInputDocument(Pipeline.RuleSet), current).State;
            }

            Assert.Fail("the pipeline did not settle within a hundred steps.");
            return current;
        }

        private string Apply(string state, string input, params (string Key, string Value)[] arguments) =>
            Pipeline.ApplyToState(
                $$"""
                { "$schema": "rulealize/input/v1", "ruleSet": "{{Pipeline.RuleSet}}", "input": "{{input}}",
                  "args": { {{string.Join(", ", arguments.Select(static a => $"\"{a.Key}\": \"{a.Value}\""))}} } }
                """,
                state).State;
    }
}
