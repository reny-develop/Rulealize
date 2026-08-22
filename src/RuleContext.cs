// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Evaluation;
using Rulealize.Abstraction.Node;
using Rulealize.Abstraction.Value;
using Rulealize.Internal.Document;
using Rulealize.Internal.Evaluation;
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

        internal RuleContext(CompiledRuleSet ruleSet)
        {
            _ruleSet = ruleSet;
        }

        /// <summary>Gets the rule set's identifier.</summary>
        public string Id => _ruleSet.Id;

        /// <summary>Gets the rule set's version.</summary>
        public string Version => _ruleSet.Version;

        /// <summary>Gets the identity a state document carries, <c>id@version</c>.</summary>
        public string RuleSet => _ruleSet.Qualified;

        /// <summary>Gets the names of the inputs this rule set declares, in document order.</summary>
        public ImmutableArray<string> Inputs => [.. _ruleSet.Inputs.Select(static input => input.Name)];

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
        /// without one being named.
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
        public async Task<TransitionResult> ApplyToStateAsync(
            Stream inputDocument,
            Stream stateDocument,
            CancellationToken cancellationToken = default)
        {
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
        public async Task<TransitionResult> ApplyToStateAsync(
            Stream inputDocument,
            Stream stateDocument,
            Stream outcomeDocument,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputDocument);
            ArgumentNullException.ThrowIfNull(stateDocument);
            ArgumentNullException.ThrowIfNull(outcomeDocument);

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

            using JsonDocument state = Parse(stateDocument, "state");
            StateSnapshot snapshot = new(StateDocument.Read(_ruleSet, state.RootElement));
            EvaluationSession session = new(_ruleSet.Definitions, snapshot, cancellationToken);

            Search search = new(validationLimit);
            foreach (CompiledInput input in _ruleSet.Inputs)
            {
                if (search.Exhausted)
                {
                    break;
                }

                Collect(session, input, search, cancellationToken);
            }

            return new ValidInputSet(search.Found.ToImmutable(), search.Evaluated, search.Exhausted);
        }

        /// <summary>Asks whether a state is final, and what its outcome is.</summary>
        /// <param name="stateDocument">A <c>rulealize/state/v1</c> document.</param>
        /// <param name="cancellationToken">Cancels a long evaluation.</param>
        /// <returns>The verdict. A rule set with no <c>terminal</c> section never reports one.</returns>
        public TerminalStatus GetTerminalStatus(string stateDocument, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stateDocument);

            using JsonDocument state = Parse(stateDocument, "state");
            return EvaluateTerminal(StateDocument.Read(_ruleSet, state.RootElement), cancellationToken);
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

            CompiledInput declared = _ruleSet.FindInput(request.Input)
                ?? throw new RuleDocumentException($"'{request.Input}' is not an input of '{_ruleSet.Qualified}'.");

            if (declared.HasDraw && !outcomeSupplied)
            {
                throw new InvalidOperationException(
                    $"'{declared.Name}' resolves something nobody chose, so applying it takes an outcome as well. "
                    + "GetOutcomes says what can happen and where each one leads; ApplyToState with an outcome "
                    + "document replays one that already did.");
            }

            StateSnapshot snapshot = new(fields);
            EvaluationSession session = new(_ruleSet.Definitions, snapshot, cancellationToken);
            EvaluationContext context = session.CreateContext(declared.FrameSize);
            BindArguments(session, declared, request, context, cancellationToken);

            if (declared.Guard is not null
                && !declared.Guard.Evaluate(context).AsBoolean($"inputs.{declared.Name}.when"))
            {
                throw new IllegalInputException(
                    declared.Name,
                    $"'{declared.Name}' is not allowed in this state.");
            }

            ImmutableArray<RuleValue> arguments =
                [.. declared.Parameters.Select(parameter => context.GetLocal(parameter.Slot))];

            return new Prepared(fields, declared, arguments);
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
            StateSnapshot snapshot = new(prepared.Fields);
            EvaluationSession session = new(_ruleSet.Definitions, snapshot, cancellationToken, trail);
            EvaluationContext context = session.CreateContext(prepared.Declared.FrameSize);

            for (int i = 0; i < prepared.Declared.Parameters.Length; i++)
            {
                context.Seed(prepared.Declared.Parameters[i].Slot, prepared.Arguments[i]);
            }

            StateDraft draft = new(snapshot, _ruleSet.Schema.Fields);
            foreach (EffectNode effect in prepared.Declared.Effects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                effect.Apply(context, draft);
            }

            ImmutableArray<RuleValue> next = draft.Commit($"inputs.{prepared.Declared.Name}.effects");
            TerminalStatus terminal = EvaluateTerminal(next, cancellationToken);
            return new TransitionResult(_ruleSet.Qualified, WriteData(next), terminal.IsTerminal, terminal.Result);
        }

        private TransitionResult Replay(
            JsonElement input,
            JsonElement state,
            JsonElement outcome,
            CancellationToken cancellationToken)
        {
            OutcomeRequest resolved = OutcomeDocument.Read(_ruleSet, outcome);
            Prepared prepared = Begin(input, state, outcomeSupplied: true, cancellationToken);

            if (!string.Equals(resolved.Input, prepared.Declared.Name, StringComparison.Ordinal))
            {
                throw new RuleDocumentException(
                    $"The outcome resolves '{resolved.Input}', and the input document applies "
                    + $"'{prepared.Declared.Name}'.");
            }

            DrawTrail trail = new(resolved.Draws);
            TransitionResult result = Run(prepared, trail, cancellationToken);

            // Reaching the end of the effects without reaching the end of the script means
            // the outcome describes draws that did not happen here, which makes it an outcome
            // belonging to some other state.
            if (trail.Consumed < resolved.Draws.Length)
            {
                throw new RuleDocumentException(
                    $"The outcome writes down {resolved.Draws.Length} draws and '{prepared.Declared.Name}' makes "
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
                [new Outcome(prepared.Declared.Name, [], [], 1, result)],
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
                        prepared.Declared.Name, branch.Script, branch.Written, branch.Probability, result));
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
            ImmutableArray<RuleValue> Fields,
            CompiledInput Declared,
            ImmutableArray<RuleValue> Arguments);

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
        /// </remarks>
        private void BindArguments(
            EvaluationSession session,
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

            foreach (CompiledParameter parameter in declared.Parameters)
            {
                if (!request.Arguments.TryGetValue(parameter.Name, out RuleValue? value))
                {
                    throw new RuleDocumentException(
                        $"'{declared.Name}' takes a '{parameter.Name}', and the input document does not give one.");
                }

                context.Seed(parameter.Slot, Resolve(declared, parameter, value, domainContext, cancellationToken));
            }

            foreach (string supplied in request.Arguments.Keys)
            {
                if (declared.FindParameter(supplied) is null)
                {
                    throw new RuleDocumentException($"'{declared.Name}' has no parameter named '{supplied}'.");
                }
            }
        }

        /// <summary>Finds the value in a parameter's domain that an argument names.</summary>
        /// <remarks>
        /// The domain is walked only as far as the match, and a domain is usually lazy, so
        /// what this costs is the part of the domain before the argument rather than all of
        /// it.
        /// </remarks>
        private static RuleValue Resolve(
            CompiledInput declared,
            CompiledParameter parameter,
            RuleValue supplied,
            EvaluationContext domainContext,
            CancellationToken cancellationToken)
        {
            string origin = $"inputs.{declared.Name}.params.{parameter.Name}.domain";

            foreach (RuleValue candidate in parameter.Domain.Evaluate(domainContext).AsSequence(origin))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ValueMatch.Matches(supplied, candidate))
                {
                    return candidate;
                }
            }

            throw new IllegalInputException(
                declared.Name,
                $"{RuleValue.Describe(supplied)} is not among the values '{parameter.Name}' "
                + $"may take in this state.");
        }

        private void Collect(
            EvaluationSession session,
            CompiledInput input,
            Search search,
            CancellationToken cancellationToken)
        {
            // Domains are evaluated once per input, with no argument bound: a candidate is
            // the product of the domains, so none of them may depend on another's choice.
            EvaluationContext domainContext = session.CreateContext(input.FrameSize);
            SequenceValue[] domains = new SequenceValue[input.Parameters.Length];
            for (int i = 0; i < domains.Length; i++)
            {
                domains[i] = input.Parameters[i].Domain
                    .Evaluate(domainContext)
                    .AsSequence($"inputs.{input.Name}.params.{input.Parameters[i].Name}.domain");
            }

            RuleValue[] chosen = new RuleValue[domains.Length];
            Walk(session, input, domains, chosen, 0, search, cancellationToken);
        }

        private void Walk(
            EvaluationSession session,
            CompiledInput input,
            SequenceValue[] domains,
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
                Consider(session, input, chosen, search);
                return;
            }

            foreach (RuleValue value in domains[depth])
            {
                cancellationToken.ThrowIfCancellationRequested();

                chosen[depth] = value;
                Walk(session, input, domains, chosen, depth + 1, search, cancellationToken);
                if (search.Exhausted)
                {
                    return;
                }
            }
        }

        private void Consider(EvaluationSession session, CompiledInput input, RuleValue[] chosen, Search search)
        {
            if (!search.Take())
            {
                return;
            }

            EvaluationContext context = session.CreateContext(input.FrameSize);
            for (int i = 0; i < chosen.Length; i++)
            {
                context.Seed(input.Parameters[i].Slot, chosen[i]);
            }

            if (input.Guard is not null && !input.Guard.Evaluate(context).AsBoolean($"inputs.{input.Name}.when"))
            {
                return;
            }

            ImmutableDictionary<string, string>.Builder arguments =
                ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            ImmutableArray<KeyValuePair<string, RuleValue>>.Builder values =
                ImmutableArray.CreateBuilder<KeyValuePair<string, RuleValue>>(chosen.Length);

            for (int i = 0; i < chosen.Length; i++)
            {
                string name = input.Parameters[i].Name;

                // Refused here rather than when the document is written, so that a rule set
                // whose domain yields something unwritable says so with the parameter named.
                arguments[name] = chosen[i].GetCanonicalText()
                    ?? throw new RuleEvaluationException(
                        $"inputs.{input.Name}.params.{name}",
                        $"{RuleValue.Describe(chosen[i])} has no text form, so it cannot be an input argument.");

                values.Add(new KeyValuePair<string, RuleValue>(name, chosen[i]));
            }

            string? actor = input.Actor?.Evaluate(context).GetCanonicalText();
            search.Found.Add(new ValidInput(input.Name, values.MoveToImmutable(), arguments.ToImmutable(), actor));
        }

        private TerminalStatus EvaluateTerminal(ImmutableArray<RuleValue> fields, CancellationToken cancellationToken)
        {
            if (_ruleSet.Terminal is not CompiledTerminal terminal)
            {
                return new TerminalStatus(false, null);
            }

            EvaluationSession session = new(_ruleSet.Definitions, new StateSnapshot(fields), cancellationToken);
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
        private sealed class Search(int limit)
        {
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
