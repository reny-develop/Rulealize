// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text;
using System.Text.Json;

namespace Rulealize.Tests
{
    /// <summary>Chess, and what writing it says about the DSL.</summary>
    /// <remarks>
    /// <para>
    /// Reversi established that a game could be written in generic vocabulary at all. Chess
    /// was written to press on the two places that looked like they would not hold: a move
    /// whose destination depends on its origin, and a legality rule that is about the
    /// position after the move rather than the position in front of it.
    /// </para>
    /// <para>
    /// The move counts below are perft values, which are published and independently known.
    /// A rule set that agrees with them at depth three is not merely self-consistent.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class ChessTests(StandardRuntime standard)
    {
        private const int Limit = 1000;

        [Fact]
        public void TheOpeningPositionHasTwentyMoves()
        {
            ValidInputSet moves = standard.Chess.GetValidInputs(standard.Chess.InitialState, Limit);

            Assert.Equal(20, moves.Count);
            Assert.False(moves.Truncated);
            Assert.All(moves, static move => Assert.Equal("white", move.Actor));
        }

        [Fact]
        public void ACompoundParameterIsWhatKeepsTheCandidateCountDown()
        {
            // The measurement the whole exercise was for. Written as a from and a to, the
            // candidate space would be 64 x 64 in every position, whatever is on the board;
            // the guard would then have to reject 4076 of them one at a time. Written as one
            // tuple over one domain, the candidates are the moves.
            ValidInputSet opening = standard.Chess.GetValidInputs(standard.Chess.InitialState, Limit);
            ValidInputSet middlegame = standard.Chess.GetValidInputs(Kiwipete, Limit);

            Assert.Equal(20, opening.Evaluated);
            Assert.Equal(20, opening.Count);

            // Kiwipete is a crowded position with both sides able to castle. The guard is
            // reached 48 times, once per pseudo-legal move, and every one of them is legal.
            Assert.Equal(48, middlegame.Evaluated);
            Assert.Equal(48, middlegame.Count);
        }

        [Fact]
        public void AMoveSurvivesTheRoundTripThroughAnInputDocument()
        {
            // GetValidInputs hands back an opaque tuple rendered as text; ApplyToState reads
            // that text back and has to reach the same move. Without a canonical text form
            // there would be no way to name a compound move in a document at all.
            ValidInput move = Single(standard.Chess.InitialState, "e2|e4|2");

            Assert.Equal("e2|e4|2", move.Arguments["m"]);

            TransitionResult after = standard.Chess.ApplyToState(
                move.ToInputDocument(standard.Chess.RuleSet),
                standard.Chess.InitialState);

            Assert.Equal("P", Square(after.State, "e4"));
            Assert.Null(Square(after.State, "e2"));
            Assert.Equal("black", Field(after.State, "turn").GetString());
        }

        [Theory]
        [InlineData(1, 20)]
        [InlineData(2, 400)]
        [InlineData(3, 8902)]
        public void PerftFromTheOpeningPosition(int depth, int expected) =>
            Assert.Equal(expected, Perft(standard.Chess.InitialState, depth));

        [Theory]
        // Kiwipete: the standard position for catching castling, en passant and pin bugs.
        [InlineData(1, 48)]
        [InlineData(2, 2039)]
        public void PerftFromKiwipete(int depth, int expected) =>
            Assert.Equal(expected, Perft(Kiwipete, depth));

        /// <summary>The depths that settle it. Around thirty seconds, so tagged to be skippable.</summary>
        /// <remarks>
        /// <c>--filter Speed!=Slow</c> leaves them out. Nothing here is a different kind of
        /// check from the shallower ones; they are simply deep enough that a rule which is
        /// wrong in some position has to have been reached by now.
        /// </remarks>
        [Theory]
        [Trait("Speed", "Slow")]
        [InlineData(false, 4, 197281)]
        [InlineData(true, 3, 97862)]
        public void DeepPerft(bool kiwipete, int depth, int expected) =>
            Assert.Equal(expected, Perft(kiwipete ? Kiwipete : standard.Chess.InitialState, depth));

        [Fact]
        public void APinnedPieceCannotMove()
        {
            // White king e1, white knight e2, black rook e8. The knight is pinned along the
            // file and has no move at all, though eight squares are empty around it.
            string state = Position("""
                "e1": "K", "e2": "N", "e8": "r", "a1": "Q"
                """, "white");

            Assert.DoesNotContain(
                standard.Chess.GetValidInputs(state, Limit),
                static move => move.Arguments["m"].StartsWith("e2", StringComparison.Ordinal));
        }

        [Fact]
        public void CheckLeavesOnlyTheMovesThatAnswerIt()
        {
            // Black rook on e8 gives check down the open file. Three answers and no others:
            // step off the file, block it on e4, or take the rook on e8. The queen has a
            // dozen other moves and not one of them is legal.
            string state = Position("""
                "e1": "K", "e8": "r", "a4": "Q", "h8": "k"
                """, "white");

            ValidInputSet moves = standard.Chess.GetValidInputs(state, Limit);

            Assert.Equal(
                ["a4|e4|-", "a4|e8|-", "e1|d1|-", "e1|d2|-", "e1|f1|-", "e1|f2|-"],
                moves.Select(static move => move.Arguments["m"]).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void BackRankMateIsTerminalAndNamesTheWinner()
        {
            string state = Position("""
                "h1": "K", "g2": "P", "h2": "P", "a1": "r", "a8": "k"
                """, "white");

            TerminalStatus status = standard.Chess.GetTerminalStatus(state);

            Assert.True(status.IsTerminal);
            Assert.Equal("black", status.Result);
            Assert.Empty(standard.Chess.GetValidInputs(state, Limit));
        }

        [Fact]
        public void StalemateIsTerminalAndIsADraw()
        {
            // Black king a8, white queen c7, white king a1. Black is not in check and has no
            // move.
            string state = Position("""
                "a8": "k", "c7": "Q", "a1": "K"
                """, "black");

            TerminalStatus status = standard.Chess.GetTerminalStatus(state);

            Assert.True(status.IsTerminal);
            Assert.Equal("draw", status.Result);
        }

        [Fact]
        public void APromotionOffersAllFourPieces()
        {
            string state = Position("""
                "e1": "K", "e8": "k", "a7": "P"
                """, "white");

            Assert.Equal(
                ["a7|a8|b", "a7|a8|n", "a7|a8|q", "a7|a8|r"],
                standard.Chess.GetValidInputs(state, Limit)
                    .Select(static move => move.Arguments["m"])
                    .Where(static move => move.StartsWith("a7", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal));
        }

        [Fact]
        public void PromotingPutsTheChosenPieceOnTheBoard()
        {
            string state = Position("""
                "e1": "K", "e8": "k", "a7": "P"
                """, "white");

            TransitionResult after = standard.Chess.ApplyToState(Input("a7|a8|n"), state);

            Assert.Equal("N", Square(after.State, "a8"));
            Assert.Null(Square(after.State, "a7"));
        }

        [Fact]
        public void CastlingMovesTheRookAsWell()
        {
            string state = Position("""
                "e1": "K", "h1": "R", "a1": "R", "e8": "k"
                """, "white", wk: true, wq: true);

            Assert.Contains(
                standard.Chess.GetValidInputs(state, Limit),
                static move => move.Arguments["m"] == "e1|g1|0-0");

            TransitionResult after = standard.Chess.ApplyToState(Input("e1|g1|0-0"), state);

            Assert.Equal("K", Square(after.State, "g1"));
            Assert.Equal("R", Square(after.State, "f1"));
            Assert.Null(Square(after.State, "e1"));
            Assert.Null(Square(after.State, "h1"));
            Assert.False(Field(after.State, "wk").GetBoolean());
            Assert.False(Field(after.State, "wq").GetBoolean());
        }

        [Fact]
        public void CastlingThroughAnAttackedSquareIsRefused()
        {
            // Black rook on f8 covers f1, the square the king would cross. The right is
            // there and so are both pieces; only the crossing is the problem.
            string state = Position("""
                "e1": "K", "h1": "R", "e8": "k", "f8": "r"
                """, "white", wk: true);

            Assert.DoesNotContain(
                standard.Chess.GetValidInputs(state, Limit),
                static move => move.Arguments["m"] == "e1|g1|0-0");

            // The same position without the rook on f8, to show the refusal above was about
            // the attacked square and not about castling being unreachable in general.
            Assert.Contains(
                standard.Chess.GetValidInputs(
                    Position("""
                        "e1": "K", "h1": "R", "e8": "k", "a8": "r"
                        """, "white", wk: true),
                    Limit),
                static move => move.Arguments["m"] == "e1|g1|0-0");
        }

        [Fact]
        public void MovingARookGivesUpThatSideOnly()
        {
            string state = Position("""
                "e1": "K", "h1": "R", "a1": "R", "e8": "k"
                """, "white", wk: true, wq: true);

            TransitionResult after = standard.Chess.ApplyToState(Input("h1|h2|-"), state);

            Assert.False(Field(after.State, "wk").GetBoolean());
            Assert.True(Field(after.State, "wq").GetBoolean());
        }

        [Fact]
        public void ADoubleStepMarksTheSquareItSkipped()
        {
            TransitionResult after = standard.Chess.ApplyToState(Input("e2|e4|2"), standard.Chess.InitialState);

            Assert.Equal("e3", Field(after.State, "ep").GetString());
        }

        [Fact]
        public void TheMarkIsGoneAfterTheNextMove()
        {
            TransitionResult first = standard.Chess.ApplyToState(Input("e2|e4|2"), standard.Chess.InitialState);
            TransitionResult second = standard.Chess.ApplyToState(Input("g8|f6|-"), first.State);

            Assert.Equal(JsonValueKind.Null, Field(second.State, "ep").ValueKind);
        }

        [Fact]
        public void CapturingInPassingRemovesAPawnThatIsNotOnTheDestination()
        {
            // White pawn e5, black pawn d7. Black plays d7-d5 past it; white takes on d6 and
            // the pawn that disappears is the one on d5.
            string state = Position("""
                "e1": "K", "e8": "k", "e5": "P", "d7": "p"
                """, "black");

            TransitionResult opened = standard.Chess.ApplyToState(Input("d7|d5|2"), state);
            Assert.Equal("d6", Field(opened.State, "ep").GetString());

            Assert.Contains(
                standard.Chess.GetValidInputs(opened.State, Limit),
                static move => move.Arguments["m"] == "e5|d6|ep");

            TransitionResult taken = standard.Chess.ApplyToState(Input("e5|d6|ep"), opened.State);

            Assert.Equal("P", Square(taken.State, "d6"));
            Assert.Null(Square(taken.State, "d5"));
            Assert.Null(Square(taken.State, "e5"));
        }

        [Fact]
        public void TheChanceToCaptureInPassingLastsExactlyOneMove()
        {
            string state = Position("""
                "e1": "K", "e8": "k", "e5": "P", "d7": "p", "h1": "R", "h8": "r"
                """, "black");

            TransitionResult opened = standard.Chess.ApplyToState(Input("d7|d5|2"), state);
            TransitionResult elsewhere = standard.Chess.ApplyToState(Input("h1|g1|-"), opened.State);
            TransitionResult back = standard.Chess.ApplyToState(Input("h8|g8|-"), elsewhere.State);

            Assert.DoesNotContain(
                standard.Chess.GetValidInputs(back.State, Limit),
                static move => move.Arguments["m"].EndsWith("|ep", StringComparison.Ordinal));
        }

        [Fact]
        public void AMoveThatWouldLeaveTheKingInCheckIsRefused()
        {
            // What the guard does check. The knight on e2 is pinned by the rook on e8, so
            // moving it is refused however well formed the document is.
            string state = Position("""
                "e1": "K", "e2": "N", "e8": "r", "h8": "k"
                """, "white");

            Assert.Throws<IllegalInputException>(() => standard.Chess.ApplyToState(Input("e2|c3|-"), state));
        }

        [Fact]
        public void AMoveOutsideTheDomainIsRefused()
        {
            // And what the domain does, which is where most of this rule set lives.
            //
            // The movement rules are in the domain — that is what a compound parameter is
            // for — so `when` is only the king-safety filter and a pawn stepping three
            // squares would pass it. It is refused all the same, because an argument has to
            // be a value its domain produces.
            IllegalInputException refused = Assert.Throws<IllegalInputException>(
                () => standard.Chess.ApplyToState(Input("e2|e5|-"), standard.Chess.InitialState));

            Assert.Equal("move", refused.Input);
            Assert.DoesNotContain(
                standard.Chess.GetValidInputs(standard.Chess.InitialState, Limit),
                static move => move.Arguments["m"] == "e2|e5|-");
        }

        [Fact]
        public void EveryMoveTheRulesRefuseIsRefusedTheSameWay()
        {
            // A rule set may state a rule in a domain or in a guard, and a caller cannot tell
            // which from the outside — nor should it have to. Here one of each: a knight's
            // move a bishop cannot make, and a legal-looking move that leaves the king in
            // check.
            string state = Position("""
                "e1": "K", "e2": "N", "e8": "r", "h8": "k", "c1": "B"
                """, "white");

            Assert.Throws<IllegalInputException>(() => standard.Chess.ApplyToState(Input("c1|c3|-"), state));
            Assert.Throws<IllegalInputException>(() => standard.Chess.ApplyToState(Input("e2|c3|-"), state));
        }

        [Fact]
        public void ArgumentsArriveAsTheDomainBuiltThemHoweverTheyWereWritten()
        {
            // The other half of resolving an argument against its domain. A move read from a
            // document is text, and what gets bound is the tuple the domain produced, so the
            // effects see opaque coordinates exactly as the candidate search did.
            //
            // Castling rights are the visible consequence: they are given up by comparing a
            // move's squares against written ones, and that comparison would answer
            // differently if the two paths disagreed about what a square is.
            string state = Position("""
                "e1": "K", "h1": "R", "a1": "R", "e8": "k"
                """, "white", wk: true, wq: true);

            TransitionResult direct = standard.Chess.ApplyToState(Input("e1|d1|-"), state);

            ValidInput listed = Single(state, "e1|d1|-");
            TransitionResult viaSearch = standard.Chess.ApplyToState(
                listed.ToInputDocument(standard.Chess.RuleSet),
                state);

            Assert.Equal(direct.State, viaSearch.State);
            Assert.False(Field(direct.State, "wk").GetBoolean());
            Assert.False(Field(direct.State, "wq").GetBoolean());
        }

        [Fact]
        public void FiftyMovesWithoutAPawnOrACaptureIsADraw()
        {
            string state = Position("""
                "e1": "K", "e8": "k", "a1": "R"
                """, "white", idle: 99);

            TransitionResult after = standard.Chess.ApplyToState(Input("a1|a2|-"), state);

            Assert.True(after.IsTerminal);
            Assert.Equal("draw", after.Result);
        }

        [Fact]
        public void ACaptureResetsTheClock()
        {
            string state = Position("""
                "e1": "K", "e8": "k", "a1": "R", "a7": "r"
                """, "white", idle: 40);

            TransitionResult after = standard.Chess.ApplyToState(Input("a1|a7|-"), state);

            Assert.Equal(0, Field(after.State, "idle").GetInt32());
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>Counts the leaves of the legal move tree to a given depth.</summary>
        /// <remarks>
        /// The reason to count rather than to inspect: a wrong rule that happens to produce
        /// the right number of moves in one position will not produce the right number in
        /// every position reachable in three.
        /// </remarks>
        private int Perft(string state, int depth)
        {
            ValidInputSet moves = standard.Chess.GetValidInputs(state, Limit);
            Assert.False(moves.Truncated);

            if (depth <= 1)
            {
                return moves.Count;
            }

            int total = 0;
            foreach (ValidInput move in moves)
            {
                total += Perft(
                    standard.Chess.ApplyToState(move.ToInputDocument(standard.Chess.RuleSet), state).State,
                    depth - 1);
            }

            return total;
        }

        private ValidInput Single(string state, string move) =>
            Assert.Single(
                standard.Chess.GetValidInputs(state, Limit),
                candidate => candidate.Arguments["m"] == move);

        private string Input(string move) => $$"""
            { "$schema": "rulealize/input/v1", "ruleSet": "chess@1.0.0",
              "input": "move", "args": { "m": "{{move}}" } }
            """;

        private static string Position(
            string squares,
            string turn,
            bool wk = false,
            bool wq = false,
            bool bk = false,
            bool bq = false,
            int idle = 0) => $$"""
            {
              "$schema": "rulealize/state/v1",
              "ruleSet": "chess@1.0.0",
              "data": {
                "board": { {{squares}} },
                "turn": "{{turn}}",
                "wk": {{Json(wk)}}, "wq": {{Json(wq)}}, "bk": {{Json(bk)}}, "bq": {{Json(bq)}},
                "ep": null,
                "idle": {{idle}}
              }
            }
            """;

        /// <summary>Kiwipete, in this rule set's state form.</summary>
        /// <remarks>
        /// <c>r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -</c>, the
        /// position perft suites use because almost every rule that is easy to get wrong is
        /// live in it at once: castling on both wings for both sides, a pinned knight, and
        /// pawn captures that open lines.
        /// </remarks>
        private static string Kiwipete => Position(
            """
            "a8": "r", "e8": "k", "h8": "r",
            "a7": "p", "c7": "p", "d7": "p", "e7": "q", "f7": "p", "g7": "b",
            "a6": "b", "b6": "n", "e6": "p", "f6": "n", "g6": "p",
            "d5": "P", "e5": "N",
            "b4": "p", "e4": "P",
            "c3": "N", "f3": "Q", "h3": "p",
            "a2": "P", "b2": "P", "c2": "P", "d2": "B", "e2": "B", "f2": "P", "g2": "P", "h2": "P",
            "a1": "R", "e1": "K", "h1": "R"
            """,
            "white",
            wk: true,
            wq: true,
            bk: true,
            bq: true);

        private static string Json(bool value) => value ? "true" : "false";

        private static JsonElement Field(string state, string name)
        {
            using JsonDocument document = JsonDocument.Parse(state);
            return Clone(document.RootElement.GetProperty("data").GetProperty(name));
        }

        private static string? Square(string state, string square)
        {
            using JsonDocument document = JsonDocument.Parse(state);
            JsonElement board = document.RootElement.GetProperty("data").GetProperty("board");
            return board.TryGetProperty(square, out JsonElement value) ? value.GetString() : null;
        }

        private static JsonElement Clone(JsonElement element)
        {
            // JsonElement is a view over the document it came from, and the document is about
            // to be disposed.
            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer))
            {
                element.WriteTo(writer);
            }

            using JsonDocument copy = JsonDocument.Parse(Encoding.UTF8.GetString(buffer.ToArray()));
            return copy.RootElement.Clone();
        }
    }
}
