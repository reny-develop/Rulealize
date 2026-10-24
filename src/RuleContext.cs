// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Evaluation;
using Rulealize.Abstraction.Node;
using Rulealize.Abstraction.Store;
using Rulealize.Abstraction.Value;
using Rulealize.Internal.Building;
using Rulealize.Internal.Document;
using Rulealize.Internal.Evaluation;
using Rulealize.Internal.Plugin;
using Rulealize.Internal.RuleSet;

namespace Rulealize
{
    /// <summary>One rule set, compiled and ready to run states through.</summary>
    /// <remarks>
    /// <para>
    /// Immutable and stateless with respect to any particular position: a context holds the
    /// rules, and a state is handed to it per call. One context serves any number of
    /// concurrent games.
    /// </para>
    /// <para>
    /// Everything that could be decided from the document alone was decided when the context
    /// was built. What is left to fail at this point is a state document that does not match
    /// the schema, an input that the rules do not allow, and the evaluation faults the value
    /// model reserves for values that make an operation meaningless.
    /// </para>
    /// </remarks>
    public sealed class RuleContext
    {
        private readonly CompiledRuleSet _ruleSet;
        private readonly StoreBinding _stores;

        internal RuleContext(CompiledRuleSet ruleSet, StoreBinding? stores = null)
        {
            _ruleSet = ruleSet;
            _stores = stores ?? StoreBinding.None;
        }

        /// <summary>Gets the rule set's identifier.</summary>
        public string Id => _ruleSet.Id;

        /// <summary>Gets the rule set's version.</summary>
        public string Version => _ruleSet.Version;

        /// <summary>Gets the identity a state document carries, <c>id@version</c>.</summary>
        public string RuleSet => _ruleSet.Qualified;

        /// <summary>Gets the names of the inputs this rule set offers, in document order.</summary>
        /// <remarks>
        /// Its own first, then those of every rule set it holds, qualified by the aliases
        /// <c>uses</c> gave them — as deep as the documents nest. A rule set holding nothing
        /// lists exactly what it declares.
        /// </remarks>
        public ImmutableArray<string> Inputs => [.. Names(_ruleSet, string.Empty)];

        private static IEnumerable<string> Names(CompiledRuleSet rules, string prefix) =>
            rules.Inputs
                .Select(input => prefix + input.Name)
                .Concat(rules.Held.SelectMany(held => Names(held.Rules, $"{prefix}{held.Alias}.")));

        /// <summary>Gets the names of what this rule set will say about its own position, in the order written.</summary>
        /// <remarks>
        /// <para>
        /// Its own first, then those of every rule set it holds once, qualified by the aliases
        /// <c>uses</c> gave them, as <see cref="Inputs"/> qualifies inputs: a rule set held once
        /// is a field of this one's state, so what it says is said about this position too.
        /// Empty unless some document declares <c>projections</c>. A caller asks this rather
        /// than assuming a name is there, for the reason it asks <see cref="Inputs"/> rather
        /// than assuming an input is.
        /// </para>
        /// <para>
        /// Every name here can be asked of the position, so a caller may ask each one in turn.
        /// What a rule set held <c>many</c> says is about one instance, not about the position,
        /// and is listed by <see cref="InstanceProjections"/> instead.
        /// </para>
        /// </remarks>
        public ImmutableArray<string> Projections =>
            [.. ProjectionNames(_ruleSet, string.Empty, false).Where(static each => !each.OfInstance).Select(static each => each.Name)];

        /// <summary>Gets the names of what the rule sets held <c>many</c> say about one instance, in the order written.</summary>
        /// <remarks>
        /// Qualified by the aliases on the way down, as <see cref="Inputs"/> qualifies inputs:
        /// <c>claims.summary</c> is what the rule set held as <c>claims</c> says about one
        /// claim, and <c>months.claims.summary</c> the same of a claim of a month. Each is asked
        /// at an instance's address, by <see cref="ProjectAsync(StateAddress, string, CancellationToken)"/>.
        /// </remarks>
        public ImmutableArray<string> InstanceProjections =>
            [.. ProjectionNames(_ruleSet, string.Empty, false).Where(static each => each.OfInstance).Select(static each => each.Name)];

        private static IEnumerable<(string Name, bool OfInstance)> ProjectionNames(CompiledRuleSet rules, string prefix, bool ofInstance) =>
            rules.Projections
                .Select(projection => (prefix + projection.Name, ofInstance))
                .Concat(rules.Held.SelectMany(held =>
                    ProjectionNames(held.Rules, $"{prefix}{held.Alias}.", ofInstance || held.Many is not null)));

        /// <summary>Gets the opening position, as a state document.</summary>
        public string InitialState => StateDocument.Write(_ruleSet, _ruleSet.InitialState);

        /// <summary>Applies an input to a state.</summary>
        /// <param name="inputDocument">A <c>rulealize/input/v1</c> document.</param>
        /// <param name="stateDocument">A <c>rulealize/state/v1</c> document.</param>
        /// <param name="cancellationToken">Cancels a long evaluation.</param>
        /// <returns>Where the transition arrived.</returns>
        /// <exception cref="RuleDocumentException">A document is malformed or does not match the schema.</exception>
        /// <exception cref="IllegalInputException">The rules do not allow this input in this state.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <exception cref="InvalidOperationException">
        /// The input resolves a draw, so it has more than one outcome and cannot be applied
        /// without one being named; or the rule set keeps instances as documents of their own,
        /// so a position is several documents and is passed by address.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Refuses exactly what <see cref="GetValidInputs(string, int, CancellationToken)"/>
        /// would not have listed. Each argument has to be a value its parameter's domain
        /// produces, and then the guard has to accept it; a rule set may put a rule in either
        /// place. Resolving the arguments costs a walk through each domain as far as the
        /// argument, which is the price of the two methods answering the same question.
        /// </para>
        /// <para>
        /// This is the overload for an input whose effects settle the next state on their
        /// own. One that draws has more than one state it could arrive at, and no way to pick
        /// between them that would not be the runtime inventing an answer nobody enumerated,
        /// so it is refused here — before anything is evaluated, because the document already
        /// said. <see cref="GetOutcomes(string, string, int, CancellationToken)"/> is what
        /// asks which states those are, and
        /// <see cref="ApplyToState(string, string, string, CancellationToken)"/> is what
        /// replays one that happened.
        /// </para>
        /// <para>
        /// Synchronous, because nothing here is I/O: the documents are already in memory, and
        /// what happens to them is node evaluation. The asynchronous overload exists for the
        /// one case that genuinely is I/O — reading a document off a stream.
        /// </para>
        /// </remarks>
        public TransitionResult ApplyToState(
            string inputDocument,
            string stateDocument,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputDocument);
            ArgumentNullException.ThrowIfNull(stateDocument);
            RequireOneDocument();

            using JsonDocument input = Parse(inputDocument, "input");
            using JsonDocument state = Parse(stateDocument, "state");
            return Apply(input.RootElement, state.RootElement, cancellationToken);
        }

        /// <summary>Applies an input to a state, reading both from streams.</summary>
        /// <param name="inputDocument">A <c>rulealize/input/v1</c> document.</param>
        /// <param name="stateDocument">A <c>rulealize/state/v1</c> document.</param>
        /// <param name="cancellationToken">Cancels reading or a long evaluation.</param>
        /// <returns>Where the transition arrived.</returns>
        /// <exception cref="RuleDocumentException">A document is malformed or does not match the schema.</exception>
        /// <exception cref="IllegalInputException">The rules do not allow this input in this state.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <exception cref="InvalidOperationException">
        /// The rule set keeps instances of what it holds as documents of their own, in the store
        /// it names, and is applied through <see cref="ApplyAsync(string, CancellationToken)"/>.
        /// </exception>
        public async Task<TransitionResult> ApplyToStateAsync(
            Stream inputDocument,
            Stream stateDocument,
            CancellationToken cancellationToken = default)
        {
            RequireOneDocument();
            ArgumentNullException.ThrowIfNull(inputDocument);
            ArgumentNullException.ThrowIfNull(stateDocument);

            using JsonDocument input = await ParseAsync(inputDocument, "input", cancellationToken).ConfigureAwait(false);
            using JsonDocument state = await ParseAsync(stateDocument, "state", cancellationToken).ConfigureAwait(false);
            return Apply(input.RootElement, state.RootElement, cancellationToken);
        }

        /// <summary>Applies an input to a state, resolving its draws as an outcome says.</summary>
        /// <param name="inputDocument">A <c>rulealize/input/v1</c> document.</param>
        /// <param name="stateDocument">A <c>rulealize/state/v1</c> document.</param>
        /// <param name="outcomeDocument">A <c>rulealize/outcome/v1</c> document.</param>
        /// <param name="cancellationToken">Cancels a long evaluation.</param>
        /// <returns>Where the transition arrived.</returns>
        /// <exception cref="RuleDocumentException">A document is malformed, or the outcome belongs elsewhere.</exception>
        /// <exception cref="IllegalInputException">The rules do not allow this input in this state.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <exception cref="InvalidOperationException">
        /// The rule set keeps instances of what it holds as documents of their own, in the store
        /// it names, and is applied through <see cref="ApplyAsync(string, CancellationToken)"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The replaying overload. An input says what somebody decided and an outcome says
        /// what the world did about it, and the two together settle the transition exactly —
        /// so a recorded pair produces the state it was recorded against, however long
        /// afterwards. That is what an audit trail over a rule set with chance in it is made
        /// of, and what
        /// <see cref="GetOutcomes(string, string, int, CancellationToken)"/> hands out in
        /// <see cref="Outcome.ToOutcomeDocument"/>.
        /// </para>
        /// <para>
        /// An outcome with no draws in it means the same thing as not passing one at all, so
        /// this overload subsumes
        /// <see cref="ApplyToState(string, string, CancellationToken)"/> rather than sitting
        /// beside it. A caller recording every transition as an input and an outcome writes
        /// the same two documents whether the rule set draws anything or not.
        /// </para>
        /// </remarks>
        public TransitionResult ApplyToState(
            string inputDocument,
            string stateDocument,
            string outcomeDocument,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputDocument);
            ArgumentNullException.ThrowIfNull(stateDocument);
            ArgumentNullException.ThrowIfNull(outcomeDocument);
            RequireOneDocument();

