// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction;

namespace Rulealize.Tests
{
    /// <summary>What happens when an input does not settle the next state on its own.</summary>
    /// <remarks>
    /// <para>
    /// <c>GetValidInputs</c> answers who may do what. This is the other half: what may then
    /// happen, and where each of those leads. A traversal is the two of them in that order,
    /// and the point of most of what is below is that the order does not change when a rule
    /// set has chance in it — an input that draws nothing has one outcome of probability one,
    /// so the loop runs once instead of three times and a caller writes no branch.
    /// </para>
    /// <para>
    /// The vocabulary under all of this is <c>Rulealize.Plugin.Chance</c>, loaded from the
    /// plugin folder like every other standard one. It provides <c>chance.pick</c> and
    /// nothing else, and it chooses nothing — everything below is the runtime deciding what
    /// could have happened.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class OutcomeTests(StandardRuntime standard)
    {
        private const int Limit = 100;

        private RuleContext Lab => standard.ChanceLab;

        // ── Placement ──────────────────────────────────────────────────────────

        [Fact]
        public void ADrawCannotDecideWhetherAMoveIsLegal() =>
            // The whole reason placement is enforced. A guard is evaluated once per candidate
            // while the search is sifting them, and there is no outcome to be drawing for.
            Assert.Contains("may only appear inside an input's 'effects'", Rejects("""
                "go": { "when": { "op": "chance.pick", "of": { "op": "seq.of", "of": [true] } },
                        "effects": [] }
                """), StringComparison.Ordinal);

        [Fact]
        public void ADrawCannotDecideWhatAParameterMayBe() =>
            Assert.Contains("may only appear inside an input's 'effects'", Rejects("""
                "go": { "params": { "x": { "domain": { "op": "chance.pick",
                                                       "of": { "op": "seq.of", "of": [1] } } } },
                        "effects": [] }
                """), StringComparison.Ordinal);

        [Fact]
        public void ADrawCannotDecideWhoseMoveItIs() =>
            Assert.Contains("may only appear inside an input's 'effects'", Rejects("""
                "go": { "actor": { "op": "chance.pick", "of": { "op": "seq.of", "of": ["ann"] } },
                        "effects": [] }
                """), StringComparison.Ordinal);

        [Fact]
        public void ADrawCannotDecideWhetherTheGameIsOver() =>
            Assert.Contains("may only appear inside an input's 'effects'", Rejects(
                """ "go": { "effects": [] } """,
                """ , "terminal": { "when": { "op": "chance.pick", "of": { "op": "seq.of", "of": [true] } } } """),
                StringComparison.Ordinal);

        [Fact]
        public void ADrawCannotHideInsideADefinition() =>
            // The one that would be hardest to find later. A definition's result is memoized
            // against its arguments and the snapshot, so a body that drew would answer the
            // first caller and then repeat itself to every other one.
            Assert.Contains("may only appear inside an input's 'effects'", Rejects(
                """ "go": { "effects": [ { "op": "state.set", "path": "n", "value": "#luck" } ] } """,
                definitions: """
                    "definitions": {
                      "luck": { "op": "chance.pick", "of": { "op": "seq.of", "of": [1] } }
                    },
                    """),
                StringComparison.Ordinal);

        [Fact]
        public void ADrawIsAllowedHoweverDeepInsideAnEffect()
        {
            // Not at the top of an effect's value but four nodes down, which is where the
            // check has to reach: a subtree is what is being permitted, not a position.
            OutcomeSet outcomes = Lab.GetOutcomes(Input("roll"), Lab.InitialState, Limit);

            Assert.Equal(3, outcomes.Count);
        }

        // ── One shape for everything ───────────────────────────────────────────

        [Fact]
        public void AnInputThatDrawsNothingStillHasAnOutcome()
        {
            // The uniformity the whole design is for. A caller loops over the outcomes of
            // every input it applies, and this loop runs exactly once.
            OutcomeSet outcomes = Lab.GetOutcomes(Input("wait"), Lab.InitialState, Limit);

            Outcome only = Assert.Single(outcomes);
            Assert.Equal(1, only.Probability);
            Assert.Empty(only.Draws);
            Assert.Equal(1, outcomes.Coverage);
            Assert.False(outcomes.Truncated);
        }

        [Fact]
        public void ThatOutcomeIsWhatApplyingItWouldHaveProduced()
        {
            Outcome only = Assert.Single(Lab.GetOutcomes(Input("wait"), Lab.InitialState, Limit));

            Assert.Equal(
                Lab.ApplyToState(Input("wait"), Lab.InitialState).State,
                only.Result.State);
        }

        [Fact]
        public void AnEvenDrawSplitsTheProbabilityEvenly()
        {
            OutcomeSet outcomes = Lab.GetOutcomes(Input("roll"), Lab.InitialState, Limit);

            Assert.Equal(["1", "2", "3"], outcomes.Select(static o => o.Draws.Single()).Order(StringComparer.Ordinal));
            Assert.All(outcomes, static o => Assert.Equal(1.0 / 3, o.Probability, 12));
            Assert.Equal(1, outcomes.Coverage, 12);
        }

        [Fact]
        public void AWeightedDrawSplitsItByWeight()
        {
            // Three red and one green left, and no blue at all.
            OutcomeSet outcomes = Lab.GetOutcomes(Input("grab"), Lab.InitialState, Limit);

            Assert.Equal(["r", "g"], outcomes.Select(static o => o.Draws.Single()));
            Assert.Equal(0.75, outcomes[0].Probability, 12);
            Assert.Equal(0.25, outcomes[1].Probability, 12);
        }

        [Fact]
        public void SomethingWithNoWeightLeftIsNotSomethingThatCanHappen() =>
            // Weight zero is an ordinary state of affairs — none of that colour left — and it
            // drops out rather than arriving as an outcome of probability zero.
            Assert.DoesNotContain(
                Lab.GetOutcomes(Input("grab"), Lab.InitialState, Limit),
                static o => o.Draws.Single() == "b");

        [Fact]
        public void NothingLeftToDrawFromIsAFault() =>
            // Not an empty answer. A legal input has at least one thing that can happen to
            // it, so a rule set that reaches an empty source is one whose guard did not say
            // the source could be empty — and saying so here is what keeps a caller from
            // needing to check for an empty set.
            Assert.Contains(
                "nothing to draw from",
                Assert.Throws<RuleEvaluationException>(
                    () => Lab.GetOutcomes(Input("vanish"), Lab.InitialState, Limit)).Message,
                StringComparison.Ordinal);

        [Fact]
        public void AnOutcomeTheSchemaForbidsIsAFaultAndNotAQuietOmission()
        {
            // One of the two branches takes the total below the minimum its schema declares.
            // That could have been made to drop the branch, and deliberately was not: dropping
            // it turns the schema into something that silently reweights a distribution, and
            // the caller would have no way of telling that from a draw that genuinely could
            // not happen. It is the same fault applying that branch on its own would raise.
            Assert.Throws<RuleEvaluationException>(
                () => Lab.GetOutcomes(Input("risky"), Lab.InitialState, Limit));
        }

        // ── A draw that depends on an earlier one ──────────────────────────────

        [Fact]
        public void ASecondDrawSeesWhatTheFirstProduced()
        {
            // n of one, two or three, and then one of the first n letters. Where the second
            // draw is and what it could produce are both decided by the first, which is why a
            // branch is found by running the effects rather than by reading the document.
            OutcomeSet outcomes = Lab.GetOutcomes(Input("cascade"), Lab.InitialState, Limit);

            Assert.Equal(6, outcomes.Count);
            Assert.Equal(
                ["1,a", "2,a", "2,b", "3,a", "3,b", "3,c"],
                outcomes.Select(static o => string.Join(",", o.Draws)).Order(StringComparer.Ordinal));
            Assert.Equal(1, outcomes.Coverage, 12);
        }

        [Fact]
        public void TheMostLikelyOutcomeComesFirst()
        {
            OutcomeSet outcomes = Lab.GetOutcomes(Input("cascade"), Lab.InitialState, Limit);

            Assert.Equal("1,a", string.Join(",", outcomes[0].Draws));
            Assert.Equal(1.0 / 3, outcomes[0].Probability, 12);
            Assert.Equal(
                outcomes.Select(static o => o.Probability).OrderDescending(),
                outcomes.Select(static o => o.Probability));
        }

        [Fact]
        public void FindingABranchCostsARunOfTheEffects()
        {
            // Six outcomes and ten runs: the empty script, the three that stop at the second
            // draw, and the six that reach the end. Nothing about the document says where the
            // draws are, so each branch is found by replaying from the start.
            OutcomeSet outcomes = Lab.GetOutcomes(Input("cascade"), Lab.InitialState, Limit);

            Assert.Equal(6, outcomes.Count);
            Assert.Equal(10, outcomes.Evaluated);
        }

        // ── The limit ──────────────────────────────────────────────────────────

        [Fact]
        public void TheLimitCountsOutcomesRatherThanWork()
        {
            // One is a legal limit and yields one, which is what makes "a legal input has at
            // least one outcome" true for every limit rather than for large enough ones. Four
            // runs of the effects happened underneath to produce it.
            OutcomeSet outcomes = Lab.GetOutcomes(Input("cascade"), Lab.InitialState, 1);

            Assert.Single(outcomes);
            Assert.True(outcomes.Truncated);
            Assert.Equal(1.0 / 3, outcomes.Coverage, 12);
        }

        [Fact]
        public void WhatSurvivesTheLimitIsTheHeaviestPartOfTheDistribution()
        {
            OutcomeSet outcomes = Lab.GetOutcomes(Input("cascade"), Lab.InitialState, 3);

            Assert.Equal(["1,a", "2,a", "2,b"], outcomes.Select(static o => string.Join(",", o.Draws)));
            Assert.Equal((1.0 / 3) + (1.0 / 6) + (1.0 / 6), outcomes.Coverage, 12);
        }

        [Fact]
        public void CoverageIsWhatTruncationCostsAndTruncatedAloneDoesNotSayIt()
        {
            // Truncating a list of legal moves leaves a set of legal moves. Truncating a list
            // of outcomes leaves a distribution that no longer sums to one, and how much is
            // missing is the part a search needs.
            OutcomeSet whole = Lab.GetOutcomes(Input("cascade"), Lab.InitialState, Limit);
            OutcomeSet part = Lab.GetOutcomes(Input("cascade"), Lab.InitialState, 2);

            Assert.False(whole.Truncated);
            Assert.Equal(1, whole.Coverage, 12);
            Assert.True(part.Truncated);
            Assert.Equal(0.5, part.Coverage, 12);
        }

        [Fact]
        public void ALimitOfNoneIsNotALimit() =>
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Lab.GetOutcomes(Input("roll"), Lab.InitialState, 0));

