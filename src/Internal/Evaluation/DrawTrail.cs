// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Evaluation;
using Rulealize.Abstraction.Value;

namespace Rulealize.Internal.Evaluation
{
    /// <summary>Raised to abandon a run whose script ran out, carrying what could come next.</summary>
    /// <remarks>
    /// <para>
    /// Control flow as an exception, which is worth the discomfort here. Where the draws are
    /// is not known until the effects are evaluated — one may sit inside a branch that a
    /// previous draw decided, and the candidates of the second may be what the first left
    /// behind — so the only way to find the next one is to run until it is reached. Nothing
    /// is lost by abandoning the run: expressions are pure and the draft is thrown away.
    /// </para>
    /// <para>
    /// It happens once per run and never on a completed one, so the cost is one throw per
    /// interior node of the outcome tree.
    /// </para>
    /// </remarks>
    internal sealed class UnscriptedDrawException(ImmutableArray<DrawCandidate> candidates, string origin) : Exception
    {
        /// <summary>Gets what could come out of the draw the run stopped at.</summary>
        /// <remarks>Already sifted: every weight here is positive.</remarks>
        public ImmutableArray<DrawCandidate> Candidates { get; } = candidates;

        /// <summary>Gets where they came from, for a fault about one of them.</summary>
        public string Origin { get; } = origin;
    }

    /// <summary>The choices a run of an input's effects is being made for, in order.</summary>
    /// <remarks>
    /// <para>
    /// A run is deterministic given its script. That is the whole design: the runtime
    /// enumerates scripts and each one replays to exactly one state, which is what makes an
    /// outcome recordable, transportable and replayable, and what keeps a draw from turning
    /// a transition into something a caller cannot reproduce.
    /// </para>
    /// <para>
    /// A script holds the values that were drawn, not the positions they were drawn at. A
    /// position would be shorter and would mean nothing on its own; a value is what a person
    /// reading a log wants, and it is the same thing an input document carries an argument
    /// as.
    /// </para>
    /// </remarks>
    internal sealed class DrawTrail(ImmutableArray<RuleValue> script)
    {
        private int _position;

        /// <summary>Gets the values this trail was built to replay.</summary>
        public ImmutableArray<RuleValue> Script => script;

        /// <summary>Gets how much of the script the run actually reached.</summary>
        /// <remarks>
        /// Short of the end after a completed run means the outcome describes draws that did
        /// not happen, which is a document that belongs to some other state.
        /// </remarks>
        public int Consumed => _position;

        /// <summary>Takes the next choice, or abandons the run at the first unscripted draw.</summary>
        /// <param name="candidates">Everything the draw says could come out.</param>
        /// <param name="origin">Where they came from, for a fault message.</param>
        /// <returns>The candidate this run is for.</returns>
        /// <exception cref="RuleEvaluationException">There is nothing that could come out.</exception>
        /// <exception cref="RuleDocumentException">The script names something that could not.</exception>
        /// <exception cref="UnscriptedDrawException">The script ended here.</exception>
        public RuleValue Take(ReadOnlySpan<DrawCandidate> candidates, string origin)
        {
            ImmutableArray<DrawCandidate> possible = Sift(candidates, origin);

            if (_position >= script.Length)
            {
                throw new UnscriptedDrawException(possible, origin);
            }

            RuleValue wanted = script[_position++];
            foreach (DrawCandidate candidate in possible)
            {
                // The candidate's value is what gets returned, never the script's. A draw
                // written down as text has to reach the rule set as the value it names, for
                // the same reason an input argument does.
                if (ValueMatch.Matches(wanted, candidate.Value))
                {
                    return candidate.Value;
                }
            }

            throw new RuleDocumentException(
                $"The outcome says {RuleValue.Describe(wanted)} came out of '{origin}', "
                + "and that is not among the things that could have.");
        }

        /// <summary>Drops what cannot happen and refuses what cannot be read.</summary>
        /// <remarks>
        /// A weight of zero is an ordinary state of affairs — no cards of that rank left —
        /// and the candidate simply does not appear. Nothing at all left is not: a legal
        /// input has at least one thing that can happen to it, and reporting that here is
        /// what makes the guarantee worth relying on. A rule set that reaches an empty deck
        /// is one whose guard forgot to say the deck is not empty.
        /// </remarks>
        private static ImmutableArray<DrawCandidate> Sift(ReadOnlySpan<DrawCandidate> candidates, string origin)
        {
            ImmutableArray<DrawCandidate>.Builder possible =
                ImmutableArray.CreateBuilder<DrawCandidate>(candidates.Length);

            foreach (DrawCandidate candidate in candidates)
            {
                if (candidate.Weight < 0)
                {
                    throw new RuleEvaluationException(
                        origin,
                        $"A weight cannot be negative, and {RuleValue.Describe(candidate.Value)} carries {candidate.Weight}.");
                }

                if (candidate.Weight > 0)
                {
                    possible.Add(candidate);
                }
            }

            if (possible.Count == 0)
            {
                throw new RuleEvaluationException(
                    origin,
                    candidates.Length == 0
                        ? "There is nothing to draw from. A legal input has at least one thing that can happen "
                          + "to it, so this is a guard that did not say the source could be empty."
                        : "Nothing that could be drawn has any weight, which leaves nothing that can happen.");
            }

            return possible.ToImmutable();
        }
    }
}
