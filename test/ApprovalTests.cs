// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;

namespace Rulealize.Tests
{
    /// <summary>The rule set the README shows, held to what the README says it does.</summary>
    /// <remarks>
    /// <para>
    /// Every other rule set here is pinned down because the vocabulary has to keep working.
    /// This one is pinned down because the text around it has to keep being true: the README
    /// quotes the document, the moves it produces and the messages a wrong state gets back,
    /// and all three are things a change to a plugin could quietly alter.
    /// </para>
    /// <para>
    /// It is also the only rule set in the suite with no board, no turn and no collection —
    /// three inputs and two enum fields — which is worth having on its own. A vocabulary that
    /// only works once there is something interesting to say with it is not a small one.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class ApprovalTests(StandardRuntime standard)
    {
        private const int Limit = 64;

        private RuleContext Approval => standard.Approval;

        [Fact]
        public void ADraftCanOnlyBeSubmitted()
        {
            ValidInputSet moves = Approval.GetValidInputs(Approval.InitialState, Limit);

            // Five candidates every time — submit, approve, and reject once per reason —
            // because the domains do not depend on the state. Only the guards do.
            Assert.Equal(5, moves.Evaluated);
            Assert.Equal(["submit"], moves.Select(static move => move.ToString()));
        }

        [Fact]
        public void ReviewOffersApprovalAndAReasonToRefuse()
        {
            ValidInputSet moves = Approval.GetValidInputs(Submitted(), Limit);

            // One input with a domain of three is three legal moves, which is the point of
            // writing `reason` as a parameter rather than as three more inputs.
            Assert.Equal(
                ["approve", "reject(reason: scope)", "reject(reason: cost)", "reject(reason: timing)"],
                moves.Select(static move => move.ToString()));
        }

        [Fact]
        public void RejectingCarriesItsReasonIntoTheStateAndEndsTheCase()
        {
            string review = Submitted();
            ValidInput timing = Approval
                .GetValidInputs(review, Limit)
                .Single(static move => move.Arguments.TryGetValue("reason", out string? r) && r == "timing");

            TransitionResult result = Approval.ApplyToState(timing.ToInputDocument(Approval.RuleSet), review);

            Assert.True(result.IsTerminal);
            Assert.Equal("rejected", result.Result);

            using JsonDocument state = JsonDocument.Parse(result.State);
            JsonElement data = state.RootElement.GetProperty("data");
            Assert.Equal("rejected", data.GetProperty("stage").GetString());
            Assert.Equal("timing", data.GetProperty("reason").GetString());
        }

        [Fact]
        public void ATerminalCaseOffersNothingFurther() =>
            Assert.Empty(Approval.GetValidInputs(Approved(), Limit));

        [Fact]
        public void EveryViolationInAStateIsReportedAndNotJustTheFirst()
        {
            // The message in the README, produced rather than transcribed.
            RuleDocumentException refused = Assert.Throws<RuleDocumentException>(
                () => Approval.GetValidInputs(
                    """
                    { "$schema": "rulealize/state/v1", "ruleSet": "approval@1.0.0",
                      "data": { "stage": "shipped", "reason": "vibes" } }
                    """,
                    Limit));

            Assert.Contains("stage: Expected one of draft, review, approved, rejected but got \"shipped\".", refused.Message, StringComparison.Ordinal);
            Assert.Contains("reason: Expected one of scope, cost, timing but got \"vibes\".", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void OnlyTheIdentifierAndTheMajorVersionHaveToMatch()
        {
            // The reason a state document names a version at all. A revision that changed
            // the shape of the state says so in the schema, not here.
            Assert.Equal(4, Approval.GetValidInputs(Submitted("approval@1.4.2"), Limit).Count);

            Assert.Throws<RuleDocumentException>(
                () => Approval.GetValidInputs(Submitted("approval@2.0.0"), Limit));
        }

        private static string Submitted(string ruleSet = "approval@1.0.0") =>
            $$"""
            { "$schema": "rulealize/state/v1", "ruleSet": "{{ruleSet}}",
              "data": { "stage": "review", "reason": null } }
            """;

        private static string Approved() =>
            """
            { "$schema": "rulealize/state/v1", "ruleSet": "approval@1.0.0",
              "data": { "stage": "approved", "reason": null } }
            """;
    }
}
