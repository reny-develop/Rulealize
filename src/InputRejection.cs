// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;

namespace Rulealize
{
    /// <summary>One thing a rule set will not accept about the arguments it was given.</summary>
    /// <remarks>
    /// <para>
    /// What is carried is a <b>code</b> and not a sentence. Wording belongs to a label
    /// document, keyed and kept one file per language: a rule set holding a sentence would
    /// hold it in one language, and it travels alone — a <c>requires</c> naming the vocabulary
    /// that only the wording used would be false. A host with no label for a code shows the
    /// code, which is honest in a way an invented phrase is not.
    /// </para>
    /// <para>
    /// <see cref="Parameter"/> is which field the refusal is about, inferred from the open
    /// parameter the clause read. A clause reading one is about that field and a screen can
    /// put the message beside it; a clause reading several is about the form, and this is
    /// null.
    /// </para>
    /// </remarks>
    /// <param name="Code">The code naming this refusal, unique within the input.</param>
    /// <param name="Parameter">The open parameter it is about, or null where it is about more than one.</param>
    public sealed record InputRejection(string Code, string? Parameter);

    /// <summary>
    /// Thrown when the arguments supplied for an input are not ones its rule set accepts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A kind of <see cref="IllegalInputException"/>, and the distinction is worth having at
    /// the boundary: the others mean the move was not on offer, while this one means it was,
    /// and the value that came back for an open parameter is what is wrong. A screen shows
    /// the first against the screen and the second against a field.
    /// </para>
    /// <para>
    /// Every clause is evaluated and every failure is carried, rather than stopping at the
    /// first. A form filled in wrongly in three places takes one round trip to learn that, for
    /// the same reason a state document is checked for every violation in one pass. Clauses
    /// are therefore written to be independent: one that relied on an earlier one having
    /// passed is evaluated anyway, and faults if the state it assumed does not hold.
    /// </para>
    /// <para>
    /// A value an open parameter's schema refuses is among <see cref="Rejections"/> under the
    /// code the parameter's <c>invalid</c> gives it, ahead of what the clauses refuse. Where
    /// the parameter gives none, the rule set has not said what that refusal is called, and it
    /// is in <see cref="Unexplained"/> as the schema's own sentence instead. A refusal that is
    /// nothing but those sentences is thrown as a plain <see cref="IllegalInputException"/>,
    /// which is what it was before a rule set could name one.
    /// </para>
    /// <para>
    /// Nothing has been written when this is thrown. Evaluation is pure until a transition
    /// commits, so a host may apply an input to find out whether it is acceptable and lose
    /// nothing by the answer being no.
    /// </para>
    /// </remarks>
    public sealed class InputRejectedException : IllegalInputException
    {
        /// <summary>Initializes a new instance of the <see cref="InputRejectedException"/> class.</summary>
        /// <param name="input">The input that was refused.</param>
        /// <param name="rejections">Every clause that refused it, in the order written.</param>
        public InputRejectedException(string input, ImmutableArray<InputRejection> rejections)
            : this(input, rejections, [])
        {
        }

        /// <summary>Initializes a new instance of the <see cref="InputRejectedException"/> class.</summary>
        /// <param name="input">The input that was refused.</param>
        /// <param name="rejections">Every refusal the rule set gave a code, in the order written.</param>
        /// <param name="unexplained">Every refusal it gave none, as the schema worded it.</param>
        public InputRejectedException(
            string input,
            ImmutableArray<InputRejection> rejections,
            ImmutableArray<string> unexplained)
            : base(input, Describe(input, rejections, unexplained))
        {
            Rejections = rejections;
            Unexplained = unexplained;
        }

        /// <summary>Gets what refused it, in the order the parameters and then the clauses are written.</summary>
        public ImmutableArray<InputRejection> Rejections { get; }

        /// <summary>Gets what an open parameter's schema refused where the rule set gave that refusal no code.</summary>
        /// <remarks>
        /// One sentence per refusal, in English, each starting with the parameter it is about.
        /// Empty unless a parameter without an <c>invalid</c> was given a value its schema does
        /// not admit.
        /// </remarks>
        public ImmutableArray<string> Unexplained { get; }

        private static string Describe(
            string input,
            ImmutableArray<InputRejection> rejections,
            ImmutableArray<string> unexplained)
        {
            List<string> parts = [];
            if (!rejections.IsDefaultOrEmpty)
            {
                parts.Add(string.Join(", ", rejections.Select(static rejection => rejection.Code)) + ".");
            }

            if (!unexplained.IsDefaultOrEmpty)
            {
                parts.AddRange(unexplained);
            }

            if (!rejections.IsDefaultOrEmpty)
            {
                parts.Add("The wording for a code belongs to a label document rather than to the rule set.");
            }

            return $"'{input}' does not accept that: " + string.Join(" ", parts);
        }
    }
}
