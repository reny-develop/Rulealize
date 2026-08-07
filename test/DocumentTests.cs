// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text;
using System.Text.Json;

namespace Rulealize.Tests
{
    /// <summary>State and input documents, which is where untrusted data gets in.</summary>
    /// <remarks>
    /// A state document is written by hand, or produced by another system, or stored and
    /// replayed months later. Declaring a schema is only worth doing if something checks
    /// documents against it, and only worth using if what comes back names every problem
    /// rather than the first.
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class DocumentTests(StandardRuntime standard)
    {
        private RuleContext Reversi => standard.Reversi;

        [Fact]
        public void EveryViolationIsReportedAtOnce()
        {
            RuleDocumentException exception = Assert.Throws<RuleDocumentException>(
                () => Reversi.GetValidInputs(
                    """{ "data": { "board": { "z9": "black" }, "turn": "green", "passes": 7 } }""",
                    64));

            Assert.Equal(3, exception.Violations.Length);
            Assert.Contains(exception.Violations, static v => v.StartsWith("board.z9", StringComparison.Ordinal));
            Assert.Contains(exception.Violations, static v => v.StartsWith("turn", StringComparison.Ordinal));
            Assert.Contains(exception.Violations, static v => v.StartsWith("passes", StringComparison.Ordinal));
        }

        [Fact]
        public void AViolationInsideABoardNamesTheSquare()
        {
            RuleDocumentException exception = Assert.Throws<RuleDocumentException>(
                () => Reversi.GetValidInputs(
                    """{ "data": { "board": { "d4": "green" }, "turn": "black", "passes": 0 } }""",
                    64));

            Assert.Contains(exception.Violations, static v => v.StartsWith("board.d4", StringComparison.Ordinal));
        }

        [Fact]
        public void AMissingFieldIsAViolation() =>
            Assert.Contains(
                Assert.Throws<RuleDocumentException>(
                    () => Reversi.GetValidInputs("""{ "data": { "turn": "black", "passes": 0 } }""", 64)).Violations,
                static v => v.StartsWith("board", StringComparison.Ordinal));

        [Fact]
        public void AFieldTheSchemaDoesNotDeclareIsAViolation() =>
            Assert.Contains(
                Assert.Throws<RuleDocumentException>(
                    () => Reversi.GetValidInputs(
                        """{ "data": { "board": {}, "turn": "black", "passes": 0, "extra": 1 } }""",
                        64)).Violations,
                static v => v.StartsWith("extra", StringComparison.Ordinal));

        [Fact]
        public void AStateWrittenForAnotherRuleSetIsRefused() =>
            Assert.Contains(
                "shogi@1.0.0",
                Assert.Throws<RuleDocumentException>(
                    () => Reversi.GetValidInputs(
                        """{ "ruleSet": "shogi@1.0.0", "data": { "board": {}, "turn": "black", "passes": 0 } }""",
                        64)).Message,
                StringComparison.Ordinal);

        [Fact]
        public void MalformedJsonIsReportedAsSuch() =>
            Assert.Contains(
                "not valid JSON",
                Assert.Throws<RuleDocumentException>(() => Reversi.GetValidInputs("{ nope", 64)).Message,
                StringComparison.Ordinal);

        [Fact]
        public void AnInputNamingSomethingThatDoesNotExistIsRefused() =>
            Assert.Contains(
                "'fly' is not an input",
                Assert.Throws<RuleDocumentException>(
                    () => Reversi.ApplyToState("""{ "input": "fly", "args": {} }""", Reversi.InitialState))
                    .Message,
                StringComparison.Ordinal);

        [Fact]
        public void AMissingArgumentIsRefused() =>
            Assert.Throws<RuleDocumentException>(
                () => Reversi.ApplyToState("""{ "input": "place", "args": {} }""", Reversi.InitialState));

        [Fact]
        public void AnArgumentTheInputDoesNotTakeIsRefused() =>
            Assert.Throws<RuleDocumentException>(
                () => Reversi.ApplyToState(
                    """{ "input": "place", "args": { "at": "d3", "how": "hard" } }""",
                    Reversi.InitialState));

        [Fact]
        public void AStateDocumentRoundTripsThroughItsOwnOutput()
        {
            // What ApplyToState hands back has to be something the next call accepts.
            string state = Reversi.InitialState;
            ValidInputSet moves = Reversi.GetValidInputs(state, 128);

            Assert.Equal(4, moves.Count);
            Assert.Equal(4, Reversi.GetValidInputs(Reversi.InitialState, 128).Count);
        }

        [Fact]
        public async Task StreamsAreReadAsynchronously()
        {
            // The asynchronous overload exists because reading a document is the one part of
            // this that genuinely is I/O.
            using MemoryStream input = new(Encoding.UTF8.GetBytes(
                """{ "input": "place", "args": { "at": "d3" } }"""));
            using MemoryStream state = new(Encoding.UTF8.GetBytes(Reversi.InitialState));

            TransitionResult result = await Reversi.ApplyToStateAsync(input, state);

            Assert.False(result.IsTerminal);
        }

        [Fact]
        public void TheDocumentedTransitionShapeIsWhatComesOut()
        {
            TransitionResult result = Reversi.ApplyToState(
                """{ "input": "place", "args": { "at": "d3" } }""",
                Reversi.InitialState);

            using JsonDocument document = JsonDocument.Parse(result.ToJson());

            Assert.True(document.RootElement.TryGetProperty("data", out _));
            Assert.False(document.RootElement.GetProperty("terminal").GetBoolean());
            Assert.False(document.RootElement.TryGetProperty("result", out _));
        }

        [Fact]
        public void AnEmptySquareIsLeftOutOfTheBoardEntirely()
        {
            // The sparse form is the grid plugin's choice, and nothing in the core or the
            // state plugin depends on it — but it is what the documents actually contain.
            using JsonDocument document = JsonDocument.Parse(Reversi.InitialState);
            JsonElement board = document.RootElement.GetProperty("data").GetProperty("board");

            Assert.Equal(4, board.EnumerateObject().Count());
            Assert.False(board.TryGetProperty("a1", out _));
        }

        [Fact]
        public void CommentsAreAcceptedInStateDocumentsToo()
        {
            ValidInputSet moves = Reversi.GetValidInputs(
                """
                {
                  "ruleSet": "reversi@1.0.0",
                  "data": {
                    // the opening position
                    "board": { "d4": "white", "e4": "black", "d5": "black", "e5": "white" },
                    "turn": "black",
                    "passes": 0,
                  }
                }
                """,
                128);

            Assert.Equal(4, moves.Count);
        }
    }
}
