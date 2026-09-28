// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;

namespace Rulealize.Tests
{
    /// <summary>The rule set the quick guide is built around, held to what the guide shows.</summary>
    /// <remarks>
    /// <para>
    /// The guide prints three positions and the moves each offers, and the whole lesson is in
    /// the second: the domain produces the same three values every time and the guard throws
    /// one away, so three candidates come back as two legal moves. A change to the vocabulary
    /// that altered either number would leave the guide teaching something that is no longer
    /// true, to the readers least able to notice.
    /// </para>
    /// <para>
    /// The last test is the one this file exists for. A document printed in prose is a second
    /// copy, and the README's copy of approval.json is what happens to one that nothing
    /// compares: it quietly lost a <c>requires</c> entry, and because the shorthand it dropped
    /// was still reachable the copy went on compiling and answering the same moves. Comparing
    /// the printed block to the document — comments stripped, so each may be commented for its
    /// own reader — is the check that was missing.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class CountdownTests(StandardRuntime standard)
    {
        private const int Limit = 64;

        private RuleContext Countdown => standard.Countdown;

        [Fact]
        public void TheOpeningOffersEveryValueOfTheDomain()
        {
            ValidInputSet moves = Countdown.GetValidInputs(Countdown.InitialState, Limit);

            Assert.Equal(3, moves.Evaluated);
            Assert.Equal(
                ["add(n: 1)", "add(n: 2)", "add(n: 3)"],
                moves.Select(static move => move.ToString()));
        }

        [Fact]
        public void TheGuardRemovesTheMoveThatWouldOvershoot()
        {
            ValidInputSet moves = Countdown.GetValidInputs(At(8), Limit);

            // The point of the whole example: the domain is state-independent and still
            // offers three, and only the guard knows that one of them no longer fits.
            Assert.Equal(3, moves.Evaluated);
            Assert.Equal(["add(n: 1)", "add(n: 2)"], moves.Select(static move => move.ToString()));
        }

        [Fact]
        public void TenIsTheEndAndItIsCalledDone()
        {
            TerminalStatus status = Countdown.GetTerminalStatus(At(10));

            Assert.True(status.IsTerminal);
            Assert.Equal("done", status.Result);
            Assert.Empty(Countdown.GetValidInputs(At(10), Limit));
        }

        [Fact]
        public void ApplyingAMoveAddsWhatItWasCalledWith()
        {
            TransitionResult result = Countdown.ApplyToState(
                """{ "$schema": "rulealize/input/v1", "input": "add", "args": { "n": 3 } }""",
                Countdown.InitialState);

            using JsonDocument state = JsonDocument.Parse(result.State);
            Assert.Equal(3, state.RootElement.GetProperty("data").GetProperty("total").GetInt32());
        }

        [Fact]
        public void TheDocumentPrintedInTheQuickGuideIsThisDocument()
        {
            string printed = StandardRuntime.PrintedDocument(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Doc", "ruleset-quickguide.md")));

            Assert.Equal(
                StandardRuntime.Canonical(StandardRuntime.ReadRuleSet("countdown.json")),
                StandardRuntime.Canonical(printed));
        }

        /// <summary>A position part-way to ten.</summary>
        /// <param name="total">How far along it is.</param>
        /// <returns>A state document.</returns>
        private static string At(int total) => $$"""
            { "$schema": "rulealize/state/v1", "ruleSet": "countdown@1.0.0", "data": { "total": {{total}} } }
            """;
    }
}
