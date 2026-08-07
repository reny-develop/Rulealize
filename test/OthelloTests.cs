// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;

namespace Rulealize.Tests
{
    /// <summary>The rule set the whole design was worked out on, played for real.</summary>
    /// <remarks>
    /// Othello is the test that matters most, because the document describing it contains no
    /// Othello-specific vocabulary at all. Everything here — capturing, flipping, passing,
    /// counting at the end — is assembled out of general operations, so a game that plays
    /// correctly is evidence about the whole decomposition and not just about one rule set.
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class OthelloTests(StandardRuntime standard)
    {
        private RuleContext Othello => standard.Othello;

        [Fact]
        public void TheOpeningPositionHasFourStones()
        {
            Board board = Board.Of(Othello.InitialState);

            Assert.Equal("white", board["d4"]);
            Assert.Equal("black", board["e4"]);
            Assert.Equal("black", board["d5"]);
            Assert.Equal("white", board["e5"]);
            Assert.Equal(4, board.Count);
            Assert.Equal("black", board.Turn);
            Assert.Equal(0, board.Passes);
        }

        [Fact]
        public void BlackHasFourOpeningMoves()
        {
            ValidInputSet moves = Othello.GetValidInputs(Othello.InitialState, 128);

            Assert.Equal(
                ["c4", "d3", "e6", "f5"],
                moves.Select(static move => move.Arguments["at"]).Order(StringComparer.Ordinal));

            Assert.All(moves, static move => Assert.Equal("black", move.Actor));
            Assert.All(moves, static move => Assert.Equal("place", move.Input));
        }

        [Fact]
        public void EveryCandidateIsCountedWhetherOrNotItSurvives()
        {
            // Sixty-four squares for place, and one for pass, which takes no parameters.
            ValidInputSet moves = Othello.GetValidInputs(Othello.InitialState, 128);

            Assert.Equal(65, moves.Evaluated);
            Assert.False(moves.Truncated);
        }

        [Fact]
        public void TheLimitTruncatesRatherThanLying()
        {
            ValidInputSet full = Othello.GetValidInputs(Othello.InitialState, 128);
            ValidInputSet clipped = Othello.GetValidInputs(Othello.InitialState, 10);

            Assert.True(clipped.Truncated);
            Assert.Equal(10, clipped.Evaluated);
            Assert.Subset(
                full.Select(static move => move.Arguments["at"]).ToHashSet(StringComparer.Ordinal),
                clipped.Select(static move => move.Arguments["at"]).ToHashSet(StringComparer.Ordinal));
        }

        [Fact]
        public void TheLimitMustBePositive() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => Othello.GetValidInputs(Othello.InitialState, 0));

        [Fact]
        public async Task PlacingAtD3CapturesExactlyOneStone()
        {
            TransitionResult result = await Othello.ApplyToStateAsync(Place("d3"), Othello.InitialState);
            Board board = Board.Of(result.State);

            Assert.Equal("black", board["d3"]);
            Assert.Equal("black", board["d4"]);   // captured
            Assert.Equal("black", board["e4"]);
            Assert.Equal("black", board["d5"]);
            Assert.Equal("white", board["e5"]);   // not on the line, so untouched
            Assert.Equal(5, board.Count);
            Assert.Equal("white", board.Turn);
            Assert.Equal(0, board.Passes);
            Assert.False(result.IsTerminal);
        }

        [Fact]
        public async Task TheCaptureIsComputedFromThePositionBeforeTheStoneWasPlayed()
        {
            // Two effects write the same board: one places the stone, one flips what it
            // captured. Under sequential semantics the second would rescan a board that
            // already had the new stone on it. Exactly one stone must flip here.
            TransitionResult result = await Othello.ApplyToStateAsync(Place("d3"), Othello.InitialState);

            Assert.Equal(4, Board.Of(result.State).CountOf("black"));
            Assert.Equal(1, Board.Of(result.State).CountOf("white"));
        }

        [Fact]
        public async Task AnIllegalPlacementIsRefused()
        {
            IllegalInputException exception = await Assert.ThrowsAsync<IllegalInputException>(
                () => Othello.ApplyToStateAsync(Place("a1"), Othello.InitialState));

            Assert.Equal("place", exception.Input);
        }

        [Fact]
        public async Task PassingIsRefusedWhileAMoveExists() =>
            await Assert.ThrowsAsync<IllegalInputException>(
                () => Othello.ApplyToStateAsync(
                    """{ "input": "pass", "args": {} }""",
                    Othello.InitialState));

        [Theory]
        [InlineData("c4")]
        [InlineData("d3")]
        [InlineData("e6")]
        [InlineData("f5")]
        public async Task AMoveThatCameOutOfGetValidInputsCanBeFedStraightBackIn(string square)
        {
            ValidInput move = Othello.GetValidInputs(Othello.InitialState, 128)
                .Single(m => m.Arguments["at"] == square);

            TransitionResult result = await Othello.ApplyToStateAsync(
                move.ToInputDocument(Othello.RuleSet),
                Othello.InitialState);

            Assert.Equal("black", Board.Of(result.State)[square]);
        }