        // ── Applying and replaying ─────────────────────────────────────────────

        [Fact]
        public void AnInputThatDrawsCannotBeAppliedOnItsOwn() =>
            // Refused from the document, before anything is evaluated, and the message says
            // which method the caller wanted.
            Assert.Contains(
                "GetOutcomes",
                Assert.Throws<InvalidOperationException>(
                    () => Lab.ApplyToState(Input("roll"), Lab.InitialState)).Message,
                StringComparison.Ordinal);

        [Fact]
        public void AnOutcomeReplaysToTheStateItDescribed()
        {
            // The property everything rests on. An input says what somebody decided, an
            // outcome says what the world did, and the two of them together settle the
            // transition — so a recorded pair produces the state it was recorded against.
            OutcomeSet outcomes = Lab.GetOutcomes(Input("cascade"), Lab.InitialState, Limit);

            foreach (Outcome outcome in outcomes)
            {
                string replayed = Lab
                    .ApplyToState(Input("cascade"), Lab.InitialState, outcome.ToOutcomeDocument(Lab.RuleSet))
                    .State;

                Assert.Equal(outcome.Result.State, replayed);
            }
        }

        [Fact]
        public void AnOutcomeWithNoDrawsMeansTheSameAsNotPassingOne()
        {
            // Which is what lets a caller record every transition the same way, whether the
            // rule set draws anything or not.
            Outcome only = Assert.Single(Lab.GetOutcomes(Input("wait"), Lab.InitialState, Limit));

            Assert.Equal(
                Lab.ApplyToState(Input("wait"), Lab.InitialState).State,
                Lab.ApplyToState(Input("wait"), Lab.InitialState, only.ToOutcomeDocument(Lab.RuleSet)).State);
        }

