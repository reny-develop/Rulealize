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
            // select, any, count and elementAt both in and out of range, of seq.sum bare,
            // with a projection and over an empty sequence, and of grid.directions in two
            // kinds and grid.ray with a length. Seven candidates can only survive if all of
            // them are right.
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
        public void AnArgumentOfTheWrongKindIsRefusedRatherThanCoerced()
        {
            // "2" is not 2, and the document boundary is no place to start pretending
            // otherwise. The domain of 'by' holds numbers, text is not one of them, and the
            // refusal names the parameter rather than faulting later inside whichever
            // operation the guard happened to reach first.
            IllegalInputException refused = Assert.Throws<IllegalInputException>(
                () => Sink.ApplyToState(
                    """{ "input": "bump", "args": { "by": "2", "cell": "1,1" } }""",
                    Sink.InitialState));

            Assert.Contains("'by'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AnOpaqueArgumentIsRecognisedInItsTextForm()
        {
            // The one concession, and the reason there is one: a coordinate has no JSON form
            // of its own, so it leaves as "1,1" and has to be recognised coming back.
            TransitionResult applied = Sink.ApplyToState(
                """{ "input": "bump", "args": { "by": 2, "cell": "1,1" } }""",
                Sink.InitialState);

            Assert.False(applied.IsTerminal);
        }

        [Fact]
        public void ADomainIsPartOfTheRulesAndNarrowsWithTheState()
        {
            // The two methods have to answer the same question. Here the whole rule is in the
            // domain and there is no guard at all: only squares already marked may be picked.
            RuleContext context = standard.Runtime.CreateContext("""
                {
                  "id": "probe", "version": "1.0.0",
                  "state": {
                    "schema": { "board": { "op": "grid.board", "width": 2, "height": 2, "coord": "algebraic",
                                           "cell": { "op": "type.bool", "nullable": true } } },
                    "initial": { "board": { "a1": true } }
                  },
                  "inputs": { "pick": {
                    "params": { "c": { "domain": {
                      "op": "seq.where", "source": { "op": "grid.coords", "of": "$board" }, "as": "c",
                      "predicate": { "op": "cmp.eq", "right": true,
                                     "left": { "op": "grid.at", "grid": "$board", "coord": "@c" } } } } },
                    "effects": [ { "op": "grid.set", "target": "$board", "coord": "@c", "value": false } ] } }
                }
                """);

            Assert.Equal(
                ["a1"],
                context.GetValidInputs(context.InitialState, 16).Select(static move => move.Arguments["c"]));

            // a1 is offered and applies; b2 is a square of the board, is written the same way,
            // and is refused — by the domain, because nothing else here would refuse anything.
            context.ApplyToState("""{ "input": "pick", "args": { "c": "a1" } }""", context.InitialState);
            Assert.Throws<IllegalInputException>(
                () => context.ApplyToState("""{ "input": "pick", "args": { "c": "b2" } }""", context.InitialState));
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

        [Fact]
        public void ASequenceCanBeWrittenOut() =>
            Assert.True(Evaluate("""
                { "op": "cmp.eq",
                  "left": { "op": "seq.elementAt", "index": 1,
                            "source": { "op": "seq.of", "of": ["a", "b", "c"] } },
                  "right": "b" }
                """));

        [Fact]
        public void AnEmptySeqOfIsTheEmptySequence() =>
            Assert.True(Evaluate("""
                { "op": "logic.not", "value": { "op": "seq.any", "source": { "op": "seq.of", "of": [] } } }
                """));

        [Fact]
        public void SeqOfAndSelectManyConcatenate() =>
            // There is no seq.concat, and this is why one is not needed.
            Assert.True(Evaluate("""
                { "op": "cmp.eq", "right": 3,
                  "left": { "op": "seq.count", "source": {
                      "op": "seq.selectMany", "as": "part", "select": "@part",
                      "source": { "op": "seq.of", "of": [
                          { "op": "seq.of", "of": ["a", "b"] },
                          { "op": "seq.of", "of": ["c"] } ] } } } }
                """));

        [Fact]
        public void ATupleReadsBackWhatItWasBuiltFrom() =>
            Assert.True(Evaluate("""
                { "op": "cmp.eq", "right": "b",
                  "left": { "op": "tuple.at", "index": 1,
                            "tuple": { "op": "tuple.of", "of": ["a", "b", "c"] } } }
                """));

        [Fact]
        public void ATupleReadsTheSameOutOfItsTextForm() =>
            // Which is what makes ApplyToState reach the move GetValidInputs offered: the
            // argument arrives as text, and tuple.at has to find the same component in it.
            Assert.True(Evaluate("""
                { "op": "cmp.eq", "right": "b", "left": { "op": "tuple.at", "tuple": "a|b|c", "index": 1 } }
                """));

        [Fact]
        public void AComponentContainingTheSeparatorStillReadsBack() =>
            // "a|b" and "c", written "a\|b|c". Without escaping this would come back as
            // three components and every value anyone had happened to try would still work.
            Assert.True(Evaluate("""
                { "op": "logic.and", "all": [
                  { "op": "cmp.eq", "right": "a|b",
                    "left": { "op": "tuple.at", "index": 0,
                              "tuple": { "op": "tuple.of", "of": ["a|b", "c"] } } },
                  { "op": "cmp.eq", "right": "a|b", "left": { "op": "tuple.at", "tuple": "a\\|b|c", "index": 0 } },
                  { "op": "cmp.eq", "right": "c", "left": { "op": "tuple.at", "tuple": "a\\|b|c", "index": 1 } } ] }
                """));

        [Fact]
        public void ReadingAComponentOfNullIsNull() =>
            Assert.True(Evaluate("""
                { "op": "cmp.isNull", "value": { "op": "tuple.at", "tuple": null, "index": 0 } }
                """));

        [Fact]
        public void ReadingPastTheEndOfATupleFaults() =>
            // Unlike a sequence, whose length is a property of the position. A tuple's
            // length is a property of the rule that built it, so this is a rule that is wrong.
            Assert.Throws<RuleEvaluationException>(() => Evaluate("""
                { "op": "cmp.isNull",
                  "value": { "op": "tuple.at", "index": 5, "tuple": { "op": "tuple.of", "of": ["a"] } } }
                """));

        [Fact]
        public void ATupleWithAnUnwritableComponentCannotBeAnArgument()
        {
            // A sequence has no text form, so neither does a tuple holding one, so it cannot
            // make the trip out through GetValidInputs. The parameter is named.
            RuleContext context = standard.Runtime.CreateContext("""
                {
                  "id": "probe", "version": "1.0.0",
                  "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
                  "inputs": { "go": { "params": { "m": { "domain": { "op": "seq.of", "of": [
                                { "op": "tuple.of", "of": ["a", { "op": "seq.of", "of": ["x"] }] } ] } } },
                                      "effects": [] } }
                }
                """);

            RuleEvaluationException error =
                Assert.Throws<RuleEvaluationException>(() => context.GetValidInputs(context.InitialState, 8));

            Assert.Contains("inputs.go.params.m", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void UpdatingABoardAsAValueLeavesTheOriginalAlone() =>
            // grid.with returns a board; it does not write one. The state is untouched, which
            // is what lets a guard ask about the position a move would produce.
            Assert.True(Evaluate("""
                { "op": "logic.and", "all": [
                  { "op": "cmp.eq", "right": true, "left": {
                      "op": "grid.at", "coord": "a1",
                      "grid": { "op": "grid.with", "grid": "$board", "coord": "a1", "value": true } } },
                  { "op": "cmp.isNull", "value": { "op": "grid.at", "grid": "$board", "coord": "a1" } } ] }
                """));

        [Fact]
        public void UpdatingSeveralSquaresAtOnceTakesAnEmptySequenceInItsStride() =>
            Assert.True(Evaluate("""
                { "op": "cmp.isNull", "value": {
                    "op": "grid.at", "coord": "a1",
                    "grid": { "op": "grid.withMany", "grid": "$board",
                              "coords": { "op": "seq.empty" }, "value": true } } }
                """));

        [Fact]
        public void UpdatingOffTheBoardFaultsTheWayWritingDoes() =>
            Assert.Throws<RuleEvaluationException>(() => Evaluate("""
                { "op": "cmp.isNull", "value": {
                    "op": "grid.with", "grid": "$board", "coord": "z9", "value": true } }
                """));

        [Fact]
        public void ASquareFieldKeepsACoordinateRatherThanItsSpelling()
        {
            // The reason grid.square exists. Both fields read "b2" in the document; only the
            // one declared as a square comes back as something a coordinate compares equal to.
            RuleContext context = standard.Runtime.CreateContext("""
                {
                  "id": "probe", "version": "1.0.0",
                  "state": {
                    "schema": {
                      "board": { "op": "grid.board", "width": 2, "height": 2, "coord": "algebraic",
                                 "cell": { "op": "type.bool", "nullable": true } },
                      "mark": { "op": "grid.square", "width": 2, "height": 2, "coord": "algebraic" },
                      "spelling": { "op": "type.string" }
                    },
                    "initial": { "board": {}, "mark": "b2", "spelling": "b2" }
                  },
                  "inputs": { "go": { "params": { "c": { "domain": { "op": "grid.coords", "of": "$board" } } },
                                      "when": { "op": "cmp.eq", "left": "@c", "right": "$mark" },
                                      "effects": [] },
                              "no": { "params": { "c": { "domain": { "op": "grid.coords", "of": "$board" } } },
                                      "when": { "op": "cmp.eq", "left": "@c", "right": "$spelling" },
                                      "effects": [] } }
                }
                """);

            ValidInputSet matches = context.GetValidInputs(context.InitialState, 16);

            Assert.Equal(["go"], matches.Select(static match => match.Input));
            Assert.Equal("b2", matches[0].Arguments["c"]);
        }

        [Fact]
        public void ASquareFieldSurvivesAStateDocument()
        {
            RuleContext context = standard.Runtime.CreateContext("""
                {
                  "id": "probe", "version": "1.0.0",
                  "state": {
                    "schema": {
                      "board": { "op": "grid.board", "width": 2, "height": 2, "coord": "algebraic",
                                 "cell": { "op": "type.bool", "nullable": true } },
                      "mark": { "op": "grid.square", "width": 2, "height": 2, "coord": "algebraic",
                                "nullable": true }
                    },
                    "initial": { "board": {}, "mark": null }
                  },
                  "inputs": { "go": { "effects": [
                    { "op": "state.set", "path": "mark",
                      "value": { "op": "seq.elementAt", "index": 0,
                                 "source": { "op": "grid.coords", "of": "$board" } } } ] } }
                }
                """);

            TransitionResult after =
                context.ApplyToState("""{ "input": "go", "args": {} }""", context.InitialState);

            using JsonDocument document = JsonDocument.Parse(after.State);
            Assert.Equal("a2", document.RootElement.GetProperty("data").GetProperty("mark").GetString());
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