        [Fact]
        public void TheOpeningIsNotTerminal() => Assert.False(Othello.GetTerminalStatus(Othello.InitialState).IsTerminal);

        [Fact]
        public async Task AGamePlayedToTheEndFillsTheBoardAndNamesAWinner()
        {
            // Always taking the first legal move is enough to reach a real ending, and the
            // arithmetic is checkable: four stones to start, one placed per ply, sixty-four
            // squares. Sixty plies with no passes means every ply was a legal placement.
            string state = Othello.InitialState;
            TerminalStatus status = Othello.GetTerminalStatus(state);
            int plies = 0;

            while (!status.IsTerminal && plies < 200)
            {
                ValidInputSet moves = Othello.GetValidInputs(state, 128);
                Assert.NotEmpty(moves);

                TransitionResult step = await Othello.ApplyToStateAsync(
                    moves[0].ToInputDocument(Othello.RuleSet),
                    state);

                state = step.State;
                status = new TerminalStatus(step.IsTerminal, step.Result);
                plies++;
            }

            Board board = Board.Of(state);

            Assert.True(status.IsTerminal);
            Assert.Equal(60, plies);
            Assert.Equal(64, board.Count);
            Assert.Equal(40, board.CountOf("black"));
            Assert.Equal(24, board.CountOf("white"));
            Assert.Equal("black", status.Result);
        }

        [Fact]
        public void ADeadlockedPositionOffersOnlyAPass()
        {
            ValidInputSet moves = Othello.GetValidInputs(StandardRuntime.Deadlocked(), 128);

            ValidInput only = Assert.Single(moves);
            Assert.Equal("pass", only.Input);
            Assert.Empty(only.Arguments);
            Assert.Equal("white", only.Actor);
        }

        [Fact]
        public async Task OnePassCountsUpAndTwoEndTheGame()
        {
            TransitionResult first = await Othello.ApplyToStateAsync(
                """{ "input": "pass", "args": {} }""",
                StandardRuntime.Deadlocked());

            Assert.False(first.IsTerminal);
            Assert.Equal(1, Board.Of(first.State).Passes);
            Assert.Equal("black", Board.Of(first.State).Turn);

            TransitionResult second = await Othello.ApplyToStateAsync(
                """{ "input": "pass", "args": {} }""",
                first.State);

            Assert.True(second.IsTerminal);
            Assert.Equal(2, Board.Of(second.State).Passes);
            Assert.Equal("white", second.Result);
        }

        [Fact]
        public async Task TheOutcomeIsReportedOnlyOnceTheGameIsOver()
        {
            TransitionResult ongoing = await Othello.ApplyToStateAsync(Place("d3"), Othello.InitialState);

            Assert.Null(ongoing.Result);
        }

        [Fact]
        public void ARuleSetKnowsItsOwnIdentity()
        {
            Assert.Equal("othello", Othello.Id);
            Assert.Equal("1.0.0", Othello.Version);
            Assert.Equal("othello@1.0.0", Othello.RuleSet);
            Assert.Equal<string>(["place", "pass"], Othello.Inputs);
        }

        [Fact]
        public void TheDocumentedShapeOfGetValidInputsIsWhatComesOut()
        {
            using JsonDocument document = JsonDocument.Parse(
                Othello.GetValidInputs(Othello.InitialState, 128).ToJson());

            JsonElement first = document.RootElement[0];

            Assert.Equal("place", first.GetProperty("input").GetString());
            Assert.Equal("black", first.GetProperty("actor").GetString());
            Assert.Equal(JsonValueKind.String, first.GetProperty("args").GetProperty("at").ValueKind);
        }

        private static string Place(string square) =>
            $$"""
            { "$schema": "rulealize/input/v1", "ruleSet": "othello@1.0.0",
              "input": "place", "args": { "at": "{{square}}" } }
            """;

        /// <summary>A state document, read back the lazy way for assertions.</summary>
        private sealed class Board
        {
            private readonly Dictionary<string, string> _squares = new(StringComparer.Ordinal);

            private Board(JsonElement data)
            {
                foreach (JsonProperty square in data.GetProperty("board").EnumerateObject())
                {
                    _squares[square.Name] = square.Value.GetString()!;
                }

                Turn = data.GetProperty("turn").GetString()!;
                Passes = data.GetProperty("passes").GetInt32();
            }

            public string Turn { get; }

            public int Passes { get; }

            public int Count => _squares.Count;

            public string? this[string square] => _squares.GetValueOrDefault(square);

            public static Board Of(string stateDocument)
            {
                using JsonDocument document = JsonDocument.Parse(stateDocument);
                return new Board(document.RootElement.GetProperty("data"));
            }

            public int CountOf(string colour) =>
                _squares.Values.Count(value => string.Equals(value, colour, StringComparison.Ordinal));
        }
    }
}
