// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;

namespace Rulealize.Tests
{
    /// <summary>Shogi, and where a compound parameter stops paying for itself.</summary>
    /// <remarks>
    /// Written to settle a prediction: that a move needing more than a pair of squares would
    /// make a tuple unreadable, and that shogi would be where it happened. What the game
    /// actually asks for turned out to be somewhere else entirely — see
    /// <c>doc/dsl-example-shogi.md</c>.
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class ShogiTests(StandardRuntime standard)
    {
        private const int Limit = 4000;

        [Fact]
        public void TheOpeningPositionHasThirtyMoves()
        {
            ValidInputSet moves = standard.Shogi.GetValidInputs(standard.Shogi.InitialState, Limit);

            Assert.Equal(30, moves.Count);
            Assert.False(moves.Truncated);
            Assert.All(moves, static move => Assert.Equal("black", move.Actor));
            Assert.All(moves, static move => Assert.Equal("move", move.Input));
        }

        [Theory]
        [InlineData(1, 30)]
        [InlineData(2, 900)]
        public void PerftFromTheOpeningPosition(int depth, int expected) =>
            Assert.Equal(expected, Perft(standard.Shogi.InitialState, depth));

        /// <summary>The depth that settles the movement rules, kept out of the ordinary run.</summary>
        [Theory]
        [Trait("Speed", "Slow")]
        [InlineData(3, 25470)]
        public void DeepPerft(int depth, int expected) =>
            Assert.Equal(expected, Perft(standard.Shogi.InitialState, depth));

        [Fact]
        public void ACompoundMoveAndAPairOfPlainParametersLiveInOneRuleSet()
        {
            // The measurement the rule set was written for. Moves are one parameter because
            // 81 x 81 x 2 would be 13,122 candidates in every position; drops are two
            // parameters because a hand and a set of empty squares have nothing to say to
            // each other, and their product is small enough to sift.
            string state = Position(
                """
                "e1": "K", "e9": "k", "e5": "P"
                """,
                "black",
                bG: 1);

            ValidInputSet plays = standard.Shogi.GetValidInputs(state, Limit);

            Assert.Equal(["drop", "move"], plays.Select(static play => play.Input).Distinct().Order(StringComparer.Ordinal));

            // A gold in hand and 78 empty squares. Every one of them is a legal drop, and
            // the search reached exactly that many candidates plus the board moves.
            Assert.Equal(78, plays.Count(static play => play.Input == "drop"));
        }

        [Fact]
        public void TheTwoShapesCostWhatTheyLookLikeTheyCost()
        {
            // Moves, as one parameter: the candidates are the moves. Written as from, to and
            // promote they would be 81 x 81 x 2 = 13,122 in every position on the board.
            ValidInputSet opening = standard.Shogi.GetValidInputs(standard.Shogi.InitialState, Limit);
            Assert.Equal(30, opening.Evaluated);

            // Drops, as two parameters whose domains are independent: the candidates are the
            // product, and the product is small because both domains are already the answer
            // to a question about the state. Seven kinds in hand, 78 empty squares.
            string state = Position(
                """
                "e1": "K", "e9": "k", "e5": "P"
                """,
                "black",
                bP: 1, bL: 1, bN: 1, bS: 1, bG: 1, bB: 1, bR: 1);

            ValidInputSet plays = standard.Shogi.GetValidInputs(state, Limit);

            // Six board moves — five king steps and a pawn — plus seven kinds across every
            // empty square. The guard is reached for all of them.
            Assert.Equal(6 + (7 * 78), plays.Evaluated);

            // What it turns away: a pawn or a lance on the last rank (8 empty squares each),
            // a knight on either of the last two (8 + 9), and a pawn anywhere on the file
            // that already holds one (6). The rest stand.
            Assert.Equal((7 * 78) - (8 + 8 + 17 + 6), plays.Count(static play => play.Input == "drop"));
        }

        [Fact]
        public void AMoveIsThreeComponentsAndNoMore()
        {
            // The prediction under test. A shogi move is an origin, a destination and
            // whether it promotes: the same three a chess move needed, and the drop that
            // looked like it would demand a fourth is a separate input instead.
            ValidInput move = Single(standard.Shogi.InitialState, "e3|e4|-");

            Assert.Equal("e3|e4|-", move.Arguments["m"]);
            Assert.Single(move.Arguments);
        }

        [Fact]
        public void ADropNamesItsPartsBecauseItsParametersAreSeparate()
        {
            string state = Position("""
                "e1": "K", "e9": "k"
                """, "black", bG: 1);

            ValidInput drop = Assert.Single(
                standard.Shogi.GetValidInputs(state, Limit),
                play => play.Input == "drop" && play.Arguments["to"] == "e5");

            Assert.Equal("G", drop.Arguments["piece"]);
            Assert.Equal("e5", drop.Arguments["to"]);
        }

        [Fact]
        public void CapturingPutsThePieceInHandUnpromoted()
        {
            // A promoted rook taken is a rook in hand, not a dragon.
            string state = Position("""
                "e1": "K", "e9": "k", "d1": "S", "d2": "+r"
                """, "black");

            TransitionResult after = standard.Shogi.ApplyToState(Move("d1|d2|-"), state);

            Assert.Equal("S", Square(after.State, "d2"));
            Assert.Equal(1, Held(after.State, "black", "R"));
            Assert.Equal(0, Held(after.State, "black", "P"));
        }

        [Fact]
        public void PromotionIsOfferedBothWaysInTheZone()
        {
            string state = Position("""
                "e1": "K", "a9": "k", "e6": "S"
                """, "black");

            // Entering the zone from e6 to e7: the silver may promote or not, and both
            // sideways-forward squares are in the zone too.
            Assert.Equal(
                ["e6|e7|+", "e6|e7|-"],
                standard.Shogi.GetValidInputs(state, Limit)
                    .Select(static play => play.Arguments["m"])
                    .Where(static move => move is not null && move.StartsWith("e6|e7", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal));
        }

        [Fact]
        public void PromotionIsNotOfferedOutsideTheZone()
        {
            string state = Position("""
                "e1": "K", "a9": "k", "e5": "S"
                """, "black");

            Assert.DoesNotContain(
                standard.Shogi.GetValidInputs(state, Limit),
                static play => play.Arguments["m"]?.EndsWith("|+", StringComparison.Ordinal) == true);
        }

        [Fact]
        public void APawnReachingTheLastRankMustPromote()
        {
            string state = Position("""
                "e1": "K", "a9": "k", "e8": "P"
                """, "black");

            Assert.Equal(
                ["e8|e9|+"],
                standard.Shogi.GetValidInputs(state, Limit)
                    .Select(static play => play.Arguments["m"])
                    .Where(static move => move is not null && move.StartsWith("e8", StringComparison.Ordinal)));
        }

        [Fact]
        public void AKnightWithTwoRanksLeftMayStillChoose()
        {
            // From g7 a black knight reaches f9 and h9, where it would be stranded, and
            // nothing else. Both are promotions only.
            string state = Position("""
                "e1": "K", "a9": "k", "g7": "N"
                """, "black");

            Assert.Equal(
                ["g7|f9|+", "g7|h9|+"],
                standard.Shogi.GetValidInputs(state, Limit)
                    .Select(static play => play.Arguments["m"])
                    .Where(static move => move is not null && move.StartsWith("g7", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal));
        }

        [Fact]
        public void APromotedPieceMovesLikeAGold()
        {
            // A promoted pawn on e5 has the six gold moves and no more; an unpromoted pawn
            // there would have one.
            string state = Position("""
                "e1": "K", "a9": "k", "e5": "+P"
                """, "black");

            Assert.Equal(
                6,
                standard.Shogi.GetValidInputs(state, Limit)
                    .Count(static play => play.Arguments["m"]?.StartsWith("e5", StringComparison.Ordinal) == true));
        }

        [Fact]
        public void TwoUnpromotedPawnsMayNotShareAFile()
        {
            string state = Position("""
                "e1": "K", "a9": "k", "e4": "P"
                """, "black", bP: 1);

            IEnumerable<string?> squares = standard.Shogi.GetValidInputs(state, Limit)
                .Where(static play => play.Input == "drop")
                .Select(static play => play.Arguments["to"]);

            Assert.DoesNotContain("e7", squares);
            Assert.Contains("d7", squares);
        }

        [Fact]
        public void APromotedPawnDoesNotBlockTheFile()
        {
            string state = Position("""
                "e1": "K", "a9": "k", "e4": "+P"
                """, "black", bP: 1);

            Assert.Contains(
                standard.Shogi.GetValidInputs(state, Limit),
                static play => play.Input == "drop" && play.Arguments["to"] == "e7");
        }

        [Fact]
        public void APieceMayNotBeDroppedWhereItCouldNeverMove()
        {
            string state = Position("""
                "e1": "K", "a9": "k"
                """, "black", bP: 1, bN: 1, bG: 1);

            List<(string? Piece, string? To)> drops = [.. standard.Shogi.GetValidInputs(state, Limit)
                .Where(static play => play.Input == "drop")
                .Select(static play => (play.Arguments["piece"], play.Arguments["to"]))];

            Assert.DoesNotContain(("P", "e9"), drops);   // a pawn on the last rank
            Assert.DoesNotContain(("N", "e9"), drops);   // a knight on either of the last two
            Assert.DoesNotContain(("N", "e8"), drops);
            Assert.Contains(("N", "e7"), drops);
            Assert.Contains(("G", "e9"), drops);         // a gold is never stranded
        }

        [Fact]
        public void APawnMayNotBeDroppedToGiveMate()
        {
            // White king alone in the corner on a9, not in check. Its three escapes are
            // covered without being attacked: the gold on c9 holds b9, the silver on c7
            // holds b8, and the knight on b6 defends a8 so the king cannot take what lands
            // there. A pawn dropped on a8 is therefore mate — and a pawn may not do that.
            // A silver dropped on the same square is the same mate and is perfectly legal.
            string state = Position("""
                "e1": "K", "a9": "k", "c9": "G", "c7": "S", "b6": "N"
                """, "black", bP: 1, bS: 1);

            IEnumerable<ValidInput> drops = standard.Shogi.GetValidInputs(state, Limit)
                .Where(static play => play.Input == "drop");

            Assert.DoesNotContain(drops, static drop => drop.Arguments["piece"] == "P" && drop.Arguments["to"] == "a8");
            Assert.Contains(drops, static drop => drop.Arguments["piece"] == "S" && drop.Arguments["to"] == "a8");
        }

        [Fact]
        public void APawnMayBeDroppedToGiveCheckThatIsNotMate()
        {
            // The same position with the gold taken away, so b9 is open. The drop is still
            // check, the king walks out of the corner, and the pawn is allowed. One square's
            // difference between this and the test above is the whole of the rule.
            string state = Position("""
                "e1": "K", "a9": "k", "c7": "S", "b6": "N"
                """, "black", bP: 1);

            Assert.Contains(
                standard.Shogi.GetValidInputs(state, Limit).Where(static play => play.Input == "drop"),
                static drop => drop.Arguments["piece"] == "P" && drop.Arguments["to"] == "a8");
        }

        [Fact]
        public void CheckMustBeAnswered()
        {
            // Black rook on e8 attacks the white king down the file. The rook is undefended,
            // so taking it answers the check as well as stepping aside; d8 and f8 do not,
            // because the rook covers its own rank.
            string state = Position("""
                "e1": "K", "e9": "k", "e8": "R"
                """, "white");

            Assert.Equal(
                ["e9|d9|-", "e9|e8|-", "e9|f9|-"],
                standard.Shogi.GetValidInputs(state, Limit)
                    .Select(static play => play.Arguments["m"])
                    .Order(StringComparer.Ordinal));
        }

        [Fact]
        public void MateIsTerminalAndNamesTheWinner()
        {
            // Gold on d8 and rook on e8: the king cannot take the gold, which is defended by
            // the rook, and every square it could reach is covered.
            string state = Position("""
                "e1": "K", "e9": "k", "e8": "G", "e7": "R"
                """, "white");

            TerminalStatus status = standard.Shogi.GetTerminalStatus(state);

            Assert.True(status.IsTerminal);
            Assert.Equal("black", status.Result);
            Assert.Empty(standard.Shogi.GetValidInputs(state, Limit));
        }

        [Fact]
        public void ADropCanAnswerCheckByInterposing()
        {
            // A lance checks along the file from a1; a gold dropped between answers it.
            // Nothing here is mate, and the point is that drops count as plays.
            string state = Position("""
                "a1": "L", "a9": "k", "i1": "K"
                """, "white", wG: 1);

            Assert.Contains(
                standard.Shogi.GetValidInputs(state, Limit),
                static play => play.Input == "drop" && play.Arguments["piece"] == "G");

            Assert.False(standard.Shogi.GetTerminalStatus(state).IsTerminal);
        }

        [Fact]
        public void ADroppedPieceLeavesTheHand()
        {
            string state = Position("""
                "e1": "K", "a9": "k"
                """, "black", bG: 2, bS: 1);

            TransitionResult after = standard.Shogi.ApplyToState(Drop("G", "e5"), state);

            Assert.Equal("G", Square(after.State, "e5"));
            Assert.Equal(1, Held(after.State, "black", "G"));
            Assert.Equal(1, Held(after.State, "black", "S"));
            Assert.Equal("white", Field(after.State, "turn").GetString());
        }

        [Fact]
        public void ADropOutsideItsDomainIsRefused()
        {
            // Nothing in hand, so the domain of 'piece' is empty and every drop is refused —
            // by the domain, since the guard never sees it.
            Assert.Throws<IllegalInputException>(
                () => standard.Shogi.ApplyToState(Drop("G", "e5"), standard.Shogi.InitialState));
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private int Perft(string state, int depth)
        {
            ValidInputSet plays = standard.Shogi.GetValidInputs(state, Limit);
            Assert.False(plays.Truncated);

            if (depth <= 1)
            {
                return plays.Count;
            }

            int total = 0;
            foreach (ValidInput play in plays)
            {
                total += Perft(
                    standard.Shogi.ApplyToState(play.ToInputDocument(standard.Shogi.RuleSet), state).State,
                    depth - 1);
            }

            return total;
        }

        private ValidInput Single(string state, string move) =>
            Assert.Single(standard.Shogi.GetValidInputs(state, Limit), play => play.Arguments["m"] == move);

        private static string Move(string move) => $$"""
            { "$schema": "rulealize/input/v1", "ruleSet": "shogi@1.0.0",
              "input": "move", "args": { "m": "{{move}}" } }
            """;

        private static string Drop(string piece, string to) => $$"""
            { "$schema": "rulealize/input/v1", "ruleSet": "shogi@1.0.0",
              "input": "drop", "args": { "piece": "{{piece}}", "to": "{{to}}" } }
            """;

        private static string Position(
            string squares,
            string turn,
            int bP = 0,
            int bL = 0,
            int bN = 0,
            int bS = 0,
            int bG = 0,
            int bB = 0,
            int bR = 0,
            int wG = 0) => $$"""
            {
              "$schema": "rulealize/state/v1",
              "ruleSet": "shogi@1.0.0",
              "data": {
                "board": { {{squares}} },
                "turn": "{{turn}}",
                "hand": {
                  "black": { "P": {{bP}}, "L": {{bL}}, "N": {{bN}}, "S": {{bS}}, "G": {{bG}}, "B": {{bB}}, "R": {{bR}} },
                  "white": { "P": 0, "L": 0, "N": 0, "S": 0, "G": {{wG}}, "B": 0, "R": 0 }
                }
              }
            }
            """;

        private static JsonElement Field(string state, string name)
        {
            using JsonDocument document = JsonDocument.Parse(state);
            return document.RootElement.GetProperty("data").GetProperty(name).Clone();
        }

        private static int Held(string state, string colour, string kind)
        {
            using JsonDocument document = JsonDocument.Parse(state);
            return document.RootElement
                .GetProperty("data").GetProperty("hand").GetProperty(colour).GetProperty(kind)
                .GetInt32();
        }

        private static string? Square(string state, string square)
        {
            using JsonDocument document = JsonDocument.Parse(state);
            JsonElement board = document.RootElement.GetProperty("data").GetProperty("board");
            return board.TryGetProperty(square, out JsonElement value) ? value.GetString() : null;
        }
    }
}
