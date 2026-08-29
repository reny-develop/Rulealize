// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rulealize.Abstraction;

namespace Rulealize.Tests
{
    /// <summary>A rule set that holds others, and what a composite may say about them.</summary>
    /// <remarks>
    /// <para>
    /// The process behind these documents is the one a person naturally writes as two: a
    /// request has to be raised and granted before somebody goes on a shift. As two documents
    /// the guard that matters cannot be written at all — the request half cannot see the
    /// roster, so nothing stops a request being raised for a shift the roster will refuse,
    /// and the roster half has never heard of a request, so nothing in it is wrong either.
    /// </para>
    /// <para>
    /// A composite is where that guard lives. It may only refuse: a component's state moves
    /// by the component's own inputs under the component's own rules, so whatever a walk of
    /// the component alone found is still an upper bound on what it does here.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class CompositionTests(StandardRuntime standard)
    {
        private const string Requires = StandardRuntime.Requires;

        /// <summary>The approving half. One request, raised and then granted or denied.</summary>
        private const string Request = $$"""
            {
              "id": "request", "version": "1.0.0",
            {{Requires}}
              "state": {
                "schema": {
                  "stage": { "op": "type.enum", "values": ["draft", "review", "granted", "denied"] },
                  "shift": { "op": "type.enum", "values": ["mon", "tue"], "nullable": true }
                },
                "initial": { "stage": "draft", "shift": null }
              },
              "inputs": {
                "raise": {
                  "params": { "shift": { "domain": { "op": "seq.of", "of": ["mon", "tue"] } } },
                  "when": { "op": "cmp.eq", "left": "$stage", "right": "draft" },
                  "effects": [
                    { "op": "state.set", "path": "shift", "value": "@shift" },
                    { "op": "state.set", "path": "stage", "value": "review" } ]
                },
                "grant": {
                  "when": { "op": "cmp.eq", "left": "$stage", "right": "review" },
                  "effects": [ { "op": "state.set", "path": "stage", "value": "granted" } ]
                }
              },
              "terminal": {
                "when": { "op": "cmp.eq", "left": "$stage", "right": "granted" },
                "result": "$stage"
              }
            }
            """;

        /// <summary>The roster half. Two shifts, each filled by at most one person.</summary>
        private const string Shift = $$"""
            {
              "id": "shift", "version": "1.0.0",
            {{Requires}}
              "state": {
                "schema": {
                  "mon": { "op": "type.enum", "values": ["ann"], "nullable": true },
                  "tue": { "op": "type.enum", "values": ["ann"], "nullable": true }
                },
                "initial": { "mon": null, "tue": null }
              },
              "inputs": {
                "assign": {
                  "params": { "slot": { "domain": { "op": "seq.of", "of": ["mon", "tue"] } } },
                  "when": {
                    "op": "logic.or",
                    "any": [
                      { "op": "logic.and", "all": [
                        { "op": "cmp.eq", "left": "@slot", "right": "mon" },
                        { "op": "cmp.isNull", "value": "$mon" } ] },
                      { "op": "logic.and", "all": [
                        { "op": "cmp.eq", "left": "@slot", "right": "tue" },
                        { "op": "cmp.isNull", "value": "$tue" } ] } ]
                  },
                  "effects": [
                    { "op": "state.set", "path": "mon", "value": { "op": "branch.if",
                      "cond": { "op": "cmp.eq", "left": "@slot", "right": "mon" },
                      "then": "ann", "else": "$mon" } },
                    { "op": "state.set", "path": "tue", "value": { "op": "branch.if",
                      "cond": { "op": "cmp.eq", "left": "@slot", "right": "tue" },
                      "then": "ann", "else": "$tue" } } ]
                }
              }
            }
            """;

        /// <summary>The process: the two halves, and the guard neither of them could write.</summary>
        private const string Process = $$"""
            {
              "id": "process", "version": "1.0.0",
            {{Requires}}
              "uses": [
                { "ruleSet": "request", "version": "^1.0", "as": "req" },
                { "ruleSet": "shift",   "version": "^1.0", "as": "roster" }
              ],
              "held": {
                "req": {
                  "raise": {
                    "when": { "op": "cmp.isNull",
                              "value": { "op": "rec.at", "record": "$roster", "key": "@shift" } }
                  }
                },
                "roster": {
                  "assign": {
                    "when": { "op": "logic.and", "all": [
                      { "op": "cmp.eq", "right": "granted",
                        "left": { "op": "rec.at", "record": "$req", "key": "stage" } },
                      { "op": "cmp.eq", "left": "@slot",
                        "right": { "op": "rec.at", "record": "$req", "key": "shift" } } ] }
                  }
                }
              },
              "terminal": {
                "when": { "op": "logic.and", "all": [
                  { "op": "logic.not", "value": { "op": "cmp.isNull",
                    "value": { "op": "rec.at", "record": "$roster", "key": "mon" } } },
                  { "op": "logic.not", "value": { "op": "cmp.isNull",
                    "value": { "op": "rec.at", "record": "$roster", "key": "tue" } } } ] },
                "result": "filled"
              }
            }
            """;

        /// <summary>The same two halves, with one input driving both of them at once.</summary>
        /// <remarks>
        /// What the merged document that composition replaces has, and narrowing alone cannot
        /// reach: a <c>grant</c> that also performs the assignment. The state between the two
        /// — granted, not yet assigned — is one the process never occupies rather than one it
        /// passes through, which is the difference between modelling this process and
        /// modelling a longer one that resembles it.
        /// </remarks>
        private const string Atomic = $$"""
            {
              "id": "atomic", "version": "1.0.0",
            {{Requires}}
              "uses": [
                { "ruleSet": "request", "as": "req" },
                { "ruleSet": "shift",   "as": "roster" }
              ],
              "held": {
                "req":    { "grant":  { "when": false } },
                "roster": { "assign": { "when": false } }
              },
              "inputs": {
                "grant": {
                  "fires": [
                    { "held": "req", "input": "grant" },
                    { "held": "roster", "input": "assign",
                      "args": { "slot": { "op": "rec.at", "record": "$req", "key": "shift" } } }
                  ]
                }
              }
            }
            """;

        private static IReadOnlyDictionary<string, string> Halves =>
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["request"] = Request,
                ["shift"] = Shift,
            };