        [Fact]
        public void ADrawnValueKeepsItsOwnJsonForm()
        {
            // A number goes out as a number, exactly as an input argument does, because it has
            // to be recognised on the way back in and "1" is not 1 in the value model.
            Outcome first = Lab.GetOutcomes(Input("roll"), Lab.InitialState, Limit)[0];

            using JsonDocument document = JsonDocument.Parse(first.ToOutcomeDocument(Lab.RuleSet));
            JsonElement draw = document.RootElement.GetProperty("draws").EnumerateArray().Single();

            Assert.Equal(JsonValueKind.Number, draw.ValueKind);
            Assert.Equal("roll", document.RootElement.GetProperty("input").GetString());
        }

        [Fact]
        public void AnOutcomeThatNamesSomethingThatCouldNotHaveHappenedIsRefused() =>
            Assert.Contains("not among the things that could have", Assert.Throws<RuleDocumentException>(
                () => Lab.ApplyToState(Input("roll"), Lab.InitialState, Resolved("roll", "9"))).Message,
                StringComparison.Ordinal);

        [Fact]
        public void AnOutcomeWithMoreDrawsThanHappenedIsRefused() =>
            Assert.Contains("do not describe the same transition", Assert.Throws<RuleDocumentException>(
                () => Lab.ApplyToState(Input("roll"), Lab.InitialState, Resolved("roll", "1", "2"))).Message,
                StringComparison.Ordinal);

