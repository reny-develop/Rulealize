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
            ImmutableArray<RuleValue> fields = StateDocument.Read(_ruleSet, state.RootElement);
            Part root = Part.Of(_ruleSet, fields, "inputs", cancellationToken);

            Search search = new(validationLimit);
            Offered(root, null, null, string.Empty, search, cancellationToken);

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

            Part root = Part.Of(_ruleSet, fields, $"inputs.{request.Input}.effects", cancellationToken);
            if (!root.TryResolveInput(
                    request.Input,
                    out ImmutableArray<string> path,
                    out CompiledInput? declared,
                    out HeldRuleSet? held))
            {
                throw new RuleDocumentException($"'{request.Input}' is not an input of '{_ruleSet.Qualified}'.");
            }

            if (declared!.HasDraw && !outcomeSupplied)
            {
                throw new InvalidOperationException(
                    $"'{request.Input}' resolves something nobody chose, so applying it takes an outcome as well. "
                    + "GetOutcomes says what can happen and where each one leads; ApplyToState with an outcome "
                    + "document replays one that already did.");
            }

            Part part = root.Descend(path);
            Part? holder = path.IsEmpty ? null : root.Descend(path.RemoveAt(path.Length - 1));

            EvaluationContext context = part.Session.CreateContext(declared.FrameSize);
            BindArguments(part.Session, request.Input, declared, request, context, cancellationToken);

            if (declared.Guard is not null
                && !declared.Guard.Evaluate(context).AsBoolean($"inputs.{request.Input}.when"))
            {
                throw new IllegalInputException(
                    request.Input,
                    $"'{request.Input}' is not allowed in this state.");
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

            return new Prepared(fields, request.Input, path, declared, arguments);
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
            Part root = Part.Of(_ruleSet, prepared.Fields, origin, cancellationToken, trail);

            Drive(root.Descend(prepared.Path), prepared.Name, prepared.Declared, prepared.Arguments, cancellationToken);

            ImmutableArray<RuleValue> next = root.Commit();
            TerminalStatus terminal = EvaluateTerminal(next, cancellationToken);
            return new TransitionResult(_ruleSet.Qualified, WriteData(next), terminal.IsTerminal, terminal.Result);
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
            ImmutableArray<RuleValue> Fields,
            string Name,
            ImmutableArray<string> Path,
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
        private static void BindArguments(
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

            foreach (CompiledParameter parameter in declared.Parameters)
            {
                if (!request.Arguments.TryGetValue(parameter.Name, out RuleValue? value))
                {
                    throw new RuleDocumentException(
                        $"'{name}' takes a '{parameter.Name}', and the input document does not give one.");
                }

                context.Seed(parameter.Slot, Resolve(name, declared, parameter, value, domainContext, cancellationToken));
            }

            foreach (string supplied in request.Arguments.Keys)
            {
                if (declared.FindParameter(supplied) is null)
                {
                    throw new RuleDocumentException($"'{name}' has no parameter named '{supplied}'.");
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
            string name,
            CompiledInput declared,
            CompiledParameter parameter,
            RuleValue supplied,
            EvaluationContext domainContext,
            CancellationToken cancellationToken)
        {
            string origin = $"inputs.{name}.params.{parameter.Name}.domain";

            foreach (RuleValue candidate in parameter.Domain.Evaluate(domainContext).AsSequence(origin))
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

                Offered(part.Held(held.Alias), part, held, prefix + held.Alias + ".", search, cancellationToken);
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
            SequenceValue[] domains = new SequenceValue[input.Parameters.Length];
            for (int i = 0; i < domains.Length; i++)
            {
                domains[i] = input.Parameters[i].Domain
                    .Evaluate(domainContext)
                    .AsSequence($"inputs.{offer.Name}.params.{input.Parameters[i].Name}.domain");
            }

            RuleValue[] chosen = new RuleValue[domains.Length];
            Walk(offer, domains, chosen, 0, search, cancellationToken);
        }

        private static void Walk(
            Offer offer,
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
                Consider(offer, chosen, search, cancellationToken);
                return;
            }

            foreach (RuleValue value in domains[depth])
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
                context.Seed(input.Parameters[i].Slot, chosen[i]);
            }

            if (input.Guard is not null && !input.Guard.Evaluate(context).AsBoolean($"inputs.{offer.Name}.when"))
            {
                return;
            }

            if (!offer.Allows(chosen, context, cancellationToken))
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
                        $"inputs.{offer.Name}.params.{name}",
                        $"{RuleValue.Describe(chosen[i])} has no text form, so it cannot be an input argument.");

                values.Add(new KeyValuePair<string, RuleValue>(name, chosen[i]));
            }

            string? actor = input.Actor?.Evaluate(context).GetCanonicalText();
            search.Found.Add(new ValidInput(offer.Name, values.MoveToImmutable(), arguments.ToImmutable(), actor));
        }

        /// <summary>Builds a session over one held rule set's part of a composite state.</summary>
        /// <remarks>
        /// A component's expressions were compiled against its own schema and its own
        /// definitions, so they run against its own snapshot. Unpacking the field it occupies
        /// is the whole of what a composite has to do to make that so, and it is what keeps a
        /// component's meaning independent of who holds it.
        /// </remarks>
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

            public static Part Of(
                CompiledRuleSet rules,
                ImmutableArray<RuleValue> fields,
                string origin,
                CancellationToken cancellationToken,
                DrawTrail? trail = null) =>
                new(rules, fields, origin, cancellationToken, trail);

            /// <summary>Gets the part a held rule set occupies, making it the first time.</summary>
            public Part Held(string alias)
            {
                if (!_held.TryGetValue(alias, out Part? part))
                {
                    HeldRuleSet held = Rules.FindHeld(alias)!;
                    part = new Part(
                        held.Rules,
                        held.Schema.Unpack(Snapshot[held.Field.FieldIndex]),
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
            /// <param name="name">The name an input document gave, relative to this part.</param>
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
            public bool TryResolveInput(
                string name,
                out ImmutableArray<string> path,
                out CompiledInput? declared,
                out HeldRuleSet? through)
            {
                ImmutableArray<string>.Builder aliases = ImmutableArray.CreateBuilder<string>();
                CompiledRuleSet rules = Rules;
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
            public ImmutableArray<RuleValue> Commit()
            {
                foreach ((string alias, Part part) in _held)
                {
                    HeldRuleSet held = Rules.FindHeld(alias)!;
                    Draft.Adopt(held.Field, held.Schema.Pack(part.Commit()));
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
                        outer.Seed(constraint.ParameterSlots[i], chosen[i]);
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

                foreach (RuleValue candidate in parameter.Domain
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
