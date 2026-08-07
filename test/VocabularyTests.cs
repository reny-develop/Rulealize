// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction;

namespace Rulealize.Tests
{
    /// <summary>The operations Reversi never reaches, and the corners of the value model.</summary>
    /// <remarks>
    /// Reversi exercises about half the standard vocabulary. The rest is covered by a rule
    /// set whose guard is a long conjunction of assertions: if any of them is wrong the
    /// candidate is not legal, and the count of surviving candidates gives it away.
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class VocabularyTests(StandardRuntime standard)
    {
        private RuleContext Sink => standard.KitchenSink;

        [Fact]
        public void TheProductOfTwoDomainsIsEnumerated()
        {
            // Two east-ray lengths crossed with nine squares of a three by three board.
            ValidInputSet moves = Sink.GetValidInputs(Sink.InitialState, 64);

            Assert.Equal(18, moves.Evaluated);
        }

        [Fact]
        public void EveryAssertionInTheGuardHolds()
        {
            // The guard asserts the results of math.sub, mul, div, mod, min, max and abs,
            // of cmp.compare, coalesce, lt, lte and gt, of logic.xor and not, of seq.where,
            // select, any, count and elementAt both in and out of range, and of
            // grid.directions in two kinds and grid.ray with a length. Seven candidates can
            // only survive if all of them are right.
            ValidInputSet moves = Sink.GetValidInputs(Sink.InitialState, 64);

            Assert.Equal(7, moves.Count);
            Assert.All(moves, static move => Assert.Equal("2", move.Arguments["by"]));
            Assert.DoesNotContain(moves, static move => move.Arguments["cell"] is "0,0" or "2,2");
        }

        [Fact]
        public void AnInputWithNoActorReportsNone() =>
            Assert.All(Sink.GetValidInputs(Sink.InitialState, 64), static move => Assert.Null(move.Actor));

        [Fact]
        public void StateUpdateReadsTheFieldItIsAboutToWrite()
        {
            JsonElement data = ApplyFirst();

            Assert.Equal(9, data.GetProperty("score").GetInt32());   // 7 + 2
        }

        [Fact]
        public void CoalesceFallsThroughANullField()
        {
            JsonElement data = ApplyFirst();

            Assert.Equal("fallback", data.GetProperty("note").GetString());
        }

        [Fact]
        public void MatchTakesItsDefaultWhenNoCaseApplies()
        {
            JsonElement data = ApplyFirst();

            Assert.Equal("other", data.GetProperty("label").GetString());
        }

        [Fact]
        public void ABooleanFieldRoundTrips()
        {
            JsonElement data = ApplyFirst();

            Assert.False(data.GetProperty("active").GetBoolean());
        }

        [Fact]
        public void IndexNotationCoordinatesRoundTrip()
        {
            JsonElement data = ApplyFirst();

            Assert.Equal("x", data.GetProperty("board").GetProperty("0,0").GetString());
        }

        [Fact]
        public void ANumericArgumentSurvivesTheRoundTrip()
        {
            // Arguments go out as JSON and come back as JSON, and the value model has no
            // conversions. A number written out as the text "2" would come back as text, and
            // the first comparison against a number would fault. Writing it as a number is
            // the only thing that keeps a numeric parameter usable at all.
            ValidInput move = Sink.GetValidInputs(Sink.InitialState, 64)[0];

            using JsonDocument document = JsonDocument.Parse(move.ToInputDocument(Sink.RuleSet));
            JsonElement args = document.RootElement.GetProperty("args");

            Assert.Equal(JsonValueKind.Number, args.GetProperty("by").ValueKind);
            Assert.Equal(JsonValueKind.String, args.GetProperty("cell").ValueKind);
        }

        [Fact]
        public void AnArgumentOfTheWrongKindFaultsRatherThanBeingCoerced()
        {
            // "2" is not 2. Nothing converts it, and the guard's cmp.gt says so.
            Assert.Throws<RuleEvaluationException>(
                () => Sink.ApplyToState(
                    """{ "input": "bump", "args": { "by": "2", "cell": "1,1" } }""",
                    Sink.InitialState));
        }

        [Fact]
        public void NullIsNotFalsy() =>
            Assert.Throws<RuleEvaluationException>(() => Evaluate("""{ "op": "cmp.coalesce", "of": [] }"""));

        [Fact]
        public void OrderingAgainstNullFaults() =>
            Assert.Contains(
                "Ordering is not defined for null",
                Assert.Throws<RuleEvaluationException>(
                    () => Evaluate("""{ "op": "cmp.lt", "left": null, "right": 1 }""")).Message,
                StringComparison.Ordinal);

        [Fact]
        public void EqualityAgainstNullDoesNot() =>
            // The asymmetry that lets Reversi's capture rule read past the end of a ray.
            Assert.False(Evaluate("""{ "op": "cmp.eq", "left": null, "right": "black" }"""));

        [Fact]
        public void ArithmeticOnNullFaults() =>
            Assert.Throws<RuleEvaluationException>(
                () => Evaluate("""{ "op": "math.add", "of": [null, 1] }"""));

        [Fact]
        public void DividingByZeroFaults() =>
            Assert.Throws<RuleEvaluationException>(
                () => Evaluate("""{ "op": "math.div", "left": 1, "right": 0 }"""));

        [Fact]
        public void MatchingWithNoCaseAndNoDefaultFaults() =>
            Assert.Throws<RuleEvaluationException>(
                () => Evaluate("""{ "op": "branch.match", "value": "z", "cases": { "a": true } }"""));

        [Fact]
        public void ReadingPastTheEndOfASequenceDoesNotFault() =>
            Assert.True(Evaluate("""
                { "op": "cmp.isNull",
                  "value": { "op": "seq.elementAt", "source": { "op": "seq.empty" }, "index": 99 } }
                """));

        [Fact]
        public void ANegativeIndexDoesFault() =>
            // Out of range is an answer; not an index at all is a mistake.
            Assert.Throws<RuleEvaluationException>(() => Evaluate("""
                { "op": "cmp.isNull",
                  "value": { "op": "seq.elementAt", "source": { "op": "seq.empty" }, "index": -1 } }
                """));

        [Fact]
        public void ASequenceCanBeWalkedTwice() =>
            // Reversi binds a ray once and consumes it from two places. A single-use
            // iterator would quietly return nothing the second time.
            Assert.True(Evaluate("""
                { "op": "bind.let",
                  "bind": { "r": { "op": "seq.select",
                                   "source": { "op": "grid.coords", "of": "$board" },
                                   "as": "c", "select": 1 } },
                  "in": { "op": "cmp.eq",
                          "left":  { "op": "seq.count", "source": "@r" },
                          "right": { "op": "seq.count", "source": "@r" } } }
                """));

        [Fact]
        public void AndStopsAtTheFirstFalseOperand() =>
            // The second operand would fault. Reaching it would mean the order is not kept.
            Assert.False(Evaluate("""
                { "op": "logic.and", "all": [ false, { "op": "math.div", "left": 1, "right": 0 } ] }
                """));

        [Fact]
        public void OrStopsAtTheFirstTrueOperand() =>
            Assert.True(Evaluate("""
                { "op": "logic.or", "any": [ true, { "op": "math.div", "left": 1, "right": 0 } ] }
                """));

        [Fact]
        public void TheUntakenBranchIsNotEvaluated() =>
            Assert.True(Evaluate("""
                { "op": "branch.if", "cond": true, "then": true,
                  "else": { "op": "math.div", "left": 1, "right": 0 } }
                """));

        [Fact]
        public void ANumberMatchesItsNormalisedText() =>
            Assert.True(Evaluate("""
                { "op": "branch.match", "value": 1.0, "cases": { "1": true } }
                """));

        [Fact]
        public void WritingOffTheBoardFaultsEvenThoughReadingDoesNot()
        {
            Assert.True(Evaluate("""
                { "op": "cmp.isNull", "value": { "op": "grid.at", "grid": "$board", "coord": "z9" } }
                """));

            RuleContext context = standard.Runtime.CreateContext($$"""
                {
                  "id": "t", "version": "1.0.0",
                  "state": { "schema": { "board": { "op": "grid.board", "width": 2, "height": 2,
                                                    "cell": { "op": "type.bool", "nullable": true } } },
                             "initial": { "board": {} } },
                  "inputs": { "go": { "effects": [
                    { "op": "grid.set", "target": "$board", "coord": "9,9", "value": true } ] } }
                }
                """);

            Assert.Throws<RuleEvaluationException>(
                () => context.ApplyToState("""{ "input": "go", "args": {} }""", context.InitialState));
        }

        private JsonElement ApplyFirst()
        {
            ValidInput move = Sink.GetValidInputs(Sink.InitialState, 64)[0];
            TransitionResult result = Sink.ApplyToState(move.ToInputDocument(Sink.RuleSet), Sink.InitialState);

            // Parsed and copied out, because the document is disposed with the scope.
            using JsonDocument document = JsonDocument.Parse(result.State);
            return document.RootElement.GetProperty("data").Clone();
        }

        /// <summary>Evaluates one boolean expression, by making a rule set's guard out of it.</summary>
        /// <remarks>
        /// There is no way to evaluate an expression on its own, and there should not be —
        /// an expression means something only in a rule set, against a state. A guard is the
        /// smallest place to put one.
        /// </remarks>
        private bool Evaluate(string expression)
        {
            RuleContext context = standard.Runtime.CreateContext($$"""
                {
                  "id": "probe", "version": "1.0.0",
                  "state": {
                    "schema": { "board": { "op": "grid.board", "width": 2, "height": 2, "coord": "algebraic",
                                           "cell": { "op": "type.bool", "nullable": true } } },
                    "initial": { "board": {} }
                  },
                  "inputs": { "go": { "when": {{expression}}, "effects": [] } }
                }
                """);

            return context.GetValidInputs(context.InitialState, 8).Count == 1;
        }
    }
}