        [Fact]
        public void AnOutcomeForAnotherInputIsRefused() =>
            Assert.Contains("the input document applies", Assert.Throws<RuleDocumentException>(
                () => Lab.ApplyToState(Input("roll"), Lab.InitialState, Resolved("cascade", "1"))).Message,
                StringComparison.Ordinal);

        // ── Nothing else moved ─────────────────────────────────────────────────

        [Theory]
        [MemberData(nameof(RuleSetsWithoutChance))]
        public void ARuleSetWithNoDrawInItAnswersTheSameWayItAlwaysDid(string name)
        {
            // Every legal move in the opening position of every rule set that has no chance in
            // it anywhere: one outcome apiece, certain, empty, and the state ApplyToState
            // gives. Nothing a caller was doing before needs a second thought, and the loop it
            // has to write to keep working is the loop a rule set with chance in it wants.
            RuleContext rules = Without(name);
            ValidInputSet moves = rules.GetValidInputs(rules.InitialState, 4096);

            Assert.NotEmpty(moves);
            foreach (ValidInput move in moves)
            {
                string document = move.ToInputDocument(rules.RuleSet);
                Outcome only = Assert.Single(rules.GetOutcomes(document, rules.InitialState, 8));

                Assert.Equal(1, only.Probability);
                Assert.Empty(only.Draws);
                Assert.Equal(rules.ApplyToState(document, rules.InitialState).State, only.Result.State);
            }
        }

        public static TheoryData<string> RuleSetsWithoutChance() =>
            ["approval", "reversi", "kitchen-sink", "chess", "shogi", "roster", "deploy", "blackjack-choice"];

        private RuleContext Without(string name) => name switch
        {
            "approval" => standard.Approval,
            "reversi" => standard.Reversi,
            "kitchen-sink" => standard.KitchenSink,
            "chess" => standard.Chess,
            "shogi" => standard.Shogi,
            "roster" => standard.Roster,
            "deploy" => standard.Deploy,
            _ => standard.BlackjackChoice
        };

        // ── Driving it ─────────────────────────────────────────────────────────

        private static string Input(string name) => $$"""
            { "$schema": "rulealize/input/v1", "ruleSet": "chance-lab@1.0.0", "input": "{{name}}", "args": {} }
            """;

        private static string Resolved(string input, params string[] draws) => $$"""
            { "$schema": "rulealize/outcome/v1", "ruleSet": "chance-lab@1.0.0",
              "input": "{{input}}", "draws": [{{string.Join(", ", draws)}}] }
            """;

        /// <summary>Compiles a rule set that puts a draw somewhere and reports why it was refused.</summary>
        private string Rejects(string input, string tail = "", string definitions = "") =>
            Assert.Throws<RuleSetBuildException>(() => standard.Runtime.CreateContext($$"""
                {
                  "id": "misplaced", "version": "1.0.0",
                  "requires": [ { "plugin": "Rulealize.Plugin.Chance", "version": "^1.0" },
                                { "plugin": "Rulealize.Plugin.TypeSchema" },
                                { "plugin": "Rulealize.Plugin.State" },
                                { "plugin": "Rulealize.Plugin.Sequence" },
                                { "plugin": "Rulealize.Plugin.Definition" },
                                { "plugin": "Rulealize.Plugin.Comparison" } ],
                  "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
                  {{definitions}}
                  "inputs": { {{input}} }{{tail}}
                }
                """)).Message;
    }
}
