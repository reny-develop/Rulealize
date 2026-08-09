// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction;

namespace Rulealize.Tests
{
    /// <summary>Records and lists in the state.</summary>
    /// <remarks>
    /// The two are not symmetric and the tests show why. A list holds a sequence, so the
    /// sequence plugin reads it and nothing new was needed; a record had no vocabulary at
    /// all, and getting one meant working out how to write a key that is computed while the
    /// path naming the field stays a literal.
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class CollectionTests(StandardRuntime standard)
    {
        // ── Records ────────────────────────────────────────────────────────────

        [Fact]
        public void AKeyCanBeComputed()
        {
            // The whole point. Shogi adds to the hand of whoever is to move, a piece whose
            // kind it worked out from the board, and a literal path cannot say that.
            RuleContext context = Tally();

            TransitionResult after = context.ApplyToState(
                """{ "input": "bump", "args": { "which": "b" } }""",
                context.InitialState);

            Assert.Equal(0, Count(after.State, "a"));
            Assert.Equal(1, Count(after.State, "b"));
        }

        [Fact]
        public void TwoEffectsOnOneRecordAddUp()
        {
            // rec.update reads from the draft, the way grid.set does. Reading the snapshot
            // instead would let the second effect quietly undo the first.
            RuleContext context = Tally();

            TransitionResult after = context.ApplyToState(
                """{ "input": "bumpBoth", "args": {} }""",
                context.InitialState);

            Assert.Equal(1, Count(after.State, "a"));
            Assert.Equal(1, Count(after.State, "b"));
        }

        [Fact]
        public void AKeyTheRecordDoesNotHaveIsAFault() =>
            // Deliberately unlike grid.at, which answers null off the board. A board has
            // squares that legitimately do not exist; a record's keys are all declared, so
            // asking for another one is a mistake and nothing depends on it being quiet.
            Assert.Throws<RuleEvaluationException>(() => Evaluate("""
                { "op": "cmp.eq", "right": 0, "left": { "op": "rec.at", "record": "$tally", "key": "z" } }
                """));

        [Fact]
        public void WhetherAKeyIsThereCanBeAskedFirst() =>
            Assert.True(Evaluate("""
                { "op": "logic.and", "all": [
                  { "op": "rec.has", "record": "$tally", "key": "a" },
                  { "op": "logic.not", "value": { "op": "rec.has", "record": "$tally", "key": "z" } } ] }
                """));

        [Fact]
        public void ReadingAKeyOfNullIsNull() =>
            Assert.True(Evaluate("""
                { "op": "cmp.isNull", "value": { "op": "rec.at", "record": null, "key": "a" } }
                """));

        [Fact]
        public void RewritingARecordLeavesTheOriginalAlone() =>
            Assert.True(Evaluate("""
                { "op": "logic.and", "all": [
                  { "op": "cmp.eq", "right": 7, "left": { "op": "rec.at", "key": "a",
                      "record": { "op": "rec.with", "record": "$tally", "key": "a", "value": 7 } } },
                  { "op": "cmp.eq", "right": 0, "left": { "op": "rec.at", "record": "$tally", "key": "a" } } ] }
                """));

        [Fact]
        public void ARecordCannotGrowAKeyItWasNotDeclaredWith() =>
            // What keeps a record inside its schema however a rule set rewrites it. The key
            // set can only ever be the one it was read with, so nothing has to check later.
            Assert.Throws<RuleEvaluationException>(() => Evaluate("""
                { "op": "cmp.isNull", "value": { "op": "rec.with", "record": "$tally", "key": "z", "value": 1 } }
                """));

        [Fact]
        public void TheKeysAreTheWayIn() =>
            // Ordinal order, which is the only one a record always has: a record may have
            // been built by a literal that no schema ever saw.
            Assert.True(Evaluate("""
                { "op": "logic.and", "all": [
                  { "op": "cmp.eq", "right": 2, "left": { "op": "seq.count",
                      "source": { "op": "rec.keys", "of": "$tally" } } },
                  { "op": "cmp.eq", "right": "a", "left": { "op": "seq.elementAt", "index": 0,
                      "source": { "op": "rec.keys", "of": "$tally" } } } ] }
                """));

        [Fact]
        public void ARecordFieldMustBeARecordToBeWrittenTo() =>
            // Caught when the rule set is built, the way pointing grid.set at a counter is.
            Assert.Contains(
                "is not a record",
                Assert.Throws<RuleSetBuildException>(() => standard.Runtime.CreateContext("""
                    {
                      "id": "t", "version": "1.0.0",
                      "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
                      "inputs": { "go": { "effects": [
                        { "op": "rec.set", "target": "$n", "key": "a", "value": 1 } ] } }
                    }
                    """)).Message,
                StringComparison.Ordinal);

        [Fact]
        public void AStateDocumentWithAnUndeclaredKeyIsRejected() =>
            Assert.Contains(
                "is not a key this record declares",
                Assert.Throws<RuleDocumentException>(() => Tally().GetValidInputs("""
                    { "$schema": "rulealize/state/v1", "ruleSet": "tally@1.0.0",
                      "data": { "tally": { "a": 0, "b": 0, "z": 1 } } }
                    """, 8)).Message,
                StringComparison.Ordinal);

        [Fact]
        public void AMissingKeyIsRejectedToo() =>
            Assert.Contains(
                "is missing",
                Assert.Throws<RuleDocumentException>(() => Tally().GetValidInputs("""
                    { "$schema": "rulealize/state/v1", "ruleSet": "tally@1.0.0",
                      "data": { "tally": { "a": 0 } } }
                    """, 8)).Message,
                StringComparison.Ordinal);

        // ── Lists ──────────────────────────────────────────────────────────────

        [Fact]
        public void AListIsASequenceAndTheSequencePluginReadsIt()
        {
            RuleContext context = Log();

            Assert.True(context.GetValidInputs(context.InitialState, 8).Count == 1);
        }

        [Fact]
        public void AppendingAcrossSeveralTransitionsAccumulates()
        {
            // Also the test that a lazy sequence written into the state is settled rather
            // than kept as a way to recompute itself from the state before it. Three
            // appends in a row would otherwise be three chained closures deep.
            RuleContext context = Log();

            string state = context.InitialState;
            for (int i = 0; i < 3; i++)
            {
                state = context.ApplyToState("""{ "input": "note", "args": {} }""", state).State;
            }

            Assert.Equal(["x", "x", "x"], Entries(state));
        }

        [Fact]
        public void AListPastItsBoundIsRejected() =>
            Assert.Contains(
                "at most 2 items",
                Assert.Throws<RuleDocumentException>(() => Log().GetValidInputs("""
                    { "$schema": "rulealize/state/v1", "ruleSet": "log@1.0.0",
                      "data": { "entries": ["x", "x", "x"] } }
                    """, 8)).Message,
                StringComparison.Ordinal);

        [Fact]
        public void AnElementOfTheWrongTypeIsNamedByItsPosition() =>
            Assert.Contains(
                "[1]",
                Assert.Throws<RuleDocumentException>(() => Log().GetValidInputs("""
                    { "$schema": "rulealize/state/v1", "ruleSet": "log@1.0.0",
                      "data": { "entries": ["x", 9] } }
                    """, 8)).Message,
                StringComparison.Ordinal);

        // ── The sequence operations lists asked for ────────────────────────────

        [Fact]
        public void SequencesRunTogether() =>
            Assert.True(Evaluate("""
                { "op": "cmp.eq", "right": 5, "left": { "op": "seq.count", "source": {
                    "op": "seq.concat", "of": [
                      { "op": "seq.of", "of": ["a", "b"] },
                      { "op": "seq.empty" },
                      { "op": "seq.of", "of": ["c", "d", "e"] } ] } } }
                """));

        [Fact]
        public void TakeAndSkipDivideASequence() =>
            Assert.True(Evaluate("""
                { "op": "logic.and", "all": [
                  { "op": "cmp.eq", "right": "b", "left": { "op": "seq.elementAt", "index": 1,
                      "source": { "op": "seq.take", "count": 2,
                                  "source": { "op": "seq.of", "of": ["a", "b", "c"] } } } },
                  { "op": "cmp.eq", "right": "c", "left": { "op": "seq.elementAt", "index": 0,
                      "source": { "op": "seq.skip", "count": 2,
                                  "source": { "op": "seq.of", "of": ["a", "b", "c"] } } } } ] }
                """));

        [Fact]
        public void AskingForMoreThanThereIsIsNotAnError() =>
            // How long a sequence is depends on the position; asking for ten of something
            // that has three is a reasonable question with a short answer.
            Assert.True(Evaluate("""
                { "op": "logic.and", "all": [
                  { "op": "cmp.eq", "right": 3, "left": { "op": "seq.count",
                      "source": { "op": "seq.take", "count": 10, "source": { "op": "seq.of", "of": ["a", "b", "c"] } } } },
                  { "op": "cmp.eq", "right": 0, "left": { "op": "seq.count",
                      "source": { "op": "seq.skip", "count": 10, "source": { "op": "seq.of", "of": ["a", "b", "c"] } } } } ] }
                """));

        [Fact]
        public void ANegativeCountIsAnError() =>
            Assert.Throws<RuleEvaluationException>(() => Evaluate("""
                { "op": "cmp.eq", "right": 0, "left": { "op": "seq.count",
                    "source": { "op": "seq.take", "count": -1, "source": { "op": "seq.empty" } } } }
                """));

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>A rule set whose only field is a record of two counters.</summary>
        private RuleContext Tally() => standard.Runtime.CreateContext("""
            {
              "id": "tally", "version": "1.0.0",
              "state": {
                "schema": { "tally": { "op": "rec.map", "keys": ["a", "b"],
                                       "value": { "op": "type.int", "min": 0 } } },
                "initial": { "tally": { "a": 0, "b": 0 } }
              },
              "inputs": {
                "bump": {
                  "params": { "which": { "domain": { "op": "rec.keys", "of": "$tally" } } },
                  "effects": [
                    { "op": "rec.update", "target": "$tally", "key": "@which", "as": "n",
                      "value": { "op": "math.add", "of": ["@n", 1] } } ]
                },
                "bumpBoth": {
                  "effects": [
                    { "op": "rec.set", "target": "$tally", "key": "a", "value": 1 },
                    { "op": "rec.update", "target": "$tally", "key": "b", "as": "n",
                      "value": { "op": "math.add", "of": ["@n", 1] } } ]
                }
              }
            }
            """);

        /// <summary>A rule set whose only field is a bounded list, appended to one entry at a time.</summary>
        private RuleContext Log() => standard.Runtime.CreateContext("""
            {
              "id": "log", "version": "1.0.0",
              "state": {
                "schema": { "entries": { "op": "type.list", "maxLength": 2,
                                         "element": { "op": "type.string" } } },
                "initial": { "entries": [] }
              },
              "inputs": {
                "note": {
                  "when": { "op": "cmp.lt", "left": { "op": "seq.count", "source": "$entries" }, "right": 99 },
                  "effects": [
                    { "op": "state.set", "path": "entries", "value": {
                        "op": "seq.concat", "of": ["$entries", { "op": "seq.of", "of": ["x"] }] } } ]
                }
              }
            }
            """);

        /// <summary>Evaluates one boolean expression against a rule set holding a record.</summary>
        private bool Evaluate(string expression)
        {
            RuleContext context = standard.Runtime.CreateContext($$"""
                {
                  "id": "probe", "version": "1.0.0",
                  "state": {
                    "schema": { "tally": { "op": "rec.map", "keys": ["a", "b"],
                                           "value": { "op": "type.int", "min": 0 } } },
                    "initial": { "tally": { "a": 0, "b": 0 } }
                  },
                  "inputs": { "go": { "when": {{expression}}, "effects": [] } }
                }
                """);

            return context.GetValidInputs(context.InitialState, 8).Count == 1;
        }

        private static int Count(string state, string key)
        {
            using JsonDocument document = JsonDocument.Parse(state);
            return document.RootElement.GetProperty("data").GetProperty("tally").GetProperty(key).GetInt32();
        }

        private static string[] Entries(string state)
        {
            using JsonDocument document = JsonDocument.Parse(state);
            return [.. document.RootElement.GetProperty("data").GetProperty("entries")
                .EnumerateArray().Select(static entry => entry.GetString()!)];
        }
    }
}
