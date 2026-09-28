// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction;

namespace Rulealize.Tests
{
    /// <summary>What a rule set says about a position, beyond what may be done to it.</summary>
    /// <remarks>
    /// <para>
    /// Three questions were answerable before this: what is legal, where a move leads, whether
    /// it is over. They are the ones the runtime needs. Everything else a caller wanted about
    /// a position had to be worked out by reading the state document, which puts a second
    /// account of the rules outside the rule set — and two accounts of one thing is the fault
    /// this whole repository is arranged against.
    /// </para>
    /// <para>
    /// A projection is the rule set answering instead, in the vocabulary it is already written
    /// in, about the position and nothing else.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class ProjectionTests(StandardRuntime standard)
    {
        private const string Requires = StandardRuntime.Requires;

        /// <summary>A counter, and three things it is willing to say about itself.</summary>
        private const string Tally = $$"""
            {
              "id": "tally", "version": "1.0.0",
            {{Requires}}
              "state": {
                "schema": {
                  "total": { "op": "type.int", "min": 0, "max": 10 },
                  "who": { "op": "type.string", "maxLength": 20 }
                },
                "initial": { "total": 0, "who": "nobody" }
              },
              "definitions": {
                "left": { "op": "math.sub", "left": 10, "right": "$total" }
              },
              "projections": {
                "remaining": "#left",
                "headline": { "op": "branch.if",
                  "cond": { "op": "cmp.eq", "left": "#left", "right": 0 },
                  "then": "done", "else": "counting" },
                "steps": { "op": "seq.select", "as": "s",
                  "source": { "op": "seq.range", "from": 1, "count": 3 },
                  "select": { "op": "math.add", "of": ["$total", "@s"] } },
                "view": { "left": "#left", "who": "$who",
                  "progress": { "done": "$total", "left": "#left" } }
              },
              "inputs": {
                "add": {
                  "params": { "n": { "domain": { "op": "seq.range", "from": 1, "count": 3 } } },
                  "when": { "op": "cmp.lte",
                    "left": { "op": "math.add", "of": ["$total", "@n"] }, "right": 10 },
                  "effects": [ { "op": "state.set", "path": "total",
                    "value": { "op": "math.add", "of": ["$total", "@n"] } } ]
                }
              }
            }
            """;

        [Fact]
        public void ARuleSetSaysWhatItWillAnswer()
        {
            RuleContext tally = Context();

            Assert.Equal(["remaining", "headline", "steps", "view"], tally.Projections.AsEnumerable());

            // And a rule set that declares none says so rather than leaving a caller to find
            // out by asking.
            Assert.Empty(standard.Countdown.Projections);
        }

        [Fact]
        public void AnAnswerIsAboutThePositionItWasAskedAbout()
        {
            RuleContext tally = Context();

            Assert.Equal("10", tally.Project("remaining", tally.InitialState));

            string after = tally.ApplyToState(Add(tally, 3), tally.InitialState).State;
            Assert.Equal("7", tally.Project("remaining", after));
        }

        [Fact]
        public void AnAnswerMayBeAWholeShape()
        {
            RuleContext tally = Context();

            using JsonDocument answer = JsonDocument.Parse(tally.Project("steps", tally.InitialState));

            // A sequence comes out as an array, so a caller reads what the rule set built
            // rather than a rendering of it.
            Assert.Equal(JsonValueKind.Array, answer.RootElement.ValueKind);
            Assert.Equal([1, 2, 3], answer.RootElement.EnumerateArray().Select(item => item.GetInt32()));
        }

        [Fact]
        public void AnAnswerMayBeARecordBuiltOutOfComputedParts()
        {
            RuleContext tally = Context();

            using JsonDocument answer = JsonDocument.Parse(tally.Project("view", tally.InitialState));
            JsonElement view = answer.RootElement;

            // No plugin is reached for this. An object with no 'op' is not a node, and the
            // value model has exactly one thing it can be, so a rule set assembles a shape
            // out of computed parts in the notation it would write the shape down in.
            Assert.Equal(JsonValueKind.Object, view.ValueKind);
            Assert.Equal(10, view.GetProperty("left").GetInt32());
            Assert.Equal("nobody", view.GetProperty("who").GetString());
            Assert.Equal(0, view.GetProperty("progress").GetProperty("done").GetInt32());
            Assert.Equal(10, view.GetProperty("progress").GetProperty("left").GetInt32());
        }

        [Fact]
        public void TheRuleSetDecidesWhatTheAnswerMeans() =>
            // Nothing in the runtime knows that ten is where this stops. The branch is the
            // document's, and a caller asking gets the document's answer rather than its own
            // reading of the state.
            Assert.Equal("\"counting\"", Context().Project("headline", Context().InitialState));

        [Fact]
        public void AskingForSomethingTheRuleSetDoesNotSayNamesWhatItDoes()
        {
            ArgumentException refused = Assert.Throws<ArgumentException>(
                () => Context().Project("nope", Context().InitialState));

            Assert.Contains("'remaining'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void APositionThisRuleSetWouldNotAcceptIsRefusedHereToo() =>
            // The same check every other method makes of a state document. A projection reads
            // the position, so it is entitled to the same certainty about it.
            Assert.Throws<RuleDocumentException>(
                () => Context().Project("remaining", standard.Reversi.InitialState));

        [Fact]
        public void ADrawMayNotBeWrittenInOne() =>
            // Memoized against a position, so a draw here would answer its first caller and
            // repeat itself to every other one. The same reason it is kept out of a guard.
            Assert.Contains("may only appear inside an input's 'effects'", Assert.Throws<RuleSetBuildException>(
                () => standard.Runtime.CreateContext(Tally.Replace(
                    "\"remaining\": \"#left\"",
                    "\"remaining\": { \"op\": \"chance.pick\", \"of\": { \"op\": \"seq.of\", \"of\": [1, 2] } }",
                    StringComparison.Ordinal))).Message, StringComparison.Ordinal);

        [Fact]
        public void ANameIsReservedTheWayAnInputsIs() =>
            Assert.Contains("may not contain '.'", Assert.Throws<RuleSetBuildException>(
                () => standard.Runtime.CreateContext(Tally.Replace(
                    "\"remaining\":", "\"held.remaining\":", StringComparison.Ordinal))).Message,
                StringComparison.Ordinal);

        [Fact]
        public void AskingTwiceAboutOnePositionGivesOneAnswer()
        {
            // No arguments and no draw, so this is a function of the position. It is the
            // property that lets a caller cache the answer against the state document.
            RuleContext tally = Context();

            Assert.Equal(
                tally.Project("steps", tally.InitialState),
                tally.Project("steps", tally.InitialState));
        }

        private RuleContext Context() => standard.Runtime.CreateContext(Tally);

        private static string Add(RuleContext tally, int n) =>
            tally.GetValidInputs(tally.InitialState, 64)
                .First(move => move.Arguments["n"] == n.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .ToInputDocument(tally.RuleSet);
    }
}
