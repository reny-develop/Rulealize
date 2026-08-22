// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;

namespace Rulealize.Tests
{
    /// <summary>The rule set that turns on something nobody chose.</summary>
    /// <remarks>
    /// <para>
    /// Reversi, chess, shogi, the roster and the pipeline are all decided by whoever moves.
    /// Blackjack is not: the card that comes off the deck settles the hand and no player
    /// picked it, so <c>hit</c> is one candidate whose thirteen outcomes are the ranks that
    /// could arrive.
    /// </para>
    /// <para>
    /// The last section plays the same hand through
    /// <see cref="StandardRuntime.BlackjackChoice"/>, which is the same game with the card
    /// written as a parameter instead. Two spellings held to one set of answers is a stronger
    /// check than either could be alone, and it is what keeps the pair from drifting.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class BlackjackTests(StandardRuntime standard)
    {
        private const int Limit = 500;

        /// <summary>The order the deal comes out in: two to each seat, then two to the dealer.</summary>
        /// <remarks>ann 16, bo 17, cy 17, dealer showing 15.</remarks>
        private static readonly string[] Opening = ["T", "6", "9", "8", "Q", "7", "J", "5"];

        private RuleContext Rules => standard.Blackjack;

        // ── One move, thirteen outcomes ────────────────────────────────────────

        [Fact]
        public void NothingCanHappenBeforeTheCardsAreOut()
        {
            ValidInputSet opening = Inputs(Rules.InitialState);

            ValidInput deal = Assert.Single(opening);
            Assert.Equal("dealSeat", deal.Input);

            // No actor. Nobody decides this, and the rule set names nobody.
            Assert.Null(deal.Actor);
        }

        [Fact]
        public void TheOpeningDealIsThirteenThingsThatCouldHappen()
        {
            OutcomeSet outcomes = Outcomes(Rules.InitialState, "dealSeat");

            Assert.Equal(13, outcomes.Count);
            Assert.All(outcomes, static outcome => Assert.Equal(1.0 / 13, outcome.Probability, 12));
            Assert.Equal(1, outcomes.Coverage, 12);
            Assert.False(outcomes.Truncated);
        }

        [Fact]
        public void ARankTheShoeRanOutOfCannotComeOutAgain()
        {
            // Four aces off the top. What is left cannot produce a fifth, and the weight of a
            // rank with none left is zero, which is not an outcome of probability zero — it is
            // not an outcome.
            string table = Deal(Rules.InitialState, "A", "A", "A", "A");
            OutcomeSet outcomes = Outcomes(table, "dealSeat");

            Assert.Equal(12, outcomes.Count);
            Assert.DoesNotContain(outcomes, static outcome => outcome.Draws.Single() == "A");
        }

        [Fact]
        public void ARankWithOneLeftIsAQuarterAsLikelyAsOneWithFour()
        {
            // The whole point of the exercise, and the thing the other spelling of this
            // document cannot say. Three of the four tens are gone and the aces have not been
            // touched, so a ten is a quarter as likely — and the answer says so rather than
            // leaving a caller to go back to the state and work it out.
            string table = Deal(Rules.InitialState, "T", "T", "T", "2");
            OutcomeSet outcomes = Outcomes(table, "dealSeat");

            Assert.Equal(1.0 / 48, Probability(outcomes, "T"), 12);
            Assert.Equal(4.0 / 48, Probability(outcomes, "A"), 12);
            Assert.Equal(3.0 / 48, Probability(outcomes, "2"), 12);
        }

        [Fact]
        public void TheDealGoesRoundTheSeatsAndThenToTheDealer()
        {
            string table = Deal(Rules.InitialState, Opening);

            Assert.Equal(["T", "6"], Cards(table, "ann"));
            Assert.Equal(["9", "8"], Cards(table, "bo"));
            Assert.Equal(["Q", "7"], Cards(table, "cy"));
            Assert.Equal(["J", "5"], DealerCards(table));
        }

        [Fact]
        public void PlayBelongsToTheFirstSeatAndEveryCandidateSaysSo()
        {
            ValidInputSet moves = Inputs(Table());

            Assert.All(moves, static move => Assert.Equal("ann", move.Actor));
            Assert.Equal(["double", "hit", "stand"], Names(moves));
        }

        [Fact]
        public void OneDecisionIsOneMoveAndThirteenOutcomes()
        {
            // Hitting is one thing ann decides, and it is one candidate. Which card turns up
            // is not hers and is thirteen outcomes. Standing is the control: nothing is drawn,
            // so it is one candidate with one outcome, and a caller loops over both the same
            // way.
            string table = Table();

            Assert.Single(Inputs(table), static move => move.Input == "hit");
            Assert.Equal(13, Outcomes(table, "hit").Count);

            Assert.Single(Inputs(table), static move => move.Input == "stand");
            Assert.Single(Outcomes(table, "stand"));
        }

        [Fact]
        public void WhatOneTurnCostsToEnumerate()
        {
            // Three decisions, three candidates, seven guards evaluated to find them. The
            // other spelling of this document answers the same question with twenty-seven
            // candidates and fifty-four guards, because every card is a move there.
            ValidInputSet moves = Inputs(Table());

            Assert.Equal(3, moves.Count);
            Assert.Equal(7, moves.Evaluated);
        }

        // ── The game ───────────────────────────────────────────────────────────

        [Fact]
        public void ASeatThatGoesBustDoesNotActAgain()
        {
            // ann is on sixteen and takes a king. Nothing in the state says twenty-six — the
            // cards say it, and the turn moving on is how a caller finds out.
            string table = Hit(Table(), "K");

            Assert.Equal(["T", "6", "K"], Cards(table, "ann"));
            Assert.Equal("bo", Acting(table));
        }

        [Fact]
        public void StandingPassesTheTurnOn()
        {
            string table = Stand(Table());

            Assert.True(Seat(table, "ann").GetProperty("stood").GetBoolean());
            Assert.Equal("bo", Acting(table));
        }

        [Fact]
        public void ANaturalNeitherActsNorNeedsTo()
        {
            // ann is dealt twenty-one in two. There is nothing she can do with it that is not
            // a mistake, so the rules do not ask — bo is up.
            string table = Deal(Rules.InitialState, "A", "K", "9", "8", "Q", "7", "J", "5");

            Assert.Equal("bo", Acting(table));
        }

        [Fact]
        public void DoublingTakesOneCardAndEndsTheHand()
        {
            string table = Double(Table(), "5");

            Assert.Equal(["T", "6", "5"], Cards(table, "ann"));
            Assert.True(Seat(table, "ann").GetProperty("stood").GetBoolean());
            Assert.Equal(20, Seat(table, "ann").GetProperty("bet").GetInt32());
            Assert.Equal("bo", Acting(table));
        }

        [Fact]
        public void DoublingIsGoneOnceAThirdCardIsOut()
        {
            string table = Hit(Table(), "2");

            Assert.Equal("ann", Acting(table));
            Assert.Equal(["hit", "stand"], Names(Inputs(table)));
        }

        [Fact]
        public void TheDealerDrawsUntilSeventeen()
        {
            // Everyone stands, and the dealer is showing fifteen.
            string table = Stand(Stand(Stand(Table())));

            Assert.Equal(["dealerHit"], Names(Inputs(table)));
            Assert.All(Inputs(table), static draw => Assert.Equal("dealer", draw.Actor));

            // A six makes it twenty-one and the dealer is finished.
            Assert.Equal(["settle"], Names(Inputs(DealerHit(table, "6"))));
        }

        [Fact]
        public void TheDealerDoesNotPlayToATableThatHasAlreadyBusted()
        {
            // Three kings, three busts, and the dealer never turns a card: there is nobody
            // left to beat.
            string table = Hit(Hit(Hit(Table(), "K"), "K"), "K");

            Assert.Equal(["J", "5"], DealerCards(table));
            Assert.Equal(["settle"], Names(Inputs(table)));
        }

        [Fact]
        public void SettlingIsNotSomethingThatCouldGoEitherWay() =>
            // The two inputs with no card in them, checked as such: one outcome each, so the
            // traversal over this rule set is the same loop over every input it applies.
            Assert.Single(Outcomes(Hit(Hit(Hit(Table(), "K"), "K"), "K"), "settle"));

        [Fact]
        public void EverySeatIsSettledAtOnceAndTheHandIsOver()
        {
            string table = Settle(Hit(Hit(Hit(Table(), "K"), "K"), "K"));
            TerminalStatus finished = Rules.GetTerminalStatus(table);

            Assert.Equal(["lose", "lose", "lose"], Results(table));
            Assert.True(finished.IsTerminal);
            Assert.Equal("house", finished.Result);
            Assert.Empty(Inputs(table));
        }

        [Fact]
        public void TheDealerBeatsWhatItOutdrawsAndBustsToEverybody()
        {
            // bo and cy stand on seventeen, the dealer draws to twenty and ann is out.
            string table = Settle(DealerHit(Stand(Stand(Hit(Table(), "K"))), "5"));

            Assert.Equal(["J", "5", "5"], DealerCards(table));
            Assert.Equal(["lose", "lose", "lose"], Results(table));
            Assert.Equal("house", Rules.GetTerminalStatus(table).Result);
        }

        [Fact]
        public void ANaturalIsPaidUnlessTheDealerHasOneToo()
        {
            // ann twenty-one in two, dealer twenty-one in two. Nobody wins that.
            string pushed = Settle(Stand(Stand(
                Deal(Rules.InitialState, "A", "Q", "9", "8", "Q", "7", "A", "K"))));

            Assert.Equal(["push", "lose", "lose"], Results(pushed));

            // The same hand against a dealer who has to work for it.
            string paid = Settle(DealerHit(Stand(Stand(
                Deal(Rules.InitialState, "A", "Q", "9", "8", "Q", "7", "J", "5"))), "6"));

            Assert.Equal(["blackjack", "lose", "lose"], Results(paid));
        }

        [Fact]
        public void ASeatThatOutdrawsTheDealerIsPaidAndATieIsAPush()
        {
            // ann draws to nineteen and stands on it, bo and cy stand on seventeen, and the
            // dealer stops on seventeen. Three stands, because a hit that does not bust leaves
            // the same seat holding the turn.
            string table = Settle(DealerHit(Stand(Stand(Stand(Hit(Table(), "3")))), "2"));

            Assert.Equal(["J", "5", "2"], DealerCards(table));
            Assert.Equal(["win", "push", "push"], Results(table));
            Assert.Equal("players", Rules.GetTerminalStatus(table).Result);
        }

        [Fact]
        public void AnAceIsElevenUntilItCannotBe()
        {
            // ann holds an ace and a six: soft seventeen. A nine takes the eleven back and
            // leaves her on sixteen rather than busting her out at twenty-six — she is still
            // the one to act. A six after that is twenty-two however the ace is counted, and
            // the turn moves on.
            string soft = Deal(Rules.InitialState, "A", "6", "9", "8", "Q", "7", "J", "5");

            Assert.Equal("ann", Acting(Hit(soft, "9")));
            Assert.Equal("bo", Acting(Hit(Hit(soft, "9"), "6")));
        }

        // ── The same game, spelled the other way ───────────────────────────────

        [Fact]
        public void BothSpellingsDealTheSameThirteenHands()
        {
            // One document lists thirteen moves and the other lists one move with thirteen
            // outcomes. What comes out the far end has to be the same thirteen tables.
            IEnumerable<string> chosen = standard.BlackjackChoice
                .GetValidInputs(standard.BlackjackChoice.InitialState, Limit)
                .Select(move => standard.BlackjackChoice
                    .ApplyToState(move.ToInputDocument(standard.BlackjackChoice.RuleSet),
                                  standard.BlackjackChoice.InitialState).State)
                .Select(Data);

            IEnumerable<string> drawn = Outcomes(Rules.InitialState, "dealSeat")
                .Select(outcome => Data(outcome.Result.State));

            Assert.Equal(chosen.Order(StringComparer.Ordinal), drawn.Order(StringComparer.Ordinal));
        }

        [Fact]
        public void OnlyOneOfThemSaysHowLikelyEachOfThoseIs()
        {
            // The difference, stated as the thing the control cannot do. Both documents offer
            // thirteen tables after three tens are gone; one of them knows that a fourth ten
            // is a quarter as likely as an ace, and the other has nowhere to put that.
            string chosen = Play(standard.BlackjackChoice, ("dealSeat", "T"), ("dealSeat", "T"),
                ("dealSeat", "T"), ("dealSeat", "2"));
            string drawn = Deal(Rules.InitialState, "T", "T", "T", "2");

            Assert.Equal(13, standard.BlackjackChoice.GetValidInputs(chosen, Limit).Count);
            Assert.Equal(13, Outcomes(drawn, "dealSeat").Count);
            Assert.Equal(1.0 / 48, Probability(Outcomes(drawn, "dealSeat"), "T"), 12);
        }

        [Fact]
        public void APlayedHandIsTheSameHandInBothDocuments()
        {
            // ann busts, bo and cy stand, the dealer draws to twenty, everybody settles.
            (string Input, string? Card)[] hand =
            [
                ("dealSeat", "T"), ("dealSeat", "6"), ("dealSeat", "9"), ("dealSeat", "8"),
                ("dealSeat", "Q"), ("dealSeat", "7"), ("dealDealer", "J"), ("dealDealer", "5"),
                ("hit", "K"), ("stand", null), ("stand", null), ("dealerHit", "5"), ("settle", null)
            ];

            Assert.Equal(Data(Play(standard.BlackjackChoice, hand)), Data(Play(Rules, hand)));
        }

        // ── Driving it ─────────────────────────────────────────────────────────

        /// <summary>The opening deal, played out.</summary>
        private string Table() => Deal(Rules.InitialState, Opening);

        private string Deal(string state, params string[] cards)
        {
            foreach (string card in cards)
            {
                state = Apply(Rules, state, DealInput(Rules, state), card);
            }

            return state;
        }

        private string Hit(string state, string card) => Apply(Rules, state, "hit", card);

        private string Double(string state, string card) => Apply(Rules, state, "double", card);

        private string DealerHit(string state, string card) => Apply(Rules, state, "dealerHit", card);

        private string Stand(string state) => Apply(Rules, state, "stand", null);

        private string Settle(string state) => Apply(Rules, state, "settle", null);

        /// <summary>Plays a script through either spelling of the game.</summary>
        private static string Play(RuleContext rules, params (string Input, string? Card)[] script)
        {
            string state = rules.InitialState;
            foreach ((string input, string? card) in script)
            {
                state = Apply(rules, state, input, card);
            }

            return state;
        }

        /// <summary>Applies one step, putting the card wherever this rule set takes one.</summary>
        /// <remarks>
        /// The whole of the difference between the two documents, in one branch. Against the
        /// drawn version the card is an outcome — what happened — and the input document says
        /// only what was decided. Against the choice version it is an argument, and the two
        /// travel together as though somebody had picked the card.
        /// </remarks>
        private static string Apply(RuleContext rules, string state, string input, string? card)
        {
            bool drawn = rules.Id == "blackjack";
            string document = $$"""
                { "$schema": "rulealize/input/v1", "ruleSet": "{{rules.RuleSet}}", "input": "{{input}}",
                  "args": {{(card is null || drawn ? "{}" : $$"""{ "card": "{{card}}" }""")}} }
                """;

            if (!drawn)
            {
                return rules.ApplyToState(document, state).State;
            }

            string outcome = $$"""
                { "$schema": "rulealize/outcome/v1", "ruleSet": "{{rules.RuleSet}}", "input": "{{input}}",
                  "draws": {{(card is null ? "[]" : $$"""["{{card}}"]""")}} }
                """;

            return rules.ApplyToState(document, state, outcome).State;
        }

        /// <summary>Which deal is legal here — a seat still wants cards, or the dealer does.</summary>
        private static string DealInput(RuleContext rules, string state) =>
            rules.GetValidInputs(state, Limit).Any(static move => move.Input == "dealSeat")
                ? "dealSeat"
                : "dealDealer";

        private ValidInputSet Inputs(string state) => Rules.GetValidInputs(state, Limit);

        private OutcomeSet Outcomes(string state, string input) => Rules.GetOutcomes($$"""
            { "$schema": "rulealize/input/v1", "ruleSet": "{{Rules.RuleSet}}", "input": "{{input}}", "args": {} }
            """, state, Limit);

        private static double Probability(OutcomeSet outcomes, string rank) =>
            outcomes.Single(outcome => outcome.Draws.Single() == rank).Probability;

        /// <summary>Whose move it is, read the way a host reads it.</summary>
        /// <remarks>
        /// The rule set stores no turn. Which seat is up is a property of the table, and the
        /// runtime hands it over on every candidate, so this asks the answer rather than the
        /// state document.
        /// </remarks>
        private string? Acting(string state) => Inputs(state).FirstOrDefault()?.Actor;

        /// <summary>The distinct input names available, in order, so a set can be asserted whole.</summary>
        private static string[] Names(ValidInputSet moves) =>
            [.. moves.Select(static move => move.Input).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        // ── Reading a table ────────────────────────────────────────────────────

        private static JsonElement Seat(string state, string id) =>
            Fields(state).GetProperty("seats").EnumerateArray()
                .First(seat => string.Equals(seat.GetProperty("id").GetString(), id, StringComparison.Ordinal));

        private static string[] Cards(string state, string id) => Ranks(Seat(state, id).GetProperty("cards"));

        /// <summary>The dealer's hand, which is a list of ranks and nothing else.</summary>
        private static string[] DealerCards(string state) => Ranks(Fields(state).GetProperty("dealer"));

        private static string[] Ranks(JsonElement cards) =>
            [.. cards.EnumerateArray().Select(static card => card.GetString()!)];

        /// <summary>Every seat's result, in seating order.</summary>
        /// <remarks>
        /// A result is null until the hand is settled, and the schema says so. Nothing here
        /// asserts an unsettled table, so the null is spelled rather than carried.
        /// </remarks>
        private static string[] Results(string state) =>
            [.. Fields(state).GetProperty("seats").EnumerateArray()
                .Select(static seat => seat.GetProperty("result").GetString() ?? "unsettled")];

        /// <summary>The table itself, without the identity of the rule set that produced it.</summary>
        /// <remarks>
        /// Which is what makes the two spellings comparable: they agree about every table and
        /// disagree about their own names.
        /// </remarks>
        private static string Data(string state) => Fields(state).GetRawText();

        private static JsonElement Fields(string state)
        {
            using JsonDocument document = JsonDocument.Parse(state);
            return document.RootElement.GetProperty("data").Clone();
        }
    }
}
