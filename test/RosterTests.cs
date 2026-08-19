// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;

namespace Rulealize.Tests
{
    /// <summary>A rule set that is not a game, and what it says about the vocabulary.</summary>
    /// <remarks>
    /// <para>
    /// Reversi, chess and shogi are three board games, which is not evidence that the DSL is
    /// general — it is three data points from one corner of the space. This is a shift
    /// roster: no turn, no opponent, no winner, and no grid plugin loaded into it at all.
    /// </para>
    /// <para>
    /// What a caller wants from it is <c>GetValidInputs</c>, read as "who may still be put on
    /// what". That is the same question a scheduling screen asks and the same one a solver
    /// branches on, and the runtime answers it without knowing that any of this is about
    /// people.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class RosterTests(StandardRuntime standard)
    {
        private const int Limit = 200;

        [Fact]
        public void AnEmptyRosterOffersEveryPlacementTheRulesAllow()
        {
            ValidInputSet placements = standard.Roster.GetValidInputs(standard.Roster.InitialState, Limit);

            // Four people crossed with ten open shifts is forty candidates, and the guard
            // turns away the two juniors on each of the three shifts that want a senior.
            Assert.Equal(40, placements.Evaluated);
            Assert.Equal(40 - 6, placements.Count);
            Assert.All(placements, static placement => Assert.Equal("assign", placement.Input));
        }

        [Fact]
        public void ThereIsNoTurnAndSoNoActor() =>
            // A game names whose move it is. Nothing here does, and the runtime is content:
            // `actor` is a rule set's business and this rule set has no opinion.
            Assert.All(
                standard.Roster.GetValidInputs(standard.Roster.InitialState, Limit),
                static placement => Assert.Null(placement.Actor));

        [Fact]
        public void OnlyASeniorMayTakeAShiftThatWantsOne()
        {
            IEnumerable<string> whoCanTakeMonday = standard.Roster
                .GetValidInputs(standard.Roster.InitialState, Limit)
                .Where(static placement => placement.Arguments["shift"] == "mon-am")
                .Select(static placement => placement.Arguments["who"]);

            Assert.Equal(["ann", "cy"], whoCanTakeMonday.Order(StringComparer.Ordinal));
        }

        [Fact]
        public void NobodyWorksBothHalvesOfADay()
        {
            string state = Assign(standard.Roster.InitialState, "ann", "mon-am");

            Assert.DoesNotContain(
                Assignments(state),
                static placement => placement.Arguments["who"] == "ann"
                    && placement.Arguments["shift"] == "mon-pm");

            Assert.Contains(
                Assignments(state),
                static placement => placement.Arguments["who"] == "ann"
                    && placement.Arguments["shift"] == "tue-pm");
        }

        [Fact]
        public void NobodyWorksMoreThanTheyAgreedTo()
        {
            // cy is down for two shifts. After two, cy is not offered a third anywhere.
            string state = Assign(Assign(standard.Roster.InitialState, "cy", "mon-am"), "cy", "tue-am");

            Assert.DoesNotContain(Assignments(state), static placement => placement.Arguments["who"] == "cy");
            Assert.Contains(Assignments(state), static placement => placement.Arguments["who"] == "ann");
        }

        [Fact]
        public void AShiftAlreadyCoveredIsNotOfferedAgain()
        {
            // This one is refused by the domain rather than the guard — `openShifts` is where
            // the rule reads best — and a caller cannot tell the difference, which is the
            // point of the domain being part of the rules.
            string state = Assign(standard.Roster.InitialState, "ann", "mon-am");

            Assert.DoesNotContain(Assignments(state), static placement => placement.Arguments["shift"] == "mon-am");
            Assert.Throws<IllegalInputException>(() => Assign(state, "bo", "mon-am"));
        }

        [Fact]
        public void AFullRosterIsComplete()
        {
            string state = FullWeek();

            TerminalStatus status = standard.Roster.GetTerminalStatus(state);

            Assert.True(status.IsTerminal);
            Assert.Equal("complete", status.Result);

            // Nothing left to assign. Releases are still on offer, because GetValidInputs
            // does not consult terminal — see ReleasingIsStillOfferedWhenTheRosterIsStuck.
            Assert.Empty(Assignments(state));
        }

        [Fact]
        public void EveryoneIsWorkedToTheirCapacityBecauseTheWeekNeedsExactlyThat()
        {
            // Capacity is 3 + 3 + 2 + 2 and the week is ten shifts, so a complete roster has
            // no slack in it anywhere. Worth asserting because it means the test above is
            // not passing by having found an easy corner of the problem.
            string state = FullWeek();

            Assert.Equal(3, Worked(state, "ann"));
            Assert.Equal(3, Worked(state, "bo"));
            Assert.Equal(2, Worked(state, "cy"));
            Assert.Equal(2, Worked(state, "di"));
        }

        [Fact]
        public void ARosterCanBeStuckWithHolesInIt()
        {
            // Both seniors are spent on the five afternoons, none of which needed one. The
            // two mornings that any junior could have taken are then taken, and the three
            // that want a senior are left with nobody able to fill them. The juniors still
            // have capacity; it is of no use to them.
            string state = standard.Roster.InitialState;
            state = Assign(state, "ann", "mon-pm");
            state = Assign(state, "ann", "tue-pm");
            state = Assign(state, "ann", "wed-pm");
            state = Assign(state, "cy", "thu-pm");
            state = Assign(state, "cy", "fri-pm");
            state = Assign(state, "bo", "tue-am");
            state = Assign(state, "di", "thu-am");

            TerminalStatus status = standard.Roster.GetTerminalStatus(state);

            Assert.True(status.IsTerminal);
            Assert.Equal("stuck", status.Result);
        }

        [Fact]
        public void ReleasingIsStillOfferedWhenTheRosterIsStuck()
        {
            // Which is what makes this usable as a search rather than a one-way filling.
            // GetValidInputs does not consult terminal, so a caller that has painted itself
            // into a corner can still back out of it.
            string state = Assign(standard.Roster.InitialState, "di", "mon-pm");

            Assert.Contains(
                Placements(state),
                static play => play.Input == "release" && play.Arguments["shift"] == "mon-pm");

            TransitionResult after = standard.Roster.ApplyToState(
                Input("release", """ "shift": "mon-pm" """),
                state);

            Assert.False(IsCovered(after.State, "mon-pm"));
        }

        [Fact]
        public void TheTrailRecordsWhatHappened()
        {
            string state = Assign(Assign(standard.Roster.InitialState, "ann", "mon-am"), "bo", "mon-pm");

            JsonElement[] log = Log(state);

            Assert.Equal(2, log.Length);
            Assert.Equal("assign", log[0].GetProperty("action").GetString());
            Assert.Equal("ann", log[0].GetProperty("who").GetString());
            Assert.Equal("mon-am", log[0].GetProperty("shift").GetString());
            Assert.Equal("bo", log[1].GetProperty("who").GetString());
        }

        [Fact]
        public void TheTrailRemembersWhoWasReleased()
        {
            string state = Assign(standard.Roster.InitialState, "ann", "mon-am");
            state = standard.Roster.ApplyToState(Input("release", """ "shift": "mon-am" """), state).State;

            JsonElement entry = Log(state)[1];

            Assert.Equal("release", entry.GetProperty("action").GetString());
            Assert.Equal("ann", entry.GetProperty("who").GetString());
        }

        [Fact]
        public void TheTrailKeepsOnlyTheLastFive()
        {
            // Appended and trimmed in one expression, and the schema's maxLength is the same
            // number said a second time. If the trimming were wrong the state document would
            // stop being readable, rather than quietly growing.
            string state = FullWeek();

            JsonElement[] log = Log(state);

            Assert.Equal(5, log.Length);
            Assert.Equal("fri-pm", log[4].GetProperty("shift").GetString());
        }

        [Fact]
        public void TheRulesAreEnoughToSearchWith()
        {
            // What a rule set like this is for. The caller knows nothing about seniority or
            // rest days; it asks what may be assigned, tries one, and backtracks when the
            // answer comes back empty. Everything that makes the search terminate — a finite
            // domain, a guard that tightens as the roster fills — is in the document.
            //
            // Only assignments are followed. Releases are legal moves and would make the
            // space cyclic, which is a real property of this rule set rather than an
            // oversight: the DSL has no notion of progress, so a searcher decides for itself
            // what counts as forward.
            string? solved = Solve(standard.Roster.InitialState, 0);

            Assert.NotNull(solved);
            Assert.Equal("complete", standard.Roster.GetTerminalStatus(solved).Result);
            Assert.Equal(3, Worked(solved, "ann"));
            Assert.Equal(2, Worked(solved, "di"));
        }

        [Fact]
        public void AnotherWeekIsAnotherStateDocumentAndTheSameRuleSet()
        {
            // The test this rule set was rewritten for. Different people, different names,
            // a different number of them, three days instead of five — and not a character
            // of the rule set changes, because none of it was ever in the rule set.
            //
            // The first version declared the staff as a type.enum of four names and the
            // shifts as the keys of a rec.map, which made the document describe one
            // particular week. A board game hides that mistake: a chess board really is
            // always eight by eight, so baking the instance into the schema costs nothing
            // there and everything here.
            string week = OtherWeek();

            ValidInputSet placements = standard.Roster.GetValidInputs(week, Limit);

            // Three people across six open shifts, less the two juniors on the one shift
            // that wants a senior.
            Assert.Equal(18, placements.Evaluated);
            Assert.Equal(16, placements.Count);

            Assert.Equal(
                ["eve"],
                placements
                    .Where(static play => play.Input == "assign" && play.Arguments["shift"] == "mon-am")
                    .Select(static play => play.Arguments["who"]));

            string? solved = Solve(week, 0, 6);

            Assert.NotNull(solved);
            Assert.Equal("complete", standard.Roster.GetTerminalStatus(solved).Result);
            Assert.Equal(2, Worked(solved, "eve"));
            Assert.Equal(2, Worked(solved, "fay"));
            Assert.Equal(2, Worked(solved, "gus"));
        }

        [Fact]
        public void TheRuleSetSaysNothingAboutWhoOrWhat()
        {
            // The same claim, read off the document instead of demonstrated. No name of a
            // person and no name of a shift appears anywhere outside `state.initial`.
            string document = StandardRuntime.ReadRuleSet("roster.json");
            string rules = document[document.IndexOf("\"definitions\"", StringComparison.Ordinal)..];

            // Quoted, because a bare "bo" is also in "board" and in "about".
            foreach (string name in (string[])["ann", "bo", "cy", "di", "mon-am", "fri-pm", "mon", "fri"])
            {
                Assert.DoesNotContain($"\"{name}\"", rules, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void TheWholeThingRunsWithoutAGrid() =>
            // The claim this rule set exists to test. Nothing here is a board, and nothing
            // here reaches for one.
            Assert.DoesNotContain(
                "grid.",
                StandardRuntime.ReadRuleSet("roster.json"),
                StringComparison.Ordinal);

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>A three-day week for three other people, as a state document.</summary>
        /// <remarks>
        /// Capacity is two each and there are six shifts, so this one has no slack either.
        /// </remarks>
        private static string OtherWeek() => """
            {
              "$schema": "rulealize/state/v1",
              "ruleSet": "roster@1.0.0",
              "data": {
                "staff": [
                  { "name": "eve", "capacity": 2, "senior": true },
                  { "name": "fay", "capacity": 2, "senior": false },
                  { "name": "gus", "capacity": 2, "senior": false }
                ],
                "shifts": [
                  { "id": "mon-am", "day": "mon", "senior": true },
                  { "id": "mon-pm", "day": "mon", "senior": false },
                  { "id": "tue-am", "day": "tue", "senior": false },
                  { "id": "tue-pm", "day": "tue", "senior": false },
                  { "id": "wed-am", "day": "wed", "senior": false },
                  { "id": "wed-pm", "day": "wed", "senior": false }
                ],
                "assigned": [],
                "log": []
              }
            }
            """;

        /// <summary>Depth-first over the assignments the rules allow, until the week is covered.</summary>
        /// <returns>A complete roster, or null if this branch cannot reach one.</returns>
        private string? Solve(string state, int depth, int shifts = 10)
        {
            if (standard.Roster.GetTerminalStatus(state) is { IsTerminal: true, Result: "complete" })
            {
                return state;
            }

            if (depth >= shifts)
            {
                return null;
            }

            foreach (ValidInput placement in Assignments(state))
            {
                string next = standard.Roster
                    .ApplyToState(placement.ToInputDocument(standard.Roster.RuleSet), state)
                    .State;

                if (Solve(next, depth + 1, shifts) is string solved)
                {
                    return solved;
                }
            }

            return null;
        }

        /// <summary>A complete week. Every person is worked to their capacity.</summary>
        private string FullWeek()
        {
            (string Who, string Shift)[] week =
            [
                ("ann", "mon-am"), ("bo", "mon-pm"),
                ("cy", "tue-am"), ("di", "tue-pm"),
                ("ann", "wed-am"), ("bo", "wed-pm"),
                ("di", "thu-am"), ("cy", "thu-pm"),
                ("ann", "fri-am"), ("bo", "fri-pm")
            ];

            string state = standard.Roster.InitialState;
            foreach ((string who, string shift) in week)
            {
                state = Assign(state, who, shift);
            }

            return state;
        }

        private string Assign(string state, string who, string shift) =>
            standard.Roster.ApplyToState(
                Input("assign", $""" "who": "{who}", "shift": "{shift}" """),
                state).State;

        private ValidInputSet Placements(string state) => standard.Roster.GetValidInputs(state, Limit);

        /// <summary>Just the assignments, because a release has no <c>who</c> to ask about.</summary>
        /// <remarks>
        /// <see cref="ValidInput.Arguments"/> throws on a key the input does not have, and a
        /// rule set with more than one input hands back a mixed set. Every test here that
        /// looks at <c>who</c> has to filter first; so does the shogi suite. It is a small
        /// edge and it has now been met twice.
        /// </remarks>
        private IEnumerable<ValidInput> Assignments(string state) =>
            Placements(state).Where(static play => play.Input == "assign");

        private static string Input(string name, string args) => $$"""
            { "$schema": "rulealize/input/v1", "ruleSet": "roster@1.0.0",
              "input": "{{name}}", "args": { {{args}} } }
            """;

        private static bool IsCovered(string state, string shift) =>
            Assigned(state).Any(entry =>
                string.Equals(entry.GetProperty("shift").GetString(), shift, StringComparison.Ordinal));

        private static int Worked(string state, string who) =>
            Assigned(state).Count(entry =>
                string.Equals(entry.GetProperty("who").GetString(), who, StringComparison.Ordinal));

        private static JsonElement[] Assigned(string state)
        {
            using JsonDocument document = JsonDocument.Parse(state);
            return [.. document.RootElement.GetProperty("data").GetProperty("assigned")
                .EnumerateArray().Select(static entry => entry.Clone())];
        }

        private static JsonElement[] Log(string state)
        {
            using JsonDocument document = JsonDocument.Parse(state);
            return [.. document.RootElement.GetProperty("data").GetProperty("log")
                .EnumerateArray().Select(static entry => entry.Clone())];
        }
    }
}