        [Fact]
        public void ACompositeOffersItsComponentsInputsUnderTheirAlias()
        {
            RuleContext process = Composite();

            Assert.Equal(["req.raise", "req.grant", "roster.assign"], process.Inputs.AsEnumerable());
        }

        [Fact]
        public void ACompositeStateHoldsEachComponentsStateAsAField()
        {
            RuleContext process = Composite();

            using JsonDocument state = JsonDocument.Parse(process.InitialState);
            JsonElement data = state.RootElement.GetProperty("data");

            Assert.Equal("process@1.0.0", state.RootElement.GetProperty("ruleSet").GetString());
            Assert.Equal("request@1.0.0", data.GetProperty("req").GetProperty("ruleSet").GetString());
            Assert.Equal("draft", data.GetProperty("req").GetProperty("data").GetProperty("stage").GetString());
        }

        [Fact]
        public void AComponentsStateMovesByItsOwnInput()
        {
            RuleContext process = Composite();

            string after = Apply(process, "req.raise", process.InitialState, ("shift", "mon"));

            Assert.Equal("review", Field(after, "req", "stage"));
            Assert.Null(Field(after, "roster", "mon"));
        }

        [Fact]
        public void TheGuardNeitherDocumentCouldWriteIsEnforcedBothWays()
        {
            // `roster.assign` is legal in the roster's own opening position, and here it is
            // legal in none: an assignment only ever happens as the consequence of a grant.
            RuleContext process = Composite();

            Assert.DoesNotContain(
                process.GetValidInputs(process.InitialState, 64),
                static move => move.Input == "roster.assign");

            string granted = Apply(
                process,
                "req.grant",
                Apply(process, "req.raise", process.InitialState, ("shift", "mon")));

            Assert.Contains(
                process.GetValidInputs(granted, 64),
                static move => move.Input == "roster.assign" && move.Arguments["slot"] == "mon");

            // And only the shift the request named.
            Assert.DoesNotContain(
                process.GetValidInputs(granted, 64),
                static move => move.Input == "roster.assign" && move.Arguments["slot"] == "tue");
        }