            using JsonDocument input = Parse(inputDocument, "input");
            using JsonDocument state = Parse(stateDocument, "state");
            using JsonDocument outcome = Parse(outcomeDocument, "outcome");
            return Replay(input.RootElement, state.RootElement, outcome.RootElement, cancellationToken);
        }

        /// <summary>Applies an input and an outcome to a state, reading all three from streams.</summary>
        /// <param name="inputDocument">A <c>rulealize/input/v1</c> document.</param>
        /// <param name="stateDocument">A <c>rulealize/state/v1</c> document.</param>
        /// <param name="outcomeDocument">A <c>rulealize/outcome/v1</c> document.</param>
        /// <param name="cancellationToken">Cancels reading or a long evaluation.</param>
        /// <returns>Where the transition arrived.</returns>
        /// <exception cref="RuleDocumentException">A document is malformed, or the outcome belongs elsewhere.</exception>
        /// <exception cref="IllegalInputException">The rules do not allow this input in this state.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <exception cref="InvalidOperationException">
        /// The rule set keeps instances of what it holds as documents of their own, in the store
        /// it names, and is applied through <see cref="ApplyAsync(string, CancellationToken)"/>.
        /// </exception>
        public async Task<TransitionResult> ApplyToStateAsync(
            Stream inputDocument,
            Stream stateDocument,
            Stream outcomeDocument,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputDocument);
            ArgumentNullException.ThrowIfNull(stateDocument);
            ArgumentNullException.ThrowIfNull(outcomeDocument);
            RequireOneDocument();

            using JsonDocument input = await ParseAsync(inputDocument, "input", cancellationToken).ConfigureAwait(false);
            using JsonDocument state = await ParseAsync(stateDocument, "state", cancellationToken).ConfigureAwait(false);
            using JsonDocument outcome =
                await ParseAsync(outcomeDocument, "outcome", cancellationToken).ConfigureAwait(false);

            return Replay(input.RootElement, state.RootElement, outcome.RootElement, cancellationToken);
        }

        /// <summary>Lists everything that can happen when an input is applied, and where each leads.</summary>
        /// <param name="inputDocument">A <c>rulealize/input/v1</c> document.</param>
        /// <param name="stateDocument">A <c>rulealize/state/v1</c> document.</param>
        /// <param name="outcomeLimit">
        /// The most outcomes to return. Counted in outcomes rather than in work, so one or
        /// more of them always comes back.
        /// </param>
        /// <param name="cancellationToken">Cancels a long search.</param>
        /// <returns>
        /// The outcomes, most likely first, and how much of the probability they account for.
        /// </returns>
        /// <exception cref="RuleDocumentException">A document is malformed or does not match the schema.</exception>
        /// <exception cref="IllegalInputException">The rules do not allow this input in this state.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <exception cref="InvalidOperationException">
        /// The rule set keeps instances of what it holds as documents of their own, in the store
        /// it names, and a position of several documents cannot be asked what may happen yet.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The other half of a search.
        /// <see cref="GetValidInputs(string, int, CancellationToken)"/> answers who may do
        /// what; this answers what may then happen. A traversal is the two of them in that
        /// order, over and over, and it does not change shape when a rule set has chance in
        /// it: <b>an input that draws nothing has exactly one outcome, of probability one</b>,
        /// so the inner loop runs once instead of thirteen times and there is nothing for a
        /// caller to branch on.
        /// </para>
        /// <para>
        /// An input the rules allow always has at least one outcome. There is no empty answer
        /// to check for — a draw with nothing to draw from is a fault, reported where it
        /// happened, because it means a guard did not say what it should have.
        /// </para>
        /// <para>
        /// The limit counts outcomes, not evaluations, which is what makes that guarantee
        /// hold for any limit at all. It is also why it is a different quantity from
        /// <c>validationLimit</c> despite the similar name: truncating a list of legal moves
        /// leaves a set of legal moves, while truncating a list of outcomes leaves a
        /// distribution that no longer sums to one. <see cref="OutcomeSet.Coverage"/> says how
        /// much of it survived, and the order — most likely first — is what makes the part
        /// that survived the part worth having.
        /// </para>
        /// <para>
        /// Synchronous, for the reason
        /// <see cref="GetValidInputs(string, int, CancellationToken)"/> is: this runs an
        /// input's effects once per branch of its outcome tree, and nothing on that path is
        /// I/O.
        /// </para>
        /// </remarks>
        public OutcomeSet GetOutcomes(
            string inputDocument,
            string stateDocument,
            int outcomeLimit,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputDocument);
            ArgumentNullException.ThrowIfNull(stateDocument);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outcomeLimit);
            RequireOneDocument();

            using JsonDocument input = Parse(inputDocument, "input");
            using JsonDocument state = Parse(stateDocument, "state");

            Prepared prepared = Begin(input.RootElement, state.RootElement, outcomeSupplied: true, cancellationToken);
            return prepared.Declared.HasDraw
                ? Enumerate(prepared, outcomeLimit, cancellationToken)
                : Certain(prepared, cancellationToken);
        }

        /// <summary>Lists the inputs the rules allow in a state.</summary>
        /// <param name="stateDocument">A <c>rulealize/state/v1</c> document.</param>
        /// <param name="validationLimit">
        /// The most candidates whose guard may be evaluated. Candidates are the product of
        /// the parameter domains, so this is the handle on combinatorial blow-up.
        /// </param>
        /// <param name="cancellationToken">Cancels a long search.</param>
        /// <returns>
        /// The available inputs, and whether the limit cut the search short. A truncated
        /// result is a subset of what is legal, never a wrong entry.
        /// </returns>
        /// <exception cref="RuleDocumentException">The state document is malformed or does not match the schema.</exception>
        /// <exception cref="InvalidOperationException">
        /// The rule set keeps instances of what it holds as documents of their own, in the store
        /// it names, and is asked through <see cref="GetValidInputsAsync(StateAddress, int, CancellationToken)"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The candidates are the product of the parameter domains and the survivors are what
        /// the guards accept, so both are part of what a rule set means by a legal input.
        /// <see cref="ApplyToState(string, string, CancellationToken)"/> enforces both, and a
        /// rule set is free to put a rule in whichever of the two suits it — in a domain when
        /// stating it there is what keeps the candidate count down, in a guard otherwise.
        /// </para>
        /// <para>
        /// Synchronous, and there is no asynchronous overload at all. This walks a domain and
        /// evaluates a guard against every member of it — thousands of node evaluations for
        /// one call — and an asynchronous signature over that hot path would cost more than it
        /// could buy.
        /// </para>
        /// <para>
        /// Whether a state is terminal is a separate question, asked with
        /// <see cref="GetTerminalStatus(string, CancellationToken)"/>. This method does not
        /// consult it, so a rule set whose guards stay satisfiable after the game ends will
        /// still list moves; that is a property of the rule set, and the runtime does not
        /// second-guess it.
        /// </para>
        /// </remarks>
        public ValidInputSet GetValidInputs(
            string stateDocument,
            int validationLimit,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stateDocument);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(validationLimit);
            RequireOneDocument();

            using JsonDocument state = Parse(stateDocument, "state");
            ImmutableArray<RuleValue> fields = StateDocument.Read(_ruleSet, state.RootElement);
            Part root = Part.Of(_ruleSet, fields, "inputs", cancellationToken);

            Search search = new(validationLimit);
            Offered(root, null, null, string.Empty, search, cancellationToken);

            return new ValidInputSet(search.Found.ToImmutable(), search.Evaluated, search.Exhausted);
        }

        /// <summary>Asks the rule set what it says about a position.</summary>
        /// <param name="projection">The name, out of <see cref="Projections"/>.</param>
        /// <param name="stateDocument">The position.</param>
        /// <param name="cancellationToken">Cancels a long evaluation.</param>
        /// <returns>The answer, as JSON.</returns>
        /// <exception cref="ArgumentException">
        /// This rule set declares no projection of that name. A misspelling rather than a
        /// document fault, which is why it is the caller who is told.
        /// </exception>
        /// <exception cref="RuleDocumentException">The state document is not one this rule set accepts.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <exception cref="InvalidOperationException">
        /// The projection reads every instance of a collection. Those are in the store the rule
        /// set names, and <see cref="ProjectAsync(string, CancellationToken)"/> reads them there.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The three questions a rule set answers without this — what is legal, where a move
        /// leads, whether it is over — are the ones the runtime needs to move a position along.
        /// Everything else a caller wanted about a position had to be worked out from the state
        /// document by reading it, which puts a second account of the rules outside the rule
        /// set and lets the two disagree. A projection is the rule set answering instead, in
        /// the vocabulary it is already written in.
        /// </para>
        /// <para>
        /// A function of the position and nothing else. It takes no arguments and cannot draw,
        /// so asking twice about one position gives one answer, and what comes back can be
        /// cached against the position, compared with the answer for another, or handed to
        /// something that will only read it.
        /// </para>
        /// <para>
        /// What comes back is the value model as JSON, and nothing interprets it on the way
        /// out: what the keys mean is the rule set's business. An opaque value is written as
        /// its canonical text, and one that has none is a fault rather than a field that
        /// quietly went missing.
        /// </para>
        /// </remarks>
        public string Project(
            string projection,
            string stateDocument,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(projection);
            ArgumentNullException.ThrowIfNull(stateDocument);

            ResolvedProjection resolved = ResolveProjection(projection);
            RequireNoInstance(resolved, projection);
            if (!resolved.Declared.Collections.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"'{projection}' reads every instance of {string.Join(", ", resolved.Declared.Collections.Select(static alias => $"'{alias}'"))}, "
                    + "and those are in the store the rule set names. ProjectAsync reads them there.");
            }

            using JsonDocument state = Parse(stateDocument, "state");
            return Answer(resolved, StateDocument.Read(_ruleSet, state.RootElement), new Dictionary<string, IReadOnlyList<KeyValuePair<string, string>>>(), cancellationToken);
        }

        /// <summary>Asks the rule set what it says about a position of several documents.</summary>
        /// <param name="projection">The name, out of <see cref="Projections"/>.</param>
        /// <param name="rootState">The rule set's own state document.</param>
        /// <param name="collections">
        /// Every instance of each collection the projection reads, by alias: each instance's key
        /// and document, in the order they were created.
        /// </param>
        /// <param name="cancellationToken">Cancels a long evaluation.</param>
        /// <returns>The answer, as JSON.</returns>
        /// <exception cref="ArgumentException">This rule set declares no projection of that name.</exception>
        /// <exception cref="RuleDocumentException">
        /// A document is malformed, does not match its schema, or was not supplied; or an
        /// instance is under a temporary key.
        /// </exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <remarks>
        /// <c>$claims</c> reads as every instance of <c>claims</c>, a sequence in the order given,
        /// each the record <c>@claims</c> is: <c>{ "key": …, "state": … }</c>. Which instances
        /// exist and in what order they were created is the store's to say, so the answer is
        /// about the ones supplied; a store that left one out asked about a different position.
        /// </remarks>
        internal string Project(
            string projection,
            string rootState,
            IReadOnlyDictionary<string, IReadOnlyList<KeyValuePair<string, string>>> collections,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(projection);
            ArgumentNullException.ThrowIfNull(rootState);
            ArgumentNullException.ThrowIfNull(collections);

            ResolvedProjection resolved = ResolveProjection(projection);
            RequireNoInstance(resolved, projection);

            using JsonDocument state = Parse(rootState, "state");
            return Answer(resolved, StateDocument.Read(_ruleSet, state.RootElement), collections, cancellationToken);
        }

        /// <summary>Asks whether a state is final, and what its outcome is.</summary>
        /// <param name="stateDocument">A <c>rulealize/state/v1</c> document.</param>
        /// <param name="cancellationToken">Cancels a long evaluation.</param>
        /// <returns>The verdict. A rule set with no <c>terminal</c> section never reports one.</returns>
        /// <exception cref="RuleDocumentException">The state document is not one this rule set accepts.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        public TerminalStatus GetTerminalStatus(string stateDocument, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stateDocument);

            using JsonDocument state = Parse(stateDocument, "state");
            return EvaluateTerminal(_ruleSet, StateDocument.Read(_ruleSet, state.RootElement), cancellationToken);
        }

        /// <summary>Says which documents applying an input reads, before any of them is fetched.</summary>
        /// <param name="inputDocument">A <c>rulealize/input/v1</c> document.</param>
        /// <returns>
        /// The addresses: <see cref="StateAddress.Root"/> first, then each instance the input
        /// document's <c>at</c> names, outermost first.
        /// </returns>
        /// <exception cref="RuleDocumentException">
        /// The document is malformed, names no input of this rule set, or its <c>at</c> does
        /// not name exactly the instances the input is about.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Read off the input document and the rule set alone. Nothing is read lazily while a
        /// transition is evaluated: what a store fetches for
        /// <see cref="ApplyToState(string, IReadOnlyDictionary{StateAddress, string}, CancellationToken)"/>
        /// is this list, and it is the whole of what the transition reads.
        /// </para>
        /// <para>
        /// The rule set's own document is always on it. A new instance is not, because there is
        /// nothing to read: it starts where its collection's <c>start</c> says. An instance of a
        /// keyed collection is on it whether or not it was ever written; one the store has no
        /// document for is left out of what it supplies, and is read as its <c>start</c>.
        /// </para>
        /// </remarks>
        internal ImmutableArray<StateAddress> GetReads(string inputDocument)
        {
            ArgumentNullException.ThrowIfNull(inputDocument);

            using JsonDocument input = Parse(inputDocument, "input");
            Located located = Locate(InputDocument.Read(_ruleSet, input.RootElement));
            return [StateAddress.Root, .. located.Instances.Select(static step => step.Address)];
        }

        /// <summary>Applies an input to a position of several documents.</summary>
        /// <param name="inputDocument">A <c>rulealize/input/v1</c> document.</param>
        /// <param name="states">
        /// The state documents, by address — every one <see cref="GetReads(string)"/> lists for
        /// this input, except an instance of a keyed collection that was never written.
        /// </param>
        /// <param name="cancellationToken">Cancels a long evaluation.</param>
        /// <returns>
        /// Where the transition arrived. <see cref="TransitionResult.Writes"/> is what it wrote,
        /// a new instance under a temporary key.
        /// </returns>
        /// <exception cref="RuleDocumentException">
        /// A document is malformed, does not match its schema, or was not supplied.
        /// </exception>
        /// <exception cref="IllegalInputException">The rules do not allow this input in this position.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <exception cref="InvalidOperationException">
        /// The input resolves a draw, which a position of several documents cannot be asked
        /// about yet.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Pure, as the one-document overload is. The runtime stores nothing and knows no
        /// versions: it says which documents the transition wrote, and checking each against
        /// the version it was read at is the store's. A guard reads only documents its
        /// transition writes, so checking what is written is enough to catch two transitions
        /// that raced.
        /// </para>
        /// <para>
        /// A rule set that keeps no instances is a position of one document, at
        /// <see cref="StateAddress.Root"/>, and this means what the one-document overload means.
        /// </para>
        /// </remarks>
        internal TransitionResult ApplyToState(
            string inputDocument,
            IReadOnlyDictionary<StateAddress, string> states,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputDocument);
            ArgumentNullException.ThrowIfNull(states);

            using JsonDocument input = Parse(inputDocument, "input");
            (InputRequest request, Located located, Position position) = ReadPosition(input.RootElement, states);

            Prepared prepared = Begin(request, located, position, outcomeSupplied: false, null, cancellationToken);
            return Run(prepared, new DrawTrail([]), cancellationToken);
        }

        /// <summary>Applies an input to a position of several documents, as a commit recorded it.</summary>
        /// <param name="inputDocument">A <c>rulealize/input/v1</c> document.</param>
        /// <param name="states">The state documents, by address.</param>
        /// <param name="commitDocument">A <c>rulealize/commit/v1</c> document: the keys the store assigned.</param>
        /// <param name="cancellationToken">Cancels a long evaluation.</param>
        /// <returns>Where the transition arrived, with each new instance under the key it was assigned.</returns>
        /// <exception cref="RuleDocumentException">
        /// A document is malformed or missing, or the commit belongs to another transition.
        /// </exception>
        /// <exception cref="IllegalInputException">The rules do not allow this input in this position.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <exception cref="InvalidOperationException">
        /// The input resolves a draw, which a position of several documents cannot be asked
        /// about yet.
        /// </exception>
        /// <remarks>
        /// The replaying overload. An input says what somebody decided and a commit says what
        /// the store did about it, so the pair writes the same documents under the same keys
        /// however long afterwards — which is how a recorded history of a collection reproduces
        /// it.
        /// </remarks>
        internal TransitionResult ApplyToState(
            string inputDocument,
            IReadOnlyDictionary<StateAddress, string> states,
            string commitDocument,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputDocument);
            ArgumentNullException.ThrowIfNull(states);
            ArgumentNullException.ThrowIfNull(commitDocument);

            using JsonDocument input = Parse(inputDocument, "input");
            using JsonDocument commit = Parse(commitDocument, "commit");
            (InputRequest request, Located located, Position position) = ReadPosition(input.RootElement, states);
            CommitRequest committed = CommitDocument.Read(_ruleSet, commit.RootElement);

            if (!string.Equals(committed.Input, request.Input, StringComparison.Ordinal))
            {
                throw new RuleDocumentException(
                    $"The commit is of '{committed.Input}', and the input document applies '{request.Input}'.");
            }

            string temporary = $"{TemporaryKeyPrefix}1";
            string? assigned = null;
            if (located.Declared.Creates is not null)
            {
                if (committed.Created.Count != 1 || !committed.Created.TryGetValue(temporary, out assigned))
                {
                    throw new RuleDocumentException(
                        $"'{request.Input}' creates one instance, under '{temporary}', and the commit assigns "
                        + (committed.Created.Count == 0 ? "no key." : $"{string.Join(", ", committed.Created.Keys.Select(static key => $"'{key}'"))}."));
                }

                if (assigned.StartsWith(TemporaryKeyPrefix, StringComparison.Ordinal))
                {
                    throw new RuleDocumentException(
                        $"The commit assigns '{assigned}', which is a temporary key and not one a store could have given.");
                }
            }
            else if (committed.Created.Count > 0)
            {
                throw new RuleDocumentException(
                    $"'{request.Input}' creates nothing, and the commit assigns keys as though it did.");
            }

            Prepared prepared = Begin(request, located, position, outcomeSupplied: false, assigned, cancellationToken);
            return Run(prepared, new DrawTrail([]), cancellationToken);
        }

        /// <summary>Lists the inputs the rules allow at one address of a position.</summary>
        /// <param name="states">
        /// The state documents, by address: the rule set's own, and the instance
        /// <paramref name="at"/> names and each it is held under.
        /// </param>
        /// <param name="at">
        /// Where to look. <see cref="StateAddress.Root"/> offers the rule set's own inputs that
        /// are at no instance; an instance offers the inputs about it, at whatever depth.
        /// </param>
        /// <param name="validationLimit">The most candidates whose guard may be evaluated.</param>
        /// <param name="cancellationToken">Cancels a long search.</param>
        /// <returns>The available inputs, each written with the <c>at</c> it was found at.</returns>
        /// <exception cref="ArgumentException"><paramref name="at"/> is not an address of this rule set.</exception>
        /// <exception cref="RuleDocumentException">A document is malformed, does not match its schema, or was not supplied.</exception>
        /// <remarks>
        /// One address per call, and no call offers every instance's moves at once: that would
        /// read every instance, and the point of keeping them apart is that nothing does.
        /// </remarks>
        internal ValidInputSet GetValidInputs(
            IReadOnlyDictionary<StateAddress, string> states,
            StateAddress at,
            int validationLimit,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(states);
            ArgumentNullException.ThrowIfNull(at);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(validationLimit);

            // The address is followed down one collection at a time, each alias resolved against
            // the rule set of the instance before it.
            ImmutableArray<Step>.Builder steps = ImmutableArray.CreateBuilder<Step>();
            CompiledRuleSet rules = _ruleSet;
            StateAddress address = StateAddress.Root;
            for (int i = 0; i < at.Segments.Length; i += 2)
            {
                HeldRuleSet? held = rules.FindHeld(at.Segments[i]);
                if (held?.Many is null || i + 1 >= at.Segments.Length)
                {
                    throw new ArgumentException(
                        $"'{at}' is not the address of an instance of '{_ruleSet.Qualified}'.", nameof(at));
                }

                if (KeyFault(held, at.Segments[i + 1]) is string fault)
                {
                    throw new ArgumentException(fault, nameof(at));
                }

                address = address.Child(held.Alias, at.Segments[i + 1]);
                steps.Add(new Step(address, held));
                rules = held.Rules;
            }

            ImmutableArray<RuleValue> fields = ReadAt(states, StateAddress.Root, _ruleSet);
            Part root = Part.Of(_ruleSet, new Position(fields, Read(states, steps.ToImmutable())), "inputs", cancellationToken);

            Search search = new(validationLimit, at);
            OfferedAt(root, null, null, string.Empty, [.. steps.Select(static step => step.Held.Alias)], search, cancellationToken);

            return new ValidInputSet(search.Found.ToImmutable(), search.Evaluated, search.Exhausted);
        }

        /// <summary>Applies an input to the documents a store keeps, and keeps what it wrote.</summary>
        /// <param name="inputDocument">A <c>rulealize/input/v1</c> document.</param>
        /// <param name="cancellationToken">Cancels reading, evaluating or committing.</param>
        /// <returns>Where the transition arrived, and the commit document that records what the store did.</returns>
        /// <exception cref="InvalidOperationException">
        /// This rule set keeps no instances, so its state is one document and
        /// <see cref="ApplyToState(string, string, CancellationToken)"/> takes it; a store it
        /// names was not given to the runtime; the transition writes to more than one store and
        /// no coordinator was given; or the input resolves a draw, which a position of several
        /// documents cannot be asked about yet.
        /// </exception>
        /// <exception cref="RuleDocumentException">
        /// A document is malformed, names no input of this rule set, or does not match its schema;
        /// or the input is at an instance the store does not have.
        /// </exception>
        /// <exception cref="InstanceNotFoundException">The input is at an instance the store has no document for.</exception>
        /// <exception cref="IllegalInputException">The rules do not allow this input in this position.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <exception cref="StateConflictException">
        /// A document the transition read was written by another transition before this one
        /// committed. Nothing was kept, and applying the input again decides it afresh.
        /// </exception>
        /// <remarks>
        /// <para>
        /// For a rule set whose <c>uses</c> declares a <c>many</c>. Every state document it has is
        /// in the store its <c>requires</c> names, its own included, and the host hands over the
        /// input alone. The runtime works out which documents the input reads from the input
        /// document and the rule set, reads those and nothing else through a session, evaluates
        /// the transition in memory, and commits what it wrote through the same session. The
        /// rule set's own document, before anything has written it, is its <c>state.initial</c>.
        /// </para>
        /// <para>
        /// Versions are the store's. A guard reads only documents its transition writes, so the
        /// session checking what is written against what it read is enough to catch two
        /// transitions that raced. A transition that writes to more than one store is handed to
        /// the <see cref="IStoreCoordinator"/> the runtime was given.
        /// </para>
        /// </remarks>
        public Task<CommittedTransition> ApplyAsync(string inputDocument, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputDocument);
            return KeepAsync(inputDocument, null, cancellationToken);
        }

        /// <summary>Applies an input to the documents a store keeps, as a commit recorded it.</summary>
        /// <param name="inputDocument">A <c>rulealize/input/v1</c> document.</param>
        /// <param name="commitDocument">The <c>rulealize/commit/v1</c> document the transition was committed with.</param>
        /// <param name="cancellationToken">Cancels reading, evaluating or committing.</param>
        /// <returns>Where the transition arrived, and the commit document it was replayed with.</returns>
        /// <exception cref="InvalidOperationException">
        /// This rule set keeps no instances; a store it names was not given to the runtime; the
        /// transition writes to more than one store and no coordinator was given; or the input
        /// resolves a draw.
        /// </exception>
        /// <exception cref="RuleDocumentException">
        /// A document is malformed or does not match its schema, or the commit belongs to another
        /// transition.
        /// </exception>
        /// <exception cref="InstanceNotFoundException">The input is at an instance the store has no document for.</exception>
        /// <exception cref="IllegalInputException">The rules do not allow this input in this position.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <exception cref="StateConflictException">
        /// A document the transition read was written since, or the instance it creates is
        /// already in the store under its key.
        /// </exception>
        /// <remarks>
        /// The replaying overload, for a store being rebuilt from a history of input and commit
        /// documents. A new instance is written under the key the commit records, and the store
        /// keeps it there rather than assign another, so the history reproduces the same
        /// documents under the same keys.
        /// </remarks>
        public Task<CommittedTransition> ApplyAsync(
            string inputDocument,
            string commitDocument,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputDocument);
            ArgumentNullException.ThrowIfNull(commitDocument);
            return KeepAsync(inputDocument, commitDocument, cancellationToken);
        }

        /// <summary>Lists the inputs the rules allow at the rule set's own document, which a store keeps.</summary>
        /// <param name="validationLimit">The most candidates whose guard may be evaluated.</param>
        /// <param name="cancellationToken">Cancels reading or searching.</param>
        /// <returns>The available inputs that are at no instance: creating one, say.</returns>
        /// <exception cref="InvalidOperationException">This rule set keeps no instances, or a store it names was not given to the runtime.</exception>
        /// <exception cref="RuleDocumentException">A stored document is malformed or does not match its schema.</exception>
        public Task<ValidInputSet> GetValidInputsAsync(int validationLimit, CancellationToken cancellationToken = default) =>
            GetValidInputsAsync(StateAddress.Root, validationLimit, cancellationToken);

        /// <summary>Lists the inputs the rules allow at one document a store keeps.</summary>
        /// <param name="at">
        /// Where to look. <see cref="StateAddress.Root"/> offers the rule set's own inputs that
        /// are at no instance; an instance offers the inputs about it, at whatever depth.
        /// </param>
        /// <param name="validationLimit">The most candidates whose guard may be evaluated.</param>
        /// <param name="cancellationToken">Cancels reading or searching.</param>
        /// <returns>The available inputs, each written with the <c>at</c> it was found at.</returns>
        /// <exception cref="ArgumentException"><paramref name="at"/> is not an address of this rule set.</exception>
        /// <exception cref="InvalidOperationException">This rule set keeps no instances, or a store it names was not given to the runtime.</exception>
        /// <exception cref="InstanceNotFoundException">The store has no instance at <paramref name="at"/>.</exception>
        /// <exception cref="RuleDocumentException">A stored document is malformed or does not match its schema.</exception>
        /// <remarks>
        /// Reads the rule set's own document and the instances on the way to
        /// <paramref name="at"/>, and no others. One address per call: offering every instance's
        /// moves at once would read every instance, and the point of keeping them apart is that
        /// nothing does.
        /// </remarks>
        public async Task<ValidInputSet> GetValidInputsAsync(
            StateAddress at,
            int validationLimit,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(at);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(validationLimit);
            RequireStore();
            CheckAddress(at);

            List<StateAddress> reads = [StateAddress.Root];
            for (int i = 0; i + 1 < at.Segments.Length; i += 2)
            {
                reads.Add(reads[^1].Child(at.Segments[i], at.Segments[i + 1]));
            }

            await using StoreSessions sessions = new(this);
            IReadOnlyDictionary<StateAddress, string> states = await sessions.ReadAsync(reads, cancellationToken).ConfigureAwait(false);
            return GetValidInputs(states, at, validationLimit, cancellationToken);
        }

        /// <summary>Asks the rule set what it says about the position a store keeps.</summary>
        /// <param name="projection">The name, out of <see cref="Projections"/>.</param>
        /// <param name="cancellationToken">Cancels reading or evaluating.</param>
        /// <returns>The answer, as JSON.</returns>
        /// <exception cref="ArgumentException">
        /// This rule set declares no projection of that name, or the projection is one of an
        /// instance's, which is asked at the instance's address.
        /// </exception>
        /// <exception cref="InvalidOperationException">This rule set keeps no instances, or a store it names was not given to the runtime.</exception>
        /// <exception cref="RuleDocumentException">A stored document is malformed or does not match its schema.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <remarks>
        /// <para>
        /// Reads the rule set's own document, and every instance of each collection the
        /// projection reads as <c>$alias</c>: a sequence of <c>{ "key": …, "state": … }</c> in
        /// the order the instances were created. Which collections those are is settled when the
        /// rule set is built, so nothing else is read.
        /// </para>
        /// <para>
        /// Still a function of the position: the same documents give the same answer. The
        /// projection is not translated into a query; the store loads the instances and the
        /// runtime reads them in memory.
        /// </para>
        /// </remarks>
        public Task<string> ProjectAsync(string projection, CancellationToken cancellationToken = default) =>
            ProjectAsync(StateAddress.Root, projection, cancellationToken);

        /// <summary>Asks a rule set what it says about one document a store keeps: its own, or one instance's.</summary>
        /// <param name="at">
        /// The document the projection is about. <see cref="StateAddress.Root"/> for the rule
        /// set's own projections and those of what it holds once; an instance's address for a
        /// projection of the rule set held <c>many</c> there.
        /// </param>
        /// <param name="projection">
        /// The name, out of <see cref="Projections"/> at <see cref="StateAddress.Root"/> and out
        /// of <see cref="InstanceProjections"/> at an instance: <c>claims.summary</c> is what the
        /// rule set held as <c>claims</c> says about one claim.
        /// </param>
        /// <param name="cancellationToken">Cancels reading or evaluating.</param>
        /// <returns>The answer, as JSON.</returns>
        /// <exception cref="ArgumentException">
        /// This rule set declares no projection of that name, <paramref name="at"/> is not an
        /// address of this rule set, or it is not an address the projection is asked at.
        /// </exception>
        /// <exception cref="InvalidOperationException">This rule set keeps no instances, or a store it names was not given to the runtime.</exception>
        /// <exception cref="InstanceNotFoundException">The store has no instance at <paramref name="at"/>.</exception>
        /// <exception cref="RuleDocumentException">A stored document is malformed or does not match its schema.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <remarks>
        /// <para>
        /// Reads the document at <paramref name="at"/>, and every instance of each collection
        /// under it that the projection reads as <c>$alias</c>, and nothing else. A projection of
        /// a rule set held once is asked about that rule set's state, which is a field of the
        /// document it is held in.
        /// </para>
        /// <para>
        /// The address is not an argument. A projection still takes none: it is the rule set
        /// held there answering about its own position, and the same document gives the same
        /// answer wherever it is held.
        /// </para>
        /// </remarks>
        public async Task<string> ProjectAsync(
            StateAddress at,
            string projection,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(at);
            ArgumentException.ThrowIfNullOrEmpty(projection);

            ResolvedProjection resolved = ResolveProjection(projection);
            RequireStore();
            CheckAddress(at);
            CheckProjectionAt(resolved, projection, at);

            await using StoreSessions sessions = new(this);
            IReadOnlyDictionary<StateAddress, string> states =
                await sessions.ReadAsync([at], cancellationToken).ConfigureAwait(false);
            ImmutableArray<RuleValue> document = ReadDocument(states, at, resolved.Instances.IsEmpty ? null : resolved.Instances[^1]);

            // Only a rule set that keeps instances can be asked about every one of a collection,
            // and that is the one at the address: a rule set held once keeps none.
            Dictionary<string, IReadOnlyList<KeyValuePair<string, string>>> collections = new(StringComparer.Ordinal);
            if (!resolved.Declared.Collections.IsEmpty)
            {
                IStateStoreSession session = await sessions.ForAsync(resolved.Rules.Store!, cancellationToken).ConfigureAwait(false);
                foreach (string alias in resolved.Declared.Collections)
                {
                    collections[alias] = await session.ReadCollectionAsync(at, alias, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            return Answer(resolved, document, collections, cancellationToken);
        }

        /// <summary>Asks whether the rule set's own document, which a store keeps, is final.</summary>
        /// <param name="cancellationToken">Cancels reading or evaluating.</param>
        /// <returns>The verdict. A rule set with no <c>terminal</c> section never reports one.</returns>
        /// <exception cref="InvalidOperationException">This rule set keeps no instances, or a store it names was not given to the runtime.</exception>
        /// <exception cref="RuleDocumentException">A stored document is malformed or does not match its schema.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        public Task<TerminalStatus> GetTerminalStatusAsync(CancellationToken cancellationToken = default) =>
            GetTerminalStatusAsync(StateAddress.Root, cancellationToken);

        /// <summary>Asks whether one document a store keeps is final, and what its outcome is.</summary>
        /// <param name="at">
        /// The document. <see cref="StateAddress.Root"/> asks the rule set's own
        /// <c>terminal</c>; an instance's address asks the <c>terminal</c> of the rule set held there.
        /// </param>
        /// <param name="cancellationToken">Cancels reading or evaluating.</param>
        /// <returns>The verdict. A rule set with no <c>terminal</c> section never reports one.</returns>
        /// <exception cref="ArgumentException"><paramref name="at"/> is not an address of this rule set.</exception>
        /// <exception cref="InvalidOperationException">This rule set keeps no instances, or a store it names was not given to the runtime.</exception>
        /// <exception cref="InstanceNotFoundException">The store has no instance at <paramref name="at"/>.</exception>
        /// <exception cref="RuleDocumentException">A stored document is malformed or does not match its schema.</exception>
        /// <exception cref="RuleEvaluationException">An operation received values that make it meaningless.</exception>
        /// <remarks>
        /// Reads the document at <paramref name="at"/> and nothing else: a rule set's
        /// <c>terminal</c> reads its own state, wherever it is held.
        /// </remarks>
        public async Task<TerminalStatus> GetTerminalStatusAsync(StateAddress at, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(at);
            RequireStore();
            CheckAddress(at);

            HeldRuleSet? held = HeldAt(at);

            await using StoreSessions sessions = new(this);
            IReadOnlyDictionary<StateAddress, string> states =
                await sessions.ReadAsync([at], cancellationToken).ConfigureAwait(false);
            return EvaluateTerminal(held?.Rules ?? _ruleSet, ReadDocument(states, at, held), cancellationToken);
        }

        /// <summary>Reads what an input reads, applies it, and commits what it wrote.</summary>
        private async Task<CommittedTransition> KeepAsync(
            string inputDocument,
            string? commitDocument,
            CancellationToken cancellationToken)
        {
            RequireStore();

            ImmutableArray<StateAddress> reads = GetReads(inputDocument);

            await using StoreSessions sessions = new(this);
            IReadOnlyDictionary<StateAddress, string> states =
                await sessions.ReadAsync(reads, cancellationToken).ConfigureAwait(false);

            TransitionResult result = commitDocument is null
                ? ApplyToState(inputDocument, states, cancellationToken)
                : ApplyToState(inputDocument, states, commitDocument, cancellationToken);

            IReadOnlyDictionary<string, string> created =
                await sessions.CommitAsync(result.Writes, cancellationToken).ConfigureAwait(false);

            string input;
            using (JsonDocument document = Parse(inputDocument, "input"))
            {
                input = document.RootElement.GetProperty("input").GetString()!;
            }

            if (commitDocument is not null)
            {
                using JsonDocument commit = Parse(commitDocument, "commit");
                created = CommitDocument.Read(_ruleSet, commit.RootElement).Created;
            }

            // A created instance was evaluated under its temporary key, and is reported under the
            // one the store assigned it.
            StateAddress at = result.At;
            if (!at.IsRoot && created.TryGetValue(at.Key, out string? assigned))
            {
                at = at.Parent.Child(at.Alias, assigned);
            }

            return new CommittedTransition(
                result.IsTerminal,
                result.Result,
                at,
                result.AtStatus,
                CommitDocument.Write(_ruleSet, input, created));
        }

        /// <summary>Says which store keeps the document at an address.</summary>
        /// <remarks>
        /// The rule set's own document is in the store it names. An instance is in the store
        /// named by the rule set that holds its collection, so a month held by a ledger is in the
        /// ledger's store and a claim held by the month is in the month's.
        /// </remarks>
        private string StoreOf(StateAddress address)
        {
            CompiledRuleSet holder = _ruleSet;
            for (int i = 0; i + 2 < address.Segments.Length; i += 2)
            {
                holder = holder.FindHeld(address.Segments[i])!.Rules;
            }

            return holder.Store!;
        }

        /// <summary>Refuses a call that goes through a store, on a rule set that keeps nothing in one.</summary>
        private void RequireStore()
        {
            if (!_ruleSet.KeepsInstances)
            {
                throw new InvalidOperationException(
                    $"'{_ruleSet.Qualified}' keeps no instances, so it is kept in no store: its state is one document, "
                    + "and the methods that take a state document take it.");
            }
        }

        /// <summary>The sessions one call opened, one per store, disposed of together.</summary>
        /// <remarks>
        /// A store is the instance a host gave, not the plugin it was given for: two plugins
        /// given one store — a <see cref="MemoryStateStore"/> standing in for both, say — are one
        /// store, read and committed through one session.
        /// </remarks>
        private sealed class StoreSessions(RuleContext context) : IAsyncDisposable
        {
            private static readonly IEqualityComparer<IStateStore> SameStore = ReferenceEqualityComparer.Instance;

            private readonly Dictionary<IStateStore, IStateStoreSession> _open = new(SameStore);

            /// <summary>Gets the session for one store plugin's store, opening it the first time.</summary>
            public Task<IStateStoreSession> ForAsync(string store, CancellationToken cancellationToken) =>
                OpenAsync(Opened(store), cancellationToken);

            /// <summary>Reads documents from whichever stores keep them.</summary>
            /// <remarks>The rule set's own document, when no store has it yet, is its initial state.</remarks>
            public async Task<IReadOnlyDictionary<StateAddress, string>> ReadAsync(
                IReadOnlyList<StateAddress> addresses,
                CancellationToken cancellationToken)
            {
                Dictionary<StateAddress, string> states = [];
                foreach (IGrouping<IStateStore, StateAddress> group in
                    addresses.GroupBy(address => Opened(context.StoreOf(address)), SameStore))
                {
                    IStateStoreSession session = await OpenAsync(group.Key, cancellationToken).ConfigureAwait(false);
                    foreach ((StateAddress address, string state) in
                        await session.ReadAsync([.. group], cancellationToken).ConfigureAwait(false))
                    {
                        states[address] = state;
                    }
                }

                states.TryAdd(StateAddress.Root, context.InitialState);
                return states;
            }

            /// <summary>Commits what a transition wrote, through one session or through the coordinator.</summary>
            public async Task<IReadOnlyDictionary<string, string>> CommitAsync(
                ImmutableArray<StateWrite> writes,
                CancellationToken cancellationToken)
            {
                List<IGrouping<IStateStore, (string Plugin, StateWrite Write)>> stores =
                [
                    .. writes
                        .Select(write => (Plugin: context.StoreOf(write.Address), Write: write))
                        .GroupBy(each => Opened(each.Plugin), SameStore),
                ];

                if (stores.Count == 0)
                {
                    return ImmutableDictionary<string, string>.Empty;
                }

                if (stores.Count == 1)
                {
                    IStateStoreSession session = await OpenAsync(stores[0].Key, cancellationToken).ConfigureAwait(false);
                    return await session.CommitAsync([.. stores[0].Select(static each => each.Write)], cancellationToken)
                        .ConfigureAwait(false);
                }

                IStoreCoordinator coordinator = context._stores.Coordinator
                    ?? throw new InvalidOperationException(
                        $"The transition writes to {string.Join(" and ", stores.Select(static store => store.First().Plugin))}, and the "
                        + "runtime was given no coordinator to keep all of it or none. RuleRuntime.UseCoordinator gives one.");

                List<StoreWriteSet> sets = [];
                foreach (IGrouping<IStateStore, (string Plugin, StateWrite Write)> store in stores)
                {
                    IStateStoreSession session = await OpenAsync(store.Key, cancellationToken).ConfigureAwait(false);
                    sets.Add(new StoreWriteSet(store.First().Plugin, session, [.. store.Select(static each => each.Write)]));
                }

                return await coordinator.CommitAsync(sets, cancellationToken).ConfigureAwait(false);
            }

            private IStateStore Opened(string store) =>
                context._stores.Find(store)
                ?? throw new InvalidOperationException(
                    $"'{context._ruleSet.Qualified}' keeps documents in {store}, and the runtime was given no such store. "
                    + "RuleRuntime.UseStore gives one, before the context is created.");

            private async Task<IStateStoreSession> OpenAsync(IStateStore store, CancellationToken cancellationToken)
            {
                if (!_open.TryGetValue(store, out IStateStoreSession? session))
                {
                    session = await store.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
                    _open[store] = session;
                }

                return session;
            }

            public async ValueTask DisposeAsync()
            {
                foreach (IStateStoreSession session in _open.Values)
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>Refuses an address that is not one of this rule set's instances.</summary>
        /// <remarks>Checked before anything is read, so a store is never asked for an address no rule set has.</remarks>
        private void CheckAddress(StateAddress at)
        {
            CompiledRuleSet rules = _ruleSet;
            for (int i = 0; i < at.Segments.Length; i += 2)
            {
                HeldRuleSet? held = rules.FindHeld(at.Segments[i]);
                if (held?.Many is null || i + 1 >= at.Segments.Length)
                {
                    throw new ArgumentException(
                        $"'{at}' is not the address of an instance of '{_ruleSet.Qualified}'.", nameof(at));
                }

                if (KeyFault(held, at.Segments[i + 1]) is string fault)
                {
                    throw new ArgumentException(fault, nameof(at));
                }

                rules = held.Rules;
            }
        }

        /// <summary>Reads the input document, and every state document it says the transition reads.</summary>
        private (InputRequest Request, Located Located, Position Position) ReadPosition(
            JsonElement input,
            IReadOnlyDictionary<StateAddress, string> states)
        {
            InputRequest request = InputDocument.Read(_ruleSet, input);
            Located located = Locate(request);

            if (located.Declared.HasDraw)
            {
                throw new InvalidOperationException(
                    $"'{request.Input}' resolves something nobody chose, and a position of several documents "
                    + "cannot be asked what may happen yet.");
            }

            ImmutableArray<RuleValue> root = ReadAt(states, StateAddress.Root, _ruleSet);
            return (request, located, new Position(root, Read(states, located.Instances)));
        }

        /// <summary>Reads each instance on the way to an input, outermost first.</summary>
        private static ImmutableArray<Instance> Read(
            IReadOnlyDictionary<StateAddress, string> states,
            ImmutableArray<Step> steps) =>
        [
            .. steps.Select(step => new Instance(step.Address, step.Held, ReadInstance(states, step.Address, step.Held)))
        ];

        /// <summary>Reads one instance's document.</summary>
        /// <remarks>
        /// An instance of a keyed collection that the store has no document for was never
        /// written, and is at its collection's start. One of a collection whose keys a store
        /// generates exists only once it has a document, so without one there is nothing there.
        /// </remarks>
        private static ImmutableArray<RuleValue> ReadInstance(
            IReadOnlyDictionary<StateAddress, string> states,
            StateAddress address,
            HeldRuleSet held)
        {
            if (states.ContainsKey(address))
            {
                return ReadAt(states, address, held.Rules);
            }

            return held.Many!.Key is null ? throw new InstanceNotFoundException(address) : held.Many.Start;
        }

        /// <summary>Reads the document at an address: the rule set's own, or the instance of <paramref name="held"/> there.</summary>
        private ImmutableArray<RuleValue> ReadDocument(
            IReadOnlyDictionary<StateAddress, string> states,
            StateAddress address,
            HeldRuleSet? held) =>
            held is null ? ReadAt(states, StateAddress.Root, _ruleSet) : ReadInstance(states, address, held);

        /// <summary>Finds the rule set held at an address, or <see langword="null"/> at the rule set's own document.</summary>
        /// <remarks>The address has been checked by <see cref="CheckAddress(StateAddress)"/>.</remarks>
        private HeldRuleSet? HeldAt(StateAddress at)
        {
            CompiledRuleSet rules = _ruleSet;
            HeldRuleSet? held = null;
            for (int i = 0; i < at.Segments.Length; i += 2)
            {
                held = rules.FindHeld(at.Segments[i])!;
                rules = held.Rules;
            }

            return held;
        }

        /// <summary>Says what is wrong with a key for one collection, or <see langword="null"/> when nothing is.</summary>
        /// <remarks>
        /// A temporary key names a write not yet committed and is never where an input is, in
        /// either kind of collection. A keyed collection's key has to be one its schema admits,
        /// because that is what makes it an instance.
        /// </remarks>
        private static string? KeyFault(HeldRuleSet held, string key)
        {
            if (key.StartsWith(TemporaryKeyPrefix, StringComparison.Ordinal))
            {
                return $"'{key}' is a temporary key, which only a transition that creates an instance writes. "
                    + "The instance is reached by the key the store assigned it.";
            }

            if (held.Many!.Key is SchemaNode schema)
            {
                SchemaViolations violations = new();
                schema.Validate(RuleValue.Text(key), violations.For(held.Alias));
                if (violations.Any)
                {
                    return $"'{key}' is not a key of '{held.Alias}'. {string.Join(" ", violations.Messages)}";
                }
            }

            return null;
        }

        /// <summary>Reads the state document at one address against the rule set that is there.</summary>
        private static ImmutableArray<RuleValue> ReadAt(
            IReadOnlyDictionary<StateAddress, string> states,
            StateAddress address,
            CompiledRuleSet rules)
        {
            if (!states.TryGetValue(address, out string? text))
            {
                throw new RuleDocumentException(
                    $"No state document was supplied at '{address}', and the transition reads it.");
            }

            using JsonDocument state = Parse(text, "state");
            return StateDocument.Read(rules, state.RootElement);
        }

        /// <summary>Finds a projection by the name <see cref="Projections"/> gives it, or tells the caller which there are.</summary>
        /// <remarks>
        /// A projection's own name may not contain a dot, so every segment before the last is an
        /// alias, resolved as an input's name is. Each alias held <c>many</c> is a collection the
        /// projection is asked about one instance of; each held once is a field of the document
        /// above it.
        /// </remarks>
        private ResolvedProjection ResolveProjection(string projection)
        {
            string[] segments = projection.Split('.');
            CompiledRuleSet rules = _ruleSet;
            ImmutableArray<HeldRuleSet>.Builder instances = ImmutableArray.CreateBuilder<HeldRuleSet>();
            ImmutableArray<HeldRuleSet>.Builder fields = ImmutableArray.CreateBuilder<HeldRuleSet>();
            for (int i = 0; i + 1 < segments.Length; i++)
            {
                HeldRuleSet held = rules.FindHeld(segments[i]) ?? throw NoSuchProjection(projection);
                (held.Many is null ? fields : instances).Add(held);
                rules = held.Rules;
            }

            CompiledProjection declared = rules.FindProjection(segments[^1]) ?? throw NoSuchProjection(projection);
            return new ResolvedProjection(rules, declared, instances.ToImmutable(), fields.ToImmutable());
        }

        private ArgumentException NoSuchProjection(string projection)
        {
            ImmutableArray<string> declared = [.. Projections, .. InstanceProjections];
            return new ArgumentException(
                $"'{projection}' is not a projection of '{_ruleSet.Qualified}'."
                + (declared.IsEmpty
                    ? " It declares none, and nothing it holds does."
                    : $" There are {string.Join(", ", declared.Select(static each => $"'{each}'"))}."),
                nameof(projection));
        }

        /// <summary>Refuses a projection of one instance, on a call that reads the rule set's own document only.</summary>
        private static void RequireNoInstance(ResolvedProjection resolved, string projection)
        {
            if (!resolved.Instances.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"'{projection}' is asked of one instance of '{resolved.Instances[^1].Alias}', and instances are in the store "
                    + "the rule set names. ProjectAsync(at, projection) reads the one at an address there.");
            }
        }

        /// <summary>Refuses an address that is not one the projection is asked at.</summary>
        /// <remarks>
        /// The collections on the way to the address are the ones the name passes through, in
        /// order: an instance's projection is asked of an instance, and the rule set's own of
        /// its own document.
        /// </remarks>
        private static void CheckProjectionAt(ResolvedProjection resolved, string projection, StateAddress at)
        {
            bool matches = at.Segments.Length == resolved.Instances.Length * 2;
            for (int i = 0; matches && i < resolved.Instances.Length; i++)
            {
                matches = string.Equals(at.Segments[i * 2], resolved.Instances[i].Alias, StringComparison.Ordinal);
            }

            if (!matches)
            {
                throw new ArgumentException(
                    resolved.Instances.IsEmpty
                        ? $"'{projection}' is asked of the rule set's own document, at '{StateAddress.Root}', not at '{at}'."
                        : $"'{projection}' is asked of one instance of '{resolved.Instances[^1].Alias}', and '{at}' is not the address of one.",
                    nameof(at));
            }
        }

        /// <summary>Evaluates a projection against the document it is asked of.</summary>
        /// <param name="resolved">The projection, and the way to the rule set that declares it.</param>
        /// <param name="document">The fields of the document at the end of the way: the rule set's own, or one instance's.</param>
        /// <param name="collections">Every instance of each collection the projection reads, by alias.</param>
        /// <param name="cancellationToken">Cancels a long evaluation.</param>
        private static string Answer(
            ResolvedProjection resolved,
            ImmutableArray<RuleValue> document,
            IReadOnlyDictionary<string, IReadOnlyList<KeyValuePair<string, string>>> collections,
            CancellationToken cancellationToken)
        {
            // A rule set held once is a field of the document that holds it, and its projection
            // runs against its own state, unpacked as its own expressions are during a transition.
            ImmutableArray<RuleValue> own = document;
            foreach (HeldRuleSet held in resolved.Fields)
            {
                own = held.Schema.Unpack(own[held.Field!.FieldIndex]);
            }

            // Every collection has a position past the fields, in declaration order. Only the
            // ones this projection reads are filled: the compiler refused every other read.
            CompiledRuleSet rules = resolved.Rules;
            CompiledProjection declared = resolved.Declared;
            ImmutableArray<string> aliases = rules.Schema.Collections;
            ImmutableArray<RuleValue>.Builder fields = ImmutableArray.CreateBuilder<RuleValue>(own.Length + aliases.Length);
            fields.AddRange(own);
            foreach (string alias in aliases)
            {
                if (!declared.Collections.Contains(alias, StringComparer.Ordinal))
                {
                    fields.Add(RuleValue.EmptySequence);
                    continue;
                }

                if (!collections.TryGetValue(alias, out IReadOnlyList<KeyValuePair<string, string>>? instances))
                {
                    throw new RuleDocumentException(
                        $"'{declared.Name}' reads every instance of '{alias}', and none were supplied.");
                }

                fields.Add(Every(rules.FindHeld(alias)!, instances));
            }

            EvaluationSession session = new(rules.Definitions, new StateSnapshot(fields.MoveToImmutable()), cancellationToken);
            EvaluationContext context = session.CreateContext(declared.FrameSize);

            return ProjectionDocument.Write(declared.Name, declared.Body.Evaluate(context));
        }

        /// <summary>A projection found by its qualified name, and the way to the rule set that declares it.</summary>
        /// <param name="Rules">The rule set that declares it.</param>
        /// <param name="Declared">The projection.</param>
        /// <param name="Instances">The collections on the way, outermost first: one instance of each is a document read.</param>
        /// <param name="Fields">The rule sets held once on the way after those, each a field of the document before it.</param>
        private sealed record ResolvedProjection(
            CompiledRuleSet Rules,
            CompiledProjection Declared,
            ImmutableArray<HeldRuleSet> Instances,
            ImmutableArray<HeldRuleSet> Fields);

        /// <summary>Reads every instance of a collection, in the order given, as <c>$alias</c> reads it.</summary>
        private static RuleValue Every(HeldRuleSet held, IReadOnlyList<KeyValuePair<string, string>> instances)
        {
            ImmutableArray<RuleValue>.Builder found = ImmutableArray.CreateBuilder<RuleValue>(instances.Count);
            foreach ((string key, string text) in instances)
            {
                if (key.StartsWith(TemporaryKeyPrefix, StringComparison.Ordinal))
                {
                    throw new RuleDocumentException(
                        $"'{held.Alias}/{key}' is under a temporary key, which names a write not yet committed and never a stored instance.");
                }

                using JsonDocument state = Parse(text, "state");
                found.Add(RuleValue.Record(new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["key"] = RuleValue.Text(key),
                    ["state"] = held.Schema.Pack(StateDocument.Read(held.Rules, state.RootElement))
                }));
            }

            return RuleValue.Sequence(found.MoveToImmutable());
        }

        /// <summary>Refuses a one-document call on a rule set whose position is several documents.</summary>
        private void RequireOneDocument()
        {
            if (_ruleSet.KeepsInstances)
            {
                throw new InvalidOperationException(
                    $"'{_ruleSet.Qualified}' keeps instances of what it holds as documents of their own, in the store "
                    + "it names, so its position is not one document. ApplyAsync, GetValidInputsAsync and ProjectAsync "
                    + "read and write it through that store.");
            }
        }

        private TransitionResult Apply(JsonElement input, JsonElement state, CancellationToken cancellationToken)
        {
            Prepared prepared = Begin(input, state, outcomeSupplied: false, cancellationToken);
            return Run(prepared, new DrawTrail([]), cancellationToken);
        }

        /// <summary>Reads the documents, resolves the arguments, and checks the input is allowed.</summary>
        /// <remarks>
        /// <para>
        /// Everything a transition does before its effects run, and everything that does not
        /// depend on which outcome is being resolved. Arguments come from an input's domains
        /// and a guard is refused a draw, so both are settled once however many outcomes the
        /// effects then turn out to have.
        /// </para>
        /// <para>
        /// An input that draws is refused here when no outcome was supplied, before anything
        /// is evaluated. It is decidable from the document, so a caller who reached for the
        /// wrong method is told which method they wanted rather than being handed a fault
        /// from somewhere inside the effects.
        /// </para>
        /// </remarks>
        private Prepared Begin(
            JsonElement input,
            JsonElement state,
            bool outcomeSupplied,
            CancellationToken cancellationToken)
        {
            ImmutableArray<RuleValue> fields = StateDocument.Read(_ruleSet, state);
            InputRequest request = InputDocument.Read(_ruleSet, input);
            Located located = Locate(request);

            return Begin(request, located, new Position(fields, []), outcomeSupplied, null, cancellationToken);
        }

        /// <summary>Resolves the input an input document names, and which instances it is at.</summary>
        /// <remarks>
        /// <para>
        /// Everything here is read off the input document and the rule set, and nothing off a
        /// state. That is what lets <see cref="GetReads"/> say which documents a transition
        /// reads before any of them has been fetched — and what makes that list the one the
        /// transition then reads, rather than a promise about it.
        /// </para>
        /// <para>
        /// An instance is needed for every collection on the way to the input — the one an
        /// input of the instance's own belongs to — and for the one an input of this rule
        /// set's own is <c>at</c>. The document's <c>at</c> has to name exactly those.
        /// </para>
        /// </remarks>
        private Located Locate(InputRequest request)
        {
            if (!Part.TryResolveInput(
                    _ruleSet,
                    request.Input,
                    out ImmutableArray<string> path,
                    out CompiledInput? declared,
                    out HeldRuleSet? held))
            {
                throw new RuleDocumentException($"'{request.Input}' is not an input of '{_ruleSet.Qualified}'.");
            }

            // Every collection on the way down to the rule set that declares the input, and the
            // one the input is at, in that order — which is the order the addresses nest in.
            List<HeldRuleSet> needed = [];
            CompiledRuleSet rules = _ruleSet;
            foreach (string alias in path)
            {
                HeldRuleSet step = rules.FindHeld(alias)!;
                if (step.Many is not null)
                {
                    needed.Add(step);
                }

                rules = step.Rules;
            }

            if (declared!.At is string at)
            {
                needed.Add(rules.FindHeld(at)!);
            }

            ImmutableArray<Step>.Builder instances = ImmutableArray.CreateBuilder<Step>(needed.Count);
            StateAddress address = StateAddress.Root;

            foreach (HeldRuleSet collection in needed)
            {
                if (!request.At.TryGetValue(collection.Alias, out string? key))
                {
                    throw new RuleDocumentException(
                        $"'{request.Input}' is about one instance of '{collection.Alias}', and the input document's "
                        + "'at' does not say which.");
                }

                if (KeyFault(collection, key) is string fault)
                {
                    throw new RuleDocumentException(fault);
                }

                address = address.Child(collection.Alias, key);
                instances.Add(new Step(address, collection));
            }

            foreach (string alias in request.At.Keys)
            {
                if (!needed.Any(collection => string.Equals(collection.Alias, alias, StringComparison.Ordinal)))
                {
                    throw new RuleDocumentException(
                        _ruleSet.CollectionsBelow.Contains(alias, StringComparer.Ordinal)
                            ? $"The input document's 'at' names an instance of '{alias}', and '{request.Input}' "
                              + "is not about one."
                            : $"The input document's 'at' names '{alias}', which is not a collection of "
                              + $"'{_ruleSet.Qualified}'.");
                }
            }

            return new Located(path, declared, held, instances.MoveToImmutable());
        }

        private Prepared Begin(
            InputRequest request,
            Located located,
            Position position,
            bool outcomeSupplied,
            string? createdKey,
            CancellationToken cancellationToken)
        {
            Part root = Part.Of(_ruleSet, position, $"inputs.{request.Input}.effects", cancellationToken);
            ImmutableArray<string> path = located.Path;
            CompiledInput declared = located.Declared;
            HeldRuleSet? held = located.Held;

            if (declared.HasDraw && !outcomeSupplied)
            {
                throw new InvalidOperationException(
                    $"'{request.Input}' resolves something nobody chose, so applying it takes an outcome as well. "
                    + "GetOutcomes says what can happen and where each one leads; ApplyToState with an outcome "
                    + "document replays one that already did.");
            }

            Part part = root.Descend(path);
            Part? holder = path.IsEmpty ? null : root.Descend(path.RemoveAt(path.Length - 1));

            EvaluationContext context = part.Session.CreateContext(declared.FrameSize);
            ImmutableArray<Inadmissible> inadmissible =
                BindArguments(part.Session, request.Input, declared, request, context, cancellationToken);
            SeedAt(part, declared, context);

            // The guard reads no open parameter, so it can be asked even where a schema refused
            // one — and is asked first, because whether the move was on offer at all is a
            // different answer from whether the value that came back is one it takes.
            if (declared.Guard is not null
                && !declared.Guard.Evaluate(context).AsBoolean($"inputs.{request.Input}.when"))
            {
                throw new IllegalInputException(
                    request.Input,
                    $"'{request.Input}' is not allowed in this state.");
            }

            // The guard said the input was on offer; this says whether what came back for an
            // open parameter is a value the rules take. Only an input with an open parameter
            // has any of these, so nothing that was complete when it was offered can be
            // refused here.
            if (Refusal(request.Input, declared, context, inadmissible) is IllegalInputException refused)
            {
                throw refused;
            }

            ImmutableArray<RuleValue> arguments =
                [.. declared.Parameters.Select(parameter => context.GetLocal(parameter.Slot))];

            // Asked after the input's own rule set has allowed it, and only ever able to
            // refuse. Everything a holder says, and every input this one drives, goes through
            // the same code the candidate search uses, so this method refuses exactly what
            // GetValidInputs would not have listed.
            Offer offer = new(
                request.Input,
                declared,
                part,
                held?.FindConstraint(declared.Name),
                holder);

            if (offer.Refusal(arguments.AsSpan(), context, cancellationToken) is string refusal)
            {
                throw new IllegalInputException(
                    request.Input,
                    $"'{request.Input}' is allowed by '{part.Rules.Qualified}', and {refusal}");
            }

            return new Prepared(position, request.Input, path, declared, arguments, createdKey ?? $"{TemporaryKeyPrefix}1");
        }

        /// <summary>Runs an input's effects once, for the outcome a trail describes.</summary>
        /// <remarks>
        /// <para>
        /// Deterministic given the trail, which is the property everything else here rests
        /// on. A fresh session each time, because a memo built while resolving one outcome
        /// has no business being read while resolving another — definitions cannot draw, so
        /// nothing would actually differ, and a cache whose correctness rests on a rule
        /// enforced somewhere else is a cache worth not having.
        /// </para>
        /// <para>
        /// Snapshot semantics are unchanged: every expression reads the position as the input
        /// found it, while the writes pile up in the draft and land together.
        /// </para>
        /// </remarks>
        private TransitionResult Run(Prepared prepared, DrawTrail trail, CancellationToken cancellationToken)
        {
            string origin = $"inputs.{prepared.Name}.effects";
            Part root = Part.Of(_ruleSet, prepared.Position, origin, cancellationToken, trail);

            Drive(root.Descend(prepared.Path), prepared.Name, prepared.Declared, prepared.Arguments, cancellationToken);

            ImmutableArray<RuleValue> next = root.Commit();
            TerminalStatus terminal = EvaluateTerminal(_ruleSet, next, cancellationToken);

            if (!_ruleSet.KeepsInstances)
            {
                return new TransitionResult(_ruleSet.Qualified, WriteData(next), terminal.IsTerminal, terminal.Result);
            }

            Dictionary<StateAddress, ImmutableArray<RuleValue>> arrived = [];
            ImmutableArray<StateWrite> writes = Writes(root, next, prepared, arrived);
            (StateAddress at, TerminalStatus atStatus) = Arrived(root, prepared, terminal, arrived, cancellationToken);

            return new TransitionResult(
                _ruleSet.Qualified,
                WriteData(next),
                terminal.IsTerminal,
                terminal.Result,
                writes)
            {
                At = at,
                AtStatus = atStatus
            };
        }

        /// <summary>Says which document an input was about, and whether it is final now.</summary>
        /// <remarks>
        /// The instance an input creates, where it creates one; otherwise the innermost instance
        /// its <c>at</c> names; otherwise the rule set's own document. Each is asked its own
        /// rule set's <c>terminal</c>, about the state the transition left it in.
        /// </remarks>
        private static (StateAddress At, TerminalStatus Status) Arrived(
            Part root,
            Prepared prepared,
            TerminalStatus terminal,
            IReadOnlyDictionary<StateAddress, ImmutableArray<RuleValue>> arrived,
            CancellationToken cancellationToken)
        {
            if (prepared.Declared.Creates is string creates)
            {
                Part declaring = root.Descend(prepared.Path);
                HeldRuleSet held = declaring.Rules.FindHeld(creates)!;
                return (
                    declaring.Address!.Child(creates, prepared.CreatedKey),
                    EvaluateTerminal(held.Rules, held.Many!.Start, cancellationToken));
            }

            if (prepared.Position.Instances.IsEmpty)
            {
                return (StateAddress.Root, terminal);
            }

            Instance instance = prepared.Position.Instances[^1];
            return (instance.Address, EvaluateTerminal(instance.Held.Rules, arrived[instance.Address], cancellationToken));
        }

        /// <summary>Lists the documents a transition over a position of several documents writes.</summary>
        /// <remarks>
        /// The holder's document when its own inputs wrote to it, each instance an input drove,
        /// outermost first, and a new instance where the input creates one — at the state its
        /// collection starts at, under the temporary key or the one a commit document assigned,
        /// beneath the instance whose rule set declared the input.
        /// </remarks>
        private ImmutableArray<StateWrite> Writes(
            Part root,
            ImmutableArray<RuleValue> next,
            Prepared prepared,
            Dictionary<StateAddress, ImmutableArray<RuleValue>> arrived)
        {
            ImmutableArray<StateWrite>.Builder writes = ImmutableArray.CreateBuilder<StateWrite>();
            if (root.Written)
            {
                writes.Add(new StateWrite(StateAddress.Root, StateDocument.Write(_ruleSet, next), false));
            }

            WriteInstances(root, writes, arrived);

            if (prepared.Declared.Creates is string creates)
            {
                Part declaring = root.Descend(prepared.Path);
                HeldRuleSet held = declaring.Rules.FindHeld(creates)!;
                writes.Add(new StateWrite(
                    declaring.Address!.Child(creates, prepared.CreatedKey),
                    StateDocument.Write(held.Rules, held.Many!.Start),
                    true));
            }

            return writes.ToImmutable();
        }

        /// <summary>Commits each instance bound under a part, and every one under those, and lists the ones written.</summary>
        /// <remarks>Each instance's state as the transition left it goes into <paramref name="arrived"/>, written or not.</remarks>
        private static void WriteInstances(
            Part part,
            ImmutableArray<StateWrite>.Builder writes,
            Dictionary<StateAddress, ImmutableArray<RuleValue>> arrived)
        {
            foreach (Part instance in part.Instances)
            {
                ImmutableArray<RuleValue> fields = instance.Commit();
                arrived[instance.Address!] = fields;
                if (instance.Written)
                {
                    writes.Add(new StateWrite(instance.Address!, StateDocument.Write(instance.Rules, fields), false));
                }

                WriteInstances(instance, writes, arrived);
            }
        }

        /// <summary>Runs one input's effects, and then everything it drives.</summary>
        /// <remarks>
        /// <para>
        /// Recursive, because a rule set a composite holds may hold rule sets of its own and
        /// drive their inputs exactly as the composite drives its. Every level runs against
        /// its own snapshot, its own definitions and its own draft, so snapshot semantics
        /// hold inside a composite transition on the terms they hold in any other: the
        /// expressions read the position as the input found it, and the writes pile up and
        /// land together.
        /// </para>
        /// <para>
        /// Two fired inputs naming one rule set share that rule set's draft, which is what
        /// makes them a set of things that happen rather than a sequence of steps.
        /// </para>
        /// </remarks>
        private static void Drive(
            Part part,
            string name,
            CompiledInput declared,
            ImmutableArray<RuleValue> arguments,
            CancellationToken cancellationToken)
        {
            EvaluationContext context = part.Session.CreateContext(declared.FrameSize);
            for (int i = 0; i < declared.Parameters.Length; i++)
            {
                context.Seed(declared.Parameters[i].Slot, arguments[i]);
            }

            SeedAt(part, declared, context);
            part.Written |= !declared.Effects.IsEmpty;

            foreach (EffectNode effect in declared.Effects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                effect.Apply(context, part.Draft);
            }

            foreach (CompiledFire fire in declared.Fires)
            {
                Fired resolved = Fire(part, fire, context, cancellationToken);
                if (!resolved.Allowed)
                {
                    // Settled before any of this ran, by Begin and by the candidate search.
                    // Reaching it means the two disagreed, which is a fault in the runtime.
                    throw new RuleEvaluationException(
                        $"inputs.{name}.fires",
                        $"'{fire.Name}' was allowed when the input was accepted and is not now: {resolved.Refusal}");
                }

                Drive(part.Held(fire.Alias), fire.Name, fire.Declared, resolved.Arguments, cancellationToken);
            }
        }


        private TransitionResult Replay(
            JsonElement input,
            JsonElement state,
            JsonElement outcome,
            CancellationToken cancellationToken)
        {
            OutcomeRequest resolved = OutcomeDocument.Read(_ruleSet, outcome);
            Prepared prepared = Begin(input, state, outcomeSupplied: true, cancellationToken);

            if (!string.Equals(resolved.Input, prepared.Name, StringComparison.Ordinal))
            {
                throw new RuleDocumentException(
                    $"The outcome resolves '{resolved.Input}', and the input document applies "
                    + $"'{prepared.Name}'.");
            }

            DrawTrail trail = new(resolved.Draws);
            TransitionResult result = Run(prepared, trail, cancellationToken);

            // Reaching the end of the effects without reaching the end of the script means
            // the outcome describes draws that did not happen here, which makes it an outcome
            // belonging to some other state.
            if (trail.Consumed < resolved.Draws.Length)
            {
                throw new RuleDocumentException(
                    $"The outcome writes down {resolved.Draws.Length} draws and '{prepared.Name}' makes "
                    + $"{trail.Consumed} of them in this state, so the two do not describe the same transition.");
            }

            return result;
        }

        /// <summary>The single outcome of an input that draws nothing.</summary>
        /// <remarks>
        /// Not an optimization, though it is one. It is what makes the traversal uniform: a
        /// caller loops over the outcomes of every input it applies, and the loop over an
        /// ordinary input runs once rather than being something the caller had to know not to
        /// write.
        /// </remarks>
        private OutcomeSet Certain(Prepared prepared, CancellationToken cancellationToken)
        {
            TransitionResult result = Run(prepared, new DrawTrail([]), cancellationToken);
            return new OutcomeSet(
                [new Outcome(prepared.Name, [], [], 1, result)],
                coverage: 1,
                evaluated: 1,
                truncated: false);
        }

        /// <summary>Walks the tree of outcomes an input's draws open up.</summary>
        /// <remarks>
        /// <para>
        /// Where the draws are is not known before the effects are evaluated — one may sit
        /// inside a branch an earlier draw decided, and its candidates may be what that draw
        /// left behind — so a branch is found by running the effects with a script and seeing
        /// where they stop. Running them again from the start for each extension is what the
        /// purity of expressions buys: the draft is thrown away and nothing else moved.
        /// </para>
        /// <para>
        /// Best-first, on the probability of the branch so far. Extending a branch can only
        /// make it less likely, so whatever is popped is at least as likely as anything left
        /// in the queue — which makes the outcomes come out in descending order without a
        /// sort, and makes the limit cut the least of the distribution rather than an
        /// arbitrary part of it. Ties are broken by arrival so that two runs of the same
        /// search agree.
        /// </para>
        /// </remarks>
        private OutcomeSet Enumerate(Prepared prepared, int limit, CancellationToken cancellationToken)
        {
            PriorityQueue<Branch, (double Unlikelihood, int Arrival)> pending = new();
            pending.Enqueue(new Branch([], [], 1), (-1, 0));

            ImmutableArray<Outcome>.Builder found = ImmutableArray.CreateBuilder<Outcome>();
            double coverage = 0;
            int evaluated = 0;
            int arrival = 1;
            bool truncated = false;

            while (pending.Count > 0)
            {
                if (found.Count >= limit)
                {
                    truncated = true;
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                Branch branch = pending.Dequeue();
                evaluated++;

                try
                {
                    TransitionResult result = Run(prepared, new DrawTrail(branch.Script), cancellationToken);
                    found.Add(new Outcome(
                        prepared.Name, branch.Script, branch.Written, branch.Probability, result));
                    coverage += branch.Probability;
                }
                catch (UnscriptedDrawException unscripted)
                {
                    Extend(branch, unscripted, pending, ref arrival);
                }
            }

            return new OutcomeSet(found.ToImmutable(), coverage, evaluated, truncated);
        }

        /// <summary>Queues one continuation per thing that could have come out of a draw.</summary>
        private static void Extend(
            Branch branch,
            UnscriptedDrawException unscripted,
            PriorityQueue<Branch, (double Unlikelihood, int Arrival)> pending,
            ref int arrival)
        {
            decimal total = 0;
            foreach (DrawCandidate candidate in unscripted.Candidates)
            {
                total += candidate.Weight;
            }

            foreach (DrawCandidate candidate in unscripted.Candidates)
            {
                // Refused here rather than when the outcome is written, so that a draw
                // producing something unwritable says so with the draw named. A drawn value
                // has to survive the trip out through a document and back, exactly as an
                // input argument does.
                string written = candidate.Value.GetCanonicalText()
                    ?? throw new RuleEvaluationException(
                        unscripted.Origin,
                        $"{RuleValue.Describe(candidate.Value)} has no text form, so it cannot be drawn.");

                double share = branch.Probability * (double)(candidate.Weight / total);
                pending.Enqueue(
                    new Branch(branch.Script.Add(candidate.Value), branch.Written.Add(written), share),
                    (-share, arrival++));
            }
        }

        /// <summary>What a transition has settled before its effects run.</summary>
        private readonly record struct Prepared(
            Position Position,
            string Name,
            ImmutableArray<string> Path,
            CompiledInput Declared,
            ImmutableArray<RuleValue> Arguments,
            string CreatedKey);

        /// <summary>The input an input document names, and the instances it is at.</summary>
        private readonly record struct Located(
            ImmutableArray<string> Path,
            CompiledInput Declared,
            HeldRuleSet? Held,
            ImmutableArray<Step> Instances);

        /// <summary>One instance on the way to an input: where it is, and the collection it belongs to.</summary>
        private readonly record struct Step(StateAddress Address, HeldRuleSet Held);

        /// <summary>The key a new instance is written under until the store assigns its real one.</summary>
        /// <remarks>
        /// The prefix is refused in an input document's <c>at</c> and in what a commit document
        /// assigns, so a temporary key can never be mistaken for a stored one.
        /// </remarks>
        private const string TemporaryKeyPrefix = "~";

        /// <summary>One partly or wholly decided outcome, waiting to be run.</summary>
        private readonly record struct Branch(
            ImmutableArray<RuleValue> Script,
            ImmutableArray<string> Written,
            double Probability);

        /// <summary>Binds an input document's arguments to the parameters they name.</summary>
        /// <remarks>
        /// <para>
        /// Every argument is resolved against its parameter's domain, and what gets bound is
        /// the value the domain produced rather than the one the document carried. Both
        /// halves of that matter.
        /// </para>
        /// <para>
        /// Resolving at all is what makes this method and
        /// <see cref="GetValidInputs(string, int, CancellationToken)"/> agree about what the
        /// rules allow. A domain is not a search hint — it is where a rule set says what a
        /// parameter may be, and a rule set that narrows a domain is stating a rule there. A
        /// runtime that consulted the domain only while searching would accept moves it had
        /// just declined to list, and the more work a rule set moved into its domains the
        /// wider that gap would grow.
        /// </para>
        /// <para>
        /// Binding the domain's value is what makes applying a move mean the same thing as
        /// the candidate it came from. An argument arrives from a document as JSON, so an
        /// opaque value arrives as the text it was written as; substituting the value it
        /// matched leaves every expression downstream seeing exactly what the search saw.
        /// </para>
        /// <para>
        /// An open parameter has no domain, and what its schema refuses is handed back rather
        /// than thrown: that is a refusal of the value, which is answered beside what
        /// <c>validate</c> refuses and after the guard has said whether the move was on offer.
        /// A parameter refused that way is left unbound, and nothing reads it — the guard
        /// cannot, and a clause that would is not asked.
        /// </para>
        /// </remarks>
        private static ImmutableArray<Inadmissible> BindArguments(
            EvaluationSession session,
            string name,
            CompiledInput declared,
            InputRequest request,
            EvaluationContext context,
            CancellationToken cancellationToken)
        {
            // Domains are evaluated with no argument bound, as the candidate search does:
            // a candidate is the product of the domains, so none may depend on another.
            EvaluationContext domainContext = declared.Parameters.IsEmpty
                ? context
                : session.CreateContext(declared.FrameSize);

            ImmutableArray<Inadmissible>.Builder inadmissible = ImmutableArray.CreateBuilder<Inadmissible>();

            foreach (CompiledParameter parameter in declared.Parameters)
            {
                if (!request.Arguments.TryGetValue(parameter.Name, out JsonElement supplied))
                {
                    throw new RuleDocumentException(
                        $"'{name}' takes a '{parameter.Name}', and the input document does not give one.");
                }

                if (parameter.Open is SchemaNode open)
                {
                    if (Admit(parameter.Name, open, supplied, out RuleValue value) is { IsEmpty: false } violations)
                    {
                        inadmissible.Add(new Inadmissible(parameter.Name, parameter.Invalid, violations));
                        continue;
                    }

                    context.Seed(parameter.Slot, value);
                    continue;
                }

                context.Seed(
                    parameter.Slot,
                    Resolve(
                        name,
                        parameter,
                        InputDocument.ReadValue(supplied, parameter.Name),
                        domainContext,
                        cancellationToken));
            }

            foreach (string supplied in request.Arguments.Keys)
            {
                if (declared.FindParameter(supplied) is null)
                {
                    throw new RuleDocumentException($"'{name}' has no parameter named '{supplied}'.");
                }
            }

            return inadmissible.ToImmutable();
        }

        /// <summary>A value an open parameter's schema refused, and what the rule set calls that.</summary>
        /// <param name="Parameter">The parameter.</param>
        /// <param name="Code">The parameter's <c>invalid</c>, or null where it has none.</param>
        /// <param name="Violations">What the schema said is wrong, each starting with the parameter.</param>
        private readonly record struct Inadmissible(string Parameter, string? Code, ImmutableArray<string> Violations);

        /// <summary>Finds the value in a parameter's domain that an argument names.</summary>
        /// <remarks>
        /// The domain is walked only as far as the match, and a domain is usually lazy, so
        /// what this costs is the part of the domain before the argument rather than all of
        /// it.
        /// </remarks>
        private static RuleValue Resolve(
            string name,
            CompiledParameter parameter,
            RuleValue supplied,
            EvaluationContext domainContext,
            CancellationToken cancellationToken)
        {
            string origin = $"inputs.{name}.params.{parameter.Name}.domain";

            foreach (RuleValue candidate in parameter.Domain!.Evaluate(domainContext).AsSequence(origin))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ValueMatch.Matches(supplied, candidate))
                {
                    return candidate;
                }
            }

            throw new IllegalInputException(
                name,
                $"{RuleValue.Describe(supplied)} is not among the values '{parameter.Name}' "
                + $"may take in this state.");
        }

        /// <summary>Reads and admits the argument of a parameter the input leaves open.</summary>
        /// <remarks>
        /// <para>
        /// There is no domain to walk, so the schema standing in for one does both halves of
        /// what a domain does. It reads the JSON, because a schema node owns the form its
        /// values are written in — which is what lets an open parameter hand the rules a value
        /// of its own kind rather than the text somebody typed. Then it says whether that value
        /// is one it admits.
        /// </para>
        /// <para>
        /// Malformed JSON is reported and a best-effort value returned rather than thrown, so
        /// checking it a second time would only name the same mistake twice.
        /// </para>
        /// </remarks>
        /// <returns>What the schema refused, each starting with the parameter; empty when it admits the value.</returns>
        private static ImmutableArray<string> Admit(
            string parameter,
            SchemaNode open,
            JsonElement supplied,
            out RuleValue value)
        {
            SchemaViolations violations = new();
            value = open.ReadJson(supplied, violations.For(parameter));

            if (!violations.Any)
            {
                open.Validate(value, violations.For(parameter));
            }

            return violations.Messages;
        }

        /// <summary>Says why the arguments that came back are refused, or <see langword="null"/> when they are not.</summary>
        /// <remarks>
        /// <para>
        /// What the schemas refused comes first, in parameter order, and then what the clauses
        /// refuse, in the order they are written — every one of them, so a form wrong in three
        /// places takes one round trip to learn that. A refusal the rule set gave a code is a
        /// rejection under that code; one it gave none is the schema's sentence.
        /// </para>
        /// <para>
        /// Where nothing but those sentences refuses, the answer is a plain
        /// <see cref="IllegalInputException"/> carrying them, as it was before a rule set could
        /// name the refusal: a rule set that named none has said nothing a host could key on.
        /// </para>
        /// </remarks>
        private static IllegalInputException? Refusal(
            string name,
            CompiledInput declared,
            EvaluationContext context,
            ImmutableArray<Inadmissible> inadmissible)
        {
            ImmutableArray<InputRejection>.Builder rejected = ImmutableArray.CreateBuilder<InputRejection>();
            ImmutableArray<string>.Builder unexplained = ImmutableArray.CreateBuilder<string>();

            foreach (Inadmissible refused in inadmissible)
            {
                if (refused.Code is string code)
                {
                    rejected.Add(new InputRejection(code, refused.Parameter));
                }
                else
                {
                    unexplained.AddRange(refused.Violations);
                }
            }

            rejected.AddRange(Rejections(name, declared, context, [.. inadmissible.Select(static refused => refused.Parameter)]));

            if (rejected.Count > 0)
            {
                return new InputRejectedException(name, rejected.ToImmutable(), unexplained.ToImmutable());
            }

            return unexplained.Count > 0
                ? new IllegalInputException(name, string.Join(" ", unexplained))
                : null;
        }

        /// <summary>Asks every clause of an input's <c>validate</c>, and collects what refuses.</summary>
        /// <remarks>
        /// Every clause is evaluated rather than stopping at the first failure, because a form
        /// wrong in three places should take one round trip to learn that and not three. The
        /// cost is that a clause relying on an earlier one having passed is evaluated anyway,
        /// which is why they are written independent of each other. The exception is a clause
        /// reading a parameter whose schema refused its value: there is no value of the kind it
        /// was written against, and that parameter has already been refused.
        /// </remarks>
        private static ImmutableArray<InputRejection> Rejections(
            string name,
            CompiledInput declared,
            EvaluationContext context,
            ImmutableArray<string> unbound)
        {
            if (declared.Validations.IsEmpty)
            {
                return [];
            }

            ImmutableArray<InputRejection>.Builder refused =
                ImmutableArray.CreateBuilder<InputRejection>();

            for (int i = 0; i < declared.Validations.Length; i++)
            {
                CompiledValidation clause = declared.Validations[i];
                if (clause.Reads.Any(unbound.Contains))
                {
                    continue;
                }

                if (!clause.Require.Evaluate(context).AsBoolean($"inputs.{name}.validate[{i}].require"))
                {
                    refused.Add(new InputRejection(clause.Code, clause.Parameter));
                }
            }

            return refused.ToImmutable();
        }

        /// <summary>Sifts everything one rule set offers, and then everything it holds.</summary>
        /// <remarks>
        /// <para>
        /// Depth-first over the aliases, so a rule set two levels down offers its inputs as
        /// <c>a.b.c</c> and is sifted by its own guard, then by whatever holds it. The cost is
        /// additive: each rule set's domains are enumerated once, and a holder adds one guard
        /// evaluation per candidate it has an opinion about.
        /// </para>
        /// <para>
        /// A rule set holding nothing runs this once over its own inputs, which is the path it
        /// ran before composition existed.
        /// </para>
        /// </remarks>
        private static void Offered(
            Part part,
            Part? holder,
            HeldRuleSet? through,
            string prefix,
            Search search,
            CancellationToken cancellationToken)
        {
            foreach (CompiledInput input in part.Rules.Inputs)
            {
                if (search.Exhausted)
                {
                    return;
                }

                HeldConstraint? constraint = through?.FindConstraint(input.Name);

                // An input its holder refuses everywhere is not enumerated at all. Walking its
                // domains to discard every candidate would spend the caller's limit on
                // candidates that cannot come back, which is the one way a hidden input could
                // cost something.
                if (constraint?.Never == true)
                {
                    continue;
                }

                Collect(
                    new Offer(prefix + input.Name, input, part, constraint, holder),
                    search,
                    cancellationToken);
            }

            foreach (HeldRuleSet held in part.Rules.Held)
            {
                if (search.Exhausted)
                {
                    return;
                }

                // An instance is offered at its own address, never alongside its holder's moves.
                if (held.Many is not null)
                {
                    continue;
                }

                Offered(part.Held(held.Alias), part, held, prefix + held.Alias + ".", search, cancellationToken);
            }
        }

        /// <summary>Sifts what is offered at one address of a rule set that keeps instances.</summary>
        /// <param name="part">The rule set's part, or an instance's on the way down.</param>
        /// <param name="holder">The part holding <paramref name="part"/>, whose <c>held</c> guard reads it.</param>
        /// <param name="through">What <paramref name="holder"/> holds <paramref name="part"/> as.</param>
        /// <param name="prefix">What the inputs of <paramref name="part"/> are qualified with.</param>
        /// <param name="below">The collections still to go down through to the address, outermost first.</param>
        /// <param name="search">The search.</param>
        /// <param name="cancellationToken">Cancels a long search.</param>
        /// <remarks>
        /// At the address itself: the inputs of its rule set that are at no instance, and what
        /// it holds once. One level above it: the inputs that are at its collection. Each is
        /// sifted by its own rules and by what <c>held</c> says, as a held rule set's always
        /// is, so <c>months.submit</c> is offered at a claim of a month with whatever holds the
        /// months asked as well.
        /// </remarks>
        private static void OfferedAt(
            Part part,
            Part? holder,
            HeldRuleSet? through,
            string prefix,
            ImmutableArray<string> below,
            Search search,
            CancellationToken cancellationToken)
        {
            string? at = below.IsEmpty ? null : below[0];
            if (below.Length <= 1)
            {
                foreach (CompiledInput input in part.Rules.Inputs)
                {
                    if (search.Exhausted)
                    {
                        return;
                    }

                    HeldConstraint? constraint = through?.FindConstraint(input.Name);
                    if (string.Equals(input.At, at, StringComparison.Ordinal) && constraint?.Never != true)
                    {
                        Collect(new Offer(prefix + input.Name, input, part, constraint, holder), search, cancellationToken);
                    }
                }
            }

            if (at is null)
            {
                foreach (HeldRuleSet held in part.Rules.Held)
                {
                    if (search.Exhausted)
                    {
                        return;
                    }

                    if (held.Many is null)
                    {
                        Offered(part.Held(held.Alias), part, held, prefix + held.Alias + ".", search, cancellationToken);
                    }
                }

                return;
            }

            if (!search.Exhausted)
            {
                OfferedAt(
                    part.Held(at),
                    part,
                    part.Rules.FindHeld(at),
                    prefix + at + ".",
                    below.RemoveAt(0),
                    search,
                    cancellationToken);
            }
        }

        /// <summary>Binds <c>@alias</c> for an input that is at an instance.</summary>
        private static void SeedAt(Part part, CompiledInput declared, EvaluationContext context)
        {
            if (declared.AtSlot is LocalSlot slot)
            {
                context.Seed(slot, part.Held(declared.At!).AtValue);
            }
        }

        private static void Collect(
            Offer offer,
            Search search,
            CancellationToken cancellationToken)
        {
            CompiledInput input = offer.Declared;

            // Domains are evaluated once per input, with no argument bound: a candidate is
            // the product of the domains, so none of them may depend on another's choice.
            EvaluationContext domainContext = offer.Part.Session.CreateContext(input.FrameSize);

            // An open parameter has no domain, and contributes one candidate rather than a
            // domain's worth of them: the move is offered with a hole where its argument goes.
            // So a form is one candidate however many values it could eventually be given.
            SequenceValue?[] domains = new SequenceValue?[input.Parameters.Length];
            for (int i = 0; i < domains.Length; i++)
            {
                domains[i] = input.Parameters[i].Domain
                    ?.Evaluate(domainContext)
                    .AsSequence($"inputs.{offer.Name}.params.{input.Parameters[i].Name}.domain");
            }

            RuleValue[] chosen = new RuleValue[domains.Length];
            Walk(offer, domains, chosen, 0, search, cancellationToken);
        }

        private static void Walk(
            Offer offer,
            SequenceValue?[] domains,
            RuleValue[] chosen,
            int depth,
            Search search,
            CancellationToken cancellationToken)
        {
            if (search.Exhausted)
            {
                return;
            }

            if (depth == domains.Length)
            {
                Consider(offer, chosen, search, cancellationToken);
                return;
            }

            if (domains[depth] is not SequenceValue domain)
            {
                // Open: nothing to choose from, and nothing reads the slot while candidates
                // are being formed — which the compiler has already made certain of.
                chosen[depth] = RuleValue.Null;
                Walk(offer, domains, chosen, depth + 1, search, cancellationToken);
                return;
            }

            foreach (RuleValue value in domain)
            {
                cancellationToken.ThrowIfCancellationRequested();

                chosen[depth] = value;
                Walk(offer, domains, chosen, depth + 1, search, cancellationToken);
                if (search.Exhausted)
                {
                    return;
                }
            }
        }

        private static void Consider(
            Offer offer,
            RuleValue[] chosen,
            Search search,
            CancellationToken cancellationToken)
        {
            CompiledInput input = offer.Declared;

            if (!search.Take())
            {
                return;
            }

            EvaluationContext context = offer.Part.Session.CreateContext(input.FrameSize);
            for (int i = 0; i < chosen.Length; i++)
            {
                if (!input.Parameters[i].IsOpen)
                {
                    context.Seed(input.Parameters[i].Slot, chosen[i]);
                }
            }

            SeedAt(offer.Part, input, context);

            if (input.Guard is not null && !input.Guard.Evaluate(context).AsBoolean($"inputs.{offer.Name}.when"))
            {
                return;
            }

            if (!offer.Allows(chosen, context, cancellationToken))
            {
                return;
            }

            // Both built in the order the parameters were declared, which is the order the
            // move reads back in and the order it is written out in. Nothing downstream
            // sorts them, so this loop is where that order is decided.
            ImmutableArray<KeyValuePair<string, string>>.Builder arguments =
                ImmutableArray.CreateBuilder<KeyValuePair<string, string>>();
            ImmutableArray<KeyValuePair<string, RuleValue>>.Builder values =
                ImmutableArray.CreateBuilder<KeyValuePair<string, RuleValue>>();
            ImmutableArray<OpenParameter>.Builder open = ImmutableArray.CreateBuilder<OpenParameter>();
            ImmutableArray<string>.Builder parameters =
                ImmutableArray.CreateBuilder<string>(chosen.Length);

            for (int i = 0; i < chosen.Length; i++)
            {
                CompiledParameter parameter = input.Parameters[i];
                parameters.Add(parameter.Name);

                if (parameter.IsOpen)
                {
                    open.Add(new OpenParameter(
                        parameter.Name,
                        parameter.OpenOp,
                        parameter.OpenField,
                        parameter.Open!.Describe()));
                    continue;
                }

                // Refused here rather than when the document is written, so that a rule set
                // whose domain yields something unwritable says so with the parameter named.
                string text = chosen[i].GetCanonicalText()
                    ?? throw new RuleEvaluationException(
                        $"inputs.{offer.Name}.params.{parameter.Name}",
                        $"{RuleValue.Describe(chosen[i])} has no text form, so it cannot be an input argument.");

                arguments.Add(new KeyValuePair<string, string>(parameter.Name, text));
                values.Add(new KeyValuePair<string, RuleValue>(parameter.Name, chosen[i]));
            }

            string? actor = input.Actor?.Evaluate(context).GetCanonicalText();
            search.Found.Add(new ValidInput(
                offer.Name,
                values.ToImmutable(),
                new ArgumentList(arguments.ToImmutable()),
                new OpenParameterList(open.ToImmutable()),
                parameters.MoveToImmutable(),
                actor,
                search.At));
        }

        /// <summary>A position as it was read: the rule set's own fields, and each instance an input is at.</summary>
        /// <param name="Root">The rule set's own fields, in schema order.</param>
        /// <param name="Instances">The instances, read from their own documents.</param>
        private sealed record Position(ImmutableArray<RuleValue> Root, ImmutableArray<Instance> Instances);

        /// <summary>One instance of a collection, read from its document.</summary>
        private readonly record struct Instance(StateAddress Address, HeldRuleSet Held, ImmutableArray<RuleValue> Fields);

        /// <summary>One rule set's part of a composite state, and what is happening to it.</summary>
        /// <remarks>
        /// <para>
        /// A composite's state is one document, and every rule set inside it has a part of
        /// that document, a snapshot of it, the session its own expressions run in, and a
        /// draft collecting its own writes. This is that, and it is a tree because a rule set
        /// a composite holds may hold rule sets of its own.
        /// </para>
        /// <para>
        /// Parts are made once per call and reached by alias, so two things that touch one
        /// component touch one part: the definition results it memoises last a whole
        /// <c>GetValidInputs</c> sweep, and two fired inputs naming it accumulate into one
        /// draft over one snapshot.
        /// </para>
        /// <para>
        /// Every draft is sealed. Effects at any depth are refused a write to a field holding
        /// a rule set's state, which is the restriction the rest of composition rests on and
        /// is not one a level of nesting is allowed to shed.
        /// </para>
        /// </remarks>
        private sealed class Part
        {
            private readonly Dictionary<string, Part> _held = new(StringComparer.Ordinal);
            private readonly string _origin;
            private readonly CancellationToken _cancellationToken;
            private readonly DrawTrail? _trail;

            private Part(
                CompiledRuleSet rules,
                ImmutableArray<RuleValue> fields,
                string origin,
                CancellationToken cancellationToken,
                DrawTrail? trail)
            {
                Rules = rules;
                Snapshot = new StateSnapshot(fields);
                Session = new EvaluationSession(rules.Definitions, Snapshot, cancellationToken, trail);
                Draft = new StateDraft(Snapshot, rules.Schema.Fields) { SealedOrigin = origin };
                _origin = origin;
                _cancellationToken = cancellationToken;
                _trail = trail;
            }

            public CompiledRuleSet Rules { get; }

            public StateSnapshot Snapshot { get; }

            public EvaluationSession Session { get; }

            public StateDraft Draft { get; }

            /// <summary>Gets whether a transition wrote to this part's document, or to a field holding a rule set inside it.</summary>
            /// <remarks>
            /// Settled by which inputs ran, not by whether a value changed, so it is a fact about
            /// the input document and the rule set: the same move writes the same documents in
            /// every position it is legal in.
            /// </remarks>
            public bool Written { get; set; }

            /// <summary>Gets where this part's document is, or null when its state is a field of another's.</summary>
            /// <remarks>
            /// <see cref="StateAddress.Root"/> for the rule set a position is read for, and an
            /// instance's own address for each instance read with it.
            /// </remarks>
            public StateAddress? Address { get; private init; }

            /// <summary>Gets what <c>@alias</c> reads for this instance: its key and its state, as found.</summary>
            public RuleValue AtValue => _atValue ??= RuleValue.Record(new Dictionary<string, RuleValue>(StringComparer.Ordinal)
            {
                ["key"] = RuleValue.Text(Address!.Segments[^1]),
                ["state"] = _packing!.Pack(Snapshot.Fields)
            });

            private HeldStateSchema? _packing;
            private RuleValue? _atValue;

            public static Part Of(
                CompiledRuleSet rules,
                ImmutableArray<RuleValue> fields,
                string origin,
                CancellationToken cancellationToken,
                DrawTrail? trail = null) =>
                new(rules, fields, origin, cancellationToken, trail);

            /// <summary>Makes the part a position is, with the instances it was read with bound under their aliases.</summary>
            /// <remarks>
            /// The instances come outermost first, so each one's holder is bound before it is:
            /// a claim of a month goes under the month it was read with.
            /// </remarks>
            public static Part Of(
                CompiledRuleSet rules,
                Position position,
                string origin,
                CancellationToken cancellationToken,
                DrawTrail? trail = null)
            {
                Part root = new(rules, position.Root, origin, cancellationToken, trail) { Address = StateAddress.Root };
                foreach (Instance instance in position.Instances)
                {
                    Part holder = root;
                    ImmutableArray<string> segments = instance.Address.Segments;
                    for (int i = 0; i + 2 < segments.Length; i += 2)
                    {
                        holder = holder._held[segments[i]];
                    }

                    holder._held.Add(
                        instance.Held.Alias,
                        new Part(instance.Held.Rules, instance.Fields, origin, cancellationToken, trail)
                        {
                            Address = instance.Address,
                            _packing = instance.Held.Schema
                        });
                }

                return root;
            }

            /// <summary>Gets the instances bound directly under this part.</summary>
            public IEnumerable<Part> Instances => _held.Values.Where(static part => part.Address is not null);

            /// <summary>Gets the part a held rule set occupies, making it the first time.</summary>
            /// <remarks>
            /// A component's expressions were compiled against its own schema and its own
            /// definitions, so they run against its own snapshot. Unpacking the field it occupies
            /// is the whole of what a composite has to do to make that so, and it is what keeps a
            /// component's meaning independent of who holds it.
            /// </remarks>
            public Part Held(string alias)
            {
                if (!_held.TryGetValue(alias, out Part? part))
                {
                    HeldRuleSet held = Rules.FindHeld(alias)!;

                    // An instance is bound when the position is read, from the document the input
                    // named. Reaching one that was not means the runtime read less than it said
                    // it would.
                    StatePath field = held.Field
                        ?? throw new InvalidOperationException(
                            $"No instance of '{alias}' was read, and evaluation reached for one.");

                    part = new Part(
                        held.Rules,
                        held.Schema.Unpack(Snapshot[field.FieldIndex]),
                        _origin,
                        _cancellationToken,
                        _trail);

                    _held.Add(alias, part);
                }

                return part;
            }

            /// <summary>Follows a chain of aliases to the part that declares an input.</summary>
            public Part Descend(ImmutableArray<string> path)
            {
                Part part = this;
                foreach (string alias in path)
                {
                    part = part.Held(alias);
                }

                return part;
            }

            /// <summary>Splits a qualified input name into the aliases leading to it, and the input.</summary>
            /// <param name="rules">The rule set the name is relative to.</param>
            /// <param name="name">The name an input document gave, relative to that rule set.</param>
            /// <param name="path">Receives the aliases, outermost first.</param>
            /// <param name="declared">Receives the input.</param>
            /// <param name="through">Receives the held rule set the input belongs to.</param>
            /// <returns><see langword="true"/> when the name names an input.</returns>
            /// <remarks>
            /// An input's own name may not contain a dot, so every segment before the last is
            /// an alias and there is nothing to disambiguate. Nesting goes as deep as the
            /// documents do: <c>a.b.c</c> is what <c>a</c> holds under <c>b</c>, and its input
            /// <c>c</c>.
            /// </remarks>
            public static bool TryResolveInput(
                CompiledRuleSet rules,
                string name,
                out ImmutableArray<string> path,
                out CompiledInput? declared,
                out HeldRuleSet? through)
            {
                ImmutableArray<string>.Builder aliases = ImmutableArray.CreateBuilder<string>();
                through = null;

                ReadOnlySpan<char> rest = name;
                while (true)
                {
                    int dot = rest.IndexOf('.');
                    if (dot < 0)
                    {
                        declared = rules.FindInput(rest.ToString());
                        path = aliases.ToImmutable();
                        return declared is not null;
                    }

                    HeldRuleSet? held = rules.FindHeld(rest[..dot].ToString());
                    if (held is null)
                    {
                        declared = null;
                        through = null;
                        path = [];
                        return false;
                    }

                    aliases.Add(held.Alias);
                    rules = held.Rules;
                    through = held;
                    rest = rest[(dot + 1)..];
                }
            }

            /// <summary>Produces the state this part arrives at, with everything it holds folded in.</summary>
            /// <remarks>
            /// An instance of a collection is a document of its own and is not folded in; it is
            /// committed on its own, as its own write.
            /// </remarks>
            public ImmutableArray<RuleValue> Commit()
            {
                foreach ((string alias, Part part) in _held)
                {
                    HeldRuleSet held = Rules.FindHeld(alias)!;
                    if (held.Field is not StatePath field)
                    {
                        continue;
                    }

                    Draft.Adopt(field, held.Schema.Pack(part.Commit()));
                    Written |= part.Written;
                }

                return Draft.Commit(_origin);
            }
        }

        /// <summary>One input on offer, and everything needed to decide whether it is allowed.</summary>
        /// <remarks>
        /// <see cref="Part"/> is the rule set that declared the input; <see cref="Holder"/> is
        /// the one that holds it, and is what a <c>held</c> guard reads — the whole composed
        /// state, which is the thing neither document could see on its own.
        /// </remarks>
        private readonly record struct Offer(
            string Name,
            CompiledInput Declared,
            Part Part,
            HeldConstraint? Constraint,
            Part? Holder)
        {
            /// <summary>Asks whether anything refuses a candidate its own rule set allowed.</summary>
            /// <param name="chosen">The candidate's arguments, in the input's parameter order.</param>
            /// <param name="context">The context the guard was evaluated in, arguments bound.</param>
            /// <param name="cancellationToken">Cancels a long domain walk.</param>
            /// <returns><see langword="true"/> when nothing refuses it.</returns>
            public bool Allows(
                ReadOnlySpan<RuleValue> chosen,
                EvaluationContext context,
                CancellationToken cancellationToken) =>
                Refusal(chosen, context, cancellationToken) is null;

            /// <summary>Says why a candidate is refused, or <see langword="null"/> when it is not.</summary>
            /// <remarks>
            /// Two things can refuse, and neither can allow anything: the <c>held</c> guard
            /// whoever holds this rule set wrote over the input, and — for an input that fires
            /// — every input it drives, which has to be one its own rule set would have
            /// allowed anyway.
            /// </remarks>
            public string? Refusal(
                ReadOnlySpan<RuleValue> chosen,
                EvaluationContext context,
                CancellationToken cancellationToken)
            {
                if (Constraint is HeldConstraint constraint && Holder is not null)
                {
                    EvaluationContext outer = Holder.Session.CreateContext(constraint.FrameSize);
                    for (int i = 0; i < chosen.Length && i < constraint.ParameterSlots.Length; i++)
                    {
                        if (!Declared.Parameters[i].IsOpen)
                        {
                            outer.Seed(constraint.ParameterSlots[i], chosen[i]);
                        }
                    }

                    if (constraint.AtSlot is LocalSlot atSlot)
                    {
                        outer.Seed(atSlot, Part.AtValue);
                    }

                    if (!constraint.When.Evaluate(outer).AsBoolean($"held.{Name}.when"))
                    {
                        return $"'held.{Name}.when' refuses it in this state.";
                    }
                }

                foreach (CompiledFire fire in Declared.Fires)
                {
                    Fired resolved = Fire(Part, fire, context, cancellationToken);
                    if (!resolved.Allowed)
                    {
                        return $"it drives '{fire.Name}', and {resolved.Refusal}";
                    }
                }

                return null;
            }
        }

        /// <summary>What a fired input resolved to, and whether it may run.</summary>
        private readonly record struct Fired(bool Allowed, ImmutableArray<RuleValue> Arguments, string Refusal);

        /// <summary>Resolves a fired input's arguments and asks its rule set whether it is allowed.</summary>
        /// <remarks>
        /// <para>
        /// The component's own two questions, in the order it asks them: each argument has to
        /// be a value its domain produces, and then its guard has to accept it. That is what
        /// keeps a walk of the component alone an upper bound on what a composite does to it —
        /// driving an input is never a way past the component's own rules. An input that
        /// fires in turn is asked the same about what it drives, so a chain of them is legal
        /// only where the whole chain is.
        /// </para>
        /// <para>
        /// The holder's <c>held</c> guard is <em>not</em> asked, and the two are not the same
        /// question. <c>held</c> says when a rule set offers something it holds as a move of
        /// its own; <c>fires</c> is it taking that move itself, having already decided. Asking
        /// here would make <c>"when": false</c> — which is how an input is hidden so that the
        /// only way to it is the one that drives it — mean the input can never happen at all.
        /// </para>
        /// <para>
        /// The arguments are expressions over the state the transition found, with the driving
        /// input's parameters bound, and so is every guard here. A <c>fires</c> list is
        /// therefore a set of inputs all legal <em>now</em> rather than a script of steps:
        /// none of them can depend on another's writes, and none has to be guarded against
        /// them.
        /// </para>
        /// </remarks>
        private static Fired Fire(
            Part part,
            CompiledFire fire,
            EvaluationContext outer,
            CancellationToken cancellationToken)
        {
            Part inner = part.Held(fire.Alias);
            CompiledInput declared = fire.Declared;

            EvaluationContext domains = inner.Session.CreateContext(declared.FrameSize);
            EvaluationContext bound = inner.Session.CreateContext(declared.FrameSize);
            ImmutableArray<RuleValue>.Builder arguments =
                ImmutableArray.CreateBuilder<RuleValue>(declared.Parameters.Length);

            for (int i = 0; i < declared.Parameters.Length; i++)
            {
                CompiledParameter parameter = declared.Parameters[i];
                RuleValue supplied = fire.Arguments[i].Evaluate(outer);
                RuleValue? matched = null;

                // A fired argument is an expression, so it has a value even where the
                // component leaves the parameter open: nothing is waiting to be typed, and
                // what stands in for the domain says whether this value is admissible.
                if (parameter.Open is SchemaNode open)
                {
                    SchemaViolations violations = new();
                    open.Validate(supplied, violations.For(parameter.Name));
                    if (violations.Any)
                    {
                        return new Fired(false, [], string.Join(" ", violations.Messages));
                    }

                    matched = supplied;
                }
                else
                {
                    foreach (RuleValue candidate in parameter.Domain!
                        .Evaluate(domains)
                        .AsSequence($"inputs.{fire.Name}.params.{parameter.Name}.domain"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (ValueMatch.Matches(supplied, candidate))
                        {
                            matched = candidate;
                            break;
                        }
                    }
                }

                if (matched is null)
                {
                    return new Fired(
                        false,
                        [],
                        $"{RuleValue.Describe(supplied)} is not among the values '{parameter.Name}' "
                        + $"may take in this state.");
                }

                arguments.Add(matched);
                bound.Seed(parameter.Slot, matched);
            }

            if (declared.Guard is not null
                && !declared.Guard.Evaluate(bound).AsBoolean($"inputs.{fire.Name}.when"))
            {
                return new Fired(false, [], $"'{inner.Rules.Qualified}' does not allow it in this state.");
            }

            // After the guard, as on the applying path: whether the input was available at all
            // is a different question from whether this argument is one it takes, and asking
            // them in one order everywhere is what makes the two paths give one answer.
            if (Rejections(fire.Name, declared, bound, []) is { IsEmpty: false } rejected)
            {
                return new Fired(
                    false,
                    [],
                    $"'{inner.Rules.Qualified}' does not accept that: "
                    + string.Join(", ", rejected.Select(static rejection => rejection.Code)) + ".");
            }

            foreach (CompiledFire onwards in declared.Fires)
            {
                Fired further = Fire(inner, onwards, bound, cancellationToken);
                if (!further.Allowed)
                {
                    return new Fired(false, [], $"it drives '{onwards.Name}', and {further.Refusal}");
                }
            }

            return new Fired(true, arguments.ToImmutable(), string.Empty);
        }

        /// <summary>Asks a rule set's <c>terminal</c> about a state of it: this one's, or one it holds.</summary>
        private static TerminalStatus EvaluateTerminal(
            CompiledRuleSet rules,
            ImmutableArray<RuleValue> fields,
            CancellationToken cancellationToken)
        {
            if (rules.Terminal is not CompiledTerminal terminal)
            {
                return new TerminalStatus(false, null);
            }

            EvaluationSession session = new(rules.Definitions, new StateSnapshot(fields), cancellationToken);
            EvaluationContext context = session.CreateContext(terminal.FrameSize);

            if (!terminal.When.Evaluate(context).AsBoolean("terminal.when"))
            {
                return new TerminalStatus(false, null);
            }

            // The outcome is only asked for once the game is over. A rule set is entitled to
            // leave it undefined — or faulting — mid-game.
            return new TerminalStatus(true, terminal.Result?.Evaluate(context).GetCanonicalText());
        }

        private string WriteData(ImmutableArray<RuleValue> fields)
        {
            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
            {
                StateDocument.WriteData(writer, _ruleSet, fields);
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        private static JsonDocument Parse(string json, string what)
        {
            try
            {
                return JsonDocument.Parse(json, RuleRuntime.ParseOptions);
            }
            catch (JsonException exception)
            {
                throw new RuleDocumentException($"The {what} document is not valid JSON.", exception);
            }
        }

        private static async Task<JsonDocument> ParseAsync(Stream json, string what, CancellationToken cancellationToken)
        {
            try
            {
                return await JsonDocument.ParseAsync(json, RuleRuntime.ParseOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                throw new RuleDocumentException($"The {what} document is not valid JSON.", exception);
            }
        }

        /// <summary>How far the candidate search has got, and whether it may go further.</summary>
        private sealed class Search(int limit, StateAddress? at = null)
        {
            /// <summary>Gets the address the moves are found at, which each one is written with.</summary>
            public StateAddress At { get; } = at ?? StateAddress.Root;

            public ImmutableArray<ValidInput>.Builder Found { get; } = ImmutableArray.CreateBuilder<ValidInput>();

            public int Evaluated { get; private set; }

            public bool Exhausted { get; private set; }

            public bool Take()
            {
                if (Evaluated >= limit)
                {
                    Exhausted = true;
                    return false;
                }

                Evaluated++;
                return true;
            }
        }
    }

    /// <summary>Whether a state is final, and what the rule set says the outcome was.</summary>
    /// <param name="IsTerminal">Whether the rule set considers the state final.</param>
    /// <param name="Result">
    /// The outcome, or <see langword="null"/> when the state is not final or the rule set
    /// declares no result.
    /// </param>
    public readonly record struct TerminalStatus(bool IsTerminal, string? Result)
    {
        /// <inheritdoc />
        public override string ToString() =>
            IsTerminal
                ? string.Create(CultureInfo.InvariantCulture, $"terminal{(Result is null ? string.Empty : $" ({Result})")}")
                : "ongoing";
    }
}