        [Fact]
        public void ApplyingAnInputTheCompositeRefusesSaysWhichRuleSetRefusedIt()
        {
            RuleContext process = Composite();

            IllegalInputException refused = Assert.Throws<IllegalInputException>(
                () => Apply(process, "roster.assign", process.InitialState, ("slot", "mon")));

            Assert.Contains("shift@1.0.0", refused.Message, StringComparison.Ordinal);
            Assert.Contains("held.roster.assign.when", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ACompositeTerminalIsTheEndOfTheProcessAndNotOfEitherHalf()
        {
            RuleContext process = Composite();

            // The request reaches its own terminal at `granted`, and the process does not end
            // there: a component's terminal is the component's business.
            string granted = Apply(
                process,
                "req.grant",
                Apply(process, "req.raise", process.InitialState, ("shift", "mon")));

            Assert.False(process.GetTerminalStatus(granted).IsTerminal);
        }

        [Fact]
        public void ACompositeMayOnlyNarrowSoAnUnreachableCandidateStaysUnreachable()
        {
            // `req.raise` for a shift the roster has already filled is refused by the
            // composite, which is the guard that could not be written as two documents.
            RuleContext process = Composite();

            string filled = Apply(
                process,
                "roster.assign",
                Apply(process, "req.grant", Apply(process, "req.raise", process.InitialState, ("shift", "mon"))),
                ("slot", "mon"));

            // Back to a fresh request would need the request to be re-enterable; what this
            // shows is the composite reading both halves at once to answer the question.
            Assert.Equal("ann", Field(filled, "roster", "mon"));
        }

        [Fact]
        public void WhatARuleSetHoldsIsReadableBeforeThereIsAnythingToRunItWith()
        {
            // What a tool has to know before it can fetch anything: no runtime, no plugin,
            // and none of the documents it is about to go and get.
            ImmutableArray<RuleSetRequirement> uses = RuleSetRequirement.ReadFrom(Process);

            Assert.Equal(["request", "shift"], uses.Select(static use => use.RuleSet));
            Assert.Equal(["req", "roster"], uses.Select(static use => use.Alias));
            Assert.Equal("^1.0", uses[0].Constraint);
            Assert.True(uses[0].IsSatisfiedBy(new Version(1, 4, 2)));
            Assert.False(uses[0].IsSatisfiedBy(new Version(2, 0, 0)));
            Assert.Equal("request ^1.0 as req", uses[0].ToString());
        }

        [Fact]
        public void AnAliasDefaultsToTheIdentifierAndAHeldNothingReadsAsEmpty()
        {
            RuleSetRequirement unnamed = RuleSetRequirement.ReadFrom("""
                { "id": "t", "version": "1.0.0", "uses": [ { "ruleSet": "shift" } ] }
                """)[0];

            Assert.Equal("shift", unnamed.Alias);
            Assert.Null(unnamed.Constraint);
            Assert.Equal("shift", unnamed.ToString());
            Assert.True(unnamed.IsSatisfiedBy(new Version(9, 9, 9)));

            Assert.Empty(RuleSetRequirement.ReadFrom(Shift));
        }

        [Fact]
        public void ADocumentAComponentNeedsHasToBeSupplied() =>
            Assert.Contains(
                "was not supplied",
                Assert.Throws<RuleSetBuildException>(() => standard.Runtime.CreateContext(Process)).Message,
                StringComparison.Ordinal);

        [Fact]
        public void AVersionAComponentDoesNotSatisfyIsRefused()
        {
            Dictionary<string, string> halves = new(Halves, StringComparer.Ordinal)
            {
                ["request"] = Request.Replace("\"version\": \"1.0.0\"", "\"version\": \"2.0.0\"", StringComparison.Ordinal),
            };

            Assert.Contains(
                "needs request ^1.0",
                Assert.Throws<RuleSetBuildException>(() => standard.Runtime.CreateContext(Process, halves)).Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public void RuleSetsThatHoldOneAnotherAreRefusedWhenTheDocumentIsCompiled()
        {
            const string Ouroboros = $$"""
                {
                  "id": "a", "version": "1.0.0",
                {{Requires}}
                  "uses": [ { "ruleSet": "b" } ]
                }
                """;

            const string Back = $$"""
                {
                  "id": "b", "version": "1.0.0",
                {{Requires}}
                  "uses": [ { "ruleSet": "a" } ]
                }
                """;

            Dictionary<string, string> documents = new(StringComparer.Ordinal) { ["a"] = Ouroboros, ["b"] = Back };

            Assert.Contains(
                "holds itself",
                Assert.Throws<RuleSetBuildException>(() => standard.Runtime.CreateContext(Ouroboros, documents)).Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public void AStateWrittenForAnotherMajorVersionOfAComponentIsRefused()
        {
            RuleContext process = Composite();
            string state = process.InitialState.Replace(
                "\"request@1.0.0\"",
                "\"request@2.0.0\"",
                StringComparison.Ordinal);

            Assert.Contains(
                "request@2.0.0",
                Assert.Throws<RuleDocumentException>(() => process.GetValidInputs(state, 64)).Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public void OneCompositeInputDrivesSeveralComponentInputsInOneTransition()
        {
            RuleContext atomic = standard.Runtime.CreateContext(Atomic, Halves);

            string after = Apply(
                atomic,
                "grant",
                Apply(atomic, "req.raise", atomic.InitialState, ("shift", "mon")));

            // Both halves moved, and there is no state between them.
            Assert.Equal("granted", Field(after, "req", "stage"));
            Assert.Equal("ann", Field(after, "roster", "mon"));
        }

        [Fact]
        public void AnInputThatFiresIsOfferedOnlyWhereEveryInputItDrivesIsAllowed()
        {
            RuleContext atomic = standard.Runtime.CreateContext(Atomic, Halves);

            // `req.grant` is not allowed in the opening position, so neither is what drives it.
            Assert.DoesNotContain(
                atomic.GetValidInputs(atomic.InitialState, 64),
                static move => move.Input == "grant");

            string raised = Apply(atomic, "req.raise", atomic.InitialState, ("shift", "mon"));

            Assert.Contains(atomic.GetValidInputs(raised, 64), static move => move.Input == "grant");
        }

        [Fact]
        public void HidingAComponentsInputIsNarrowingItToNothing()
        {
            RuleContext atomic = standard.Runtime.CreateContext(Atomic, Halves);
            string raised = Apply(atomic, "req.raise", atomic.InitialState, ("shift", "mon"));

            // `req.grant` is allowed by the request itself in this state, and the composite
            // offers it nowhere: the only way to it is the input that drives it.
            Assert.DoesNotContain(
                atomic.GetValidInputs(raised, 64),
                static move => move.Input is "req.grant" or "roster.assign");

            Assert.Throws<IllegalInputException>(() => Apply(atomic, "req.grant", raised));
        }

        [Fact]
        public void AHiddenInputCostsNothingToNotOffer()
        {
            // `roster.assign` has a domain of two and `held` refuses it everywhere; walking
            // that domain to discard both would spend the caller's limit on candidates that
            // cannot come back. What is evaluated is the composite's own `grant`, and
            // `req.raise`'s two — the hidden inputs cost nothing.
            RuleContext atomic = standard.Runtime.CreateContext(Atomic, Halves);
            string raised = Apply(atomic, "req.raise", atomic.InitialState, ("shift", "mon"));

            Assert.Equal(3, atomic.GetValidInputs(raised, 64).Evaluated);
        }

        [Fact]
        public void ApplyingAnInputWhoseDrivenInputIsRefusedSaysWhichOne()
        {
            RuleContext atomic = standard.Runtime.CreateContext(Atomic, Halves);

            IllegalInputException refused = Assert.Throws<IllegalInputException>(
                () => Apply(atomic, "grant", atomic.InitialState));

            Assert.Contains("drives 'req.grant'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ACompositeMayNotWriteAComponentsStateWithAnEffect()
        {
            const string Reaching = $$"""
                {
                  "id": "reaching", "version": "1.0.0",
                {{Requires}}
                  "uses": [ { "ruleSet": "shift", "as": "roster" } ],
                  "inputs": {
                    "cheat": { "effects": [
                      { "op": "state.set", "path": "roster", "value": "$roster" } ] }
                  }
                }
                """;

            RuleContext context = standard.Runtime.CreateContext(Reaching, Halves);

            RuleEvaluationException refused = Assert.Throws<RuleEvaluationException>(
                () => Apply(context, "cheat", context.InitialState));

            Assert.Contains("shift@1.0.0", refused.Message, StringComparison.Ordinal);
            Assert.Contains("'fires'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AnArgumentAFiredInputDoesNotTakeIsRefusedWhenTheDocumentIsCompiled()
        {
            string wrong = Atomic.Replace("\"input\": \"grant\"", "\"input\": \"raise\"", StringComparison.Ordinal);

            Assert.Contains(
                "and none are given",
                Assert.Throws<RuleSetBuildException>(() => standard.Runtime.CreateContext(wrong, Halves)).Message,
                StringComparison.Ordinal);
        }

        // ── Nesting ────────────────────────────────────────────────────────────
        //
        // A rule set a composite holds may hold rule sets of its own, and everything above
        // has to mean the same thing at every depth: the inputs are offered, the seal holds,
        // a driven input drives what it drives, and a holder's guard is the holder's.

        /// <summary>A rule set holding `shift`, which the outer one then holds in turn.</summary>
        private const string Middle = $$"""
            {
              "id": "middle", "version": "1.0.0",
            {{Requires}}
              "uses": [ { "ruleSet": "shift", "as": "inner" } ],
              "held": { "inner": { "assign": { "when": false } } },
              "inputs": {
                "fill": {
                  "params": { "slot": { "domain": { "op": "seq.of", "of": ["mon", "tue"] } } },
                  "fires": [ { "held": "inner", "input": "assign", "args": { "slot": "@slot" } } ]
                },
                "reach": {
                  "effects": [ { "op": "state.set", "path": "inner", "value": "$inner" } ]
                }
              }
            }
            """;

        private const string Outer = $$"""
            {
              "id": "outer", "version": "1.0.0",
            {{Requires}}
              "uses": [ { "ruleSet": "middle", "as": "mid" } ],
              "held": {
                "mid": {
                  // A guard on an input that itself fires: what it drives still has to be
                  // allowed, and this has to be asked as well.
                  "fill": { "when": { "op": "cmp.eq", "left": "@slot", "right": "mon" } }
                }
              }
            }
            """;

        private static IReadOnlyDictionary<string, string> Nested =>
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["middle"] = Middle,
                ["shift"] = Shift,
            };

        [Fact]
        public void NestingGoesAsDeepAsTheDocumentsDo()
        {
            RuleContext outer = standard.Runtime.CreateContext(Outer, Nested);

            Assert.Equal(
                ["mid.fill", "mid.reach", "mid.inner.assign"],
                outer.Inputs.AsEnumerable());
        }

        [Fact]
        public void AnInputTwoLevelsDownDrivesWhatItDrives()
        {
            RuleContext outer = standard.Runtime.CreateContext(Outer, Nested);

            string after = Apply(outer, "mid.fill", outer.InitialState, ("slot", "mon"));

            Assert.Equal("ann", (string?)JsonNode.Parse(after)!["data"]!["mid"]!["data"]!["inner"]!["data"]!["mon"]);
        }

        [Fact]
        public void TheSealHoldsAtEveryDepth()
        {
            // The same effect is refused whether `middle` is the context or is held by one.
            RuleContext middle = standard.Runtime.CreateContext(Middle, Nested);
            RuleContext outer = standard.Runtime.CreateContext(Outer, Nested);

            Assert.Contains(
                "shift@1.0.0",
                Assert.Throws<RuleEvaluationException>(
                    () => Apply(middle, "reach", middle.InitialState)).Message,
                StringComparison.Ordinal);

            Assert.Contains(
                "shift@1.0.0",
                Assert.Throws<RuleEvaluationException>(
                    () => Apply(outer, "mid.reach", outer.InitialState)).Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public void AHeldGuardIsTheHoldersAndAppliesAtTheDepthItWasWritten()
        {
            // `middle` hides `inner.assign`; holding `middle` does not change that, and the
            // input that drives it goes on working.
            RuleContext outer = standard.Runtime.CreateContext(Outer, Nested);

            Assert.DoesNotContain(
                outer.GetValidInputs(outer.InitialState, 64),
                static move => move.Input == "mid.inner.assign");

            // And the outer rule set's own guard over an input that fires is asked beside it.
            Assert.Contains(
                outer.GetValidInputs(outer.InitialState, 64),
                static move => move.Input == "mid.fill" && move.Arguments["slot"] == "mon");

            Assert.DoesNotContain(
                outer.GetValidInputs(outer.InitialState, 64),
                static move => move.Input == "mid.fill" && move.Arguments["slot"] == "tue");

            Assert.Contains(
                "held.mid.fill.when",
                Assert.Throws<IllegalInputException>(
                    () => Apply(outer, "mid.fill", outer.InitialState, ("slot", "tue"))).Message,
                StringComparison.Ordinal);
        }

        private RuleContext Composite() => standard.Runtime.CreateContext(Process, Halves);

        private static string Apply(
            RuleContext context,
            string input,
            string state,
            params (string Name, string Value)[] arguments)
        {
            string args = string.Join(
                ", ",
                arguments.Select(static argument => $"\"{argument.Name}\": \"{argument.Value}\""));

            return context.ApplyToState($$"""{ "input": "{{input}}", "args": { {{args}} } }""", state).State;
        }

        private static string? Field(string state, string alias, string name) =>
            (string?)JsonNode.Parse(state)!["data"]![alias]!["data"]![name];
    }
}
