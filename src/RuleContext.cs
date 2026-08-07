// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Nodes;
using Rulealize.Abstraction.Values;
using Rulealize.Internal.Documents;
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
        public Task<TransitionResult> ApplyToStateAsync(
            string inputDocument,
            string stateDocument,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputDocument);
            ArgumentNullException.ThrowIfNull(stateDocument);

            using JsonDocument input = Parse(inputDocument, "input");
            using JsonDocument state = Parse(stateDocument, "state");
            return Task.FromResult(Apply(input.RootElement, state.RootElement, cancellationToken));
        }

        /// <summary>Applies an input to a state, reading both from streams.</summary>
        /// <param name="inputDocument">A <c>rulealize/input/v1</c> document.</param>
        /// <param name="stateDocument">A <c>rulealize/state/v1</c> document.</param>
        /// <param name="cancellationToken">Cancels reading or a long evaluation.</param>
        /// <returns>Where the transition arrived.</returns>
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
        /// Synchronous by design. This walks a domain and evaluates a guard against every
        /// member of it — thousands of node evaluations for one call — and an asynchronous
        /// signature over that hot path would cost more than it could buy.
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
            ImmutableArray<RuleValue> fields = StateDocument.Read(_ruleSet, state);
            InputRequest request = InputDocument.Read(_ruleSet, input);

            CompiledInput declared = _ruleSet.FindInput(request.Input)
                ?? throw new RuleDocumentException($"'{request.Input}' is not an input of '{_ruleSet.Qualified}'.");

            StateSnapshot snapshot = new(fields);
            EvaluationSession session = new(_ruleSet.Definitions, snapshot, cancellationToken);
            EvaluationContext context = session.CreateContext(declared.FrameSize);
            BindArguments(declared, request, context);

            if (declared.Guard is not null
                && !declared.Guard.Evaluate(context).AsBoolean($"inputs.{declared.Name}.when"))
            {
                throw new IllegalInputException(
                    declared.Name,
                    $"'{declared.Name}' is not allowed in this state.");
            }

            // Snapshot semantics: every expression below reads the position as it was, while
            // the writes pile up in the draft and land together.
            StateDraft draft = new(snapshot, _ruleSet.Schema.FieldCount);
            foreach (EffectNode effect in declared.Effects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                effect.Apply(context, draft);
            }

            ImmutableArray<RuleValue> next = draft.Commit();
            TerminalStatus terminal = EvaluateTerminal(next, cancellationToken);
            return new TransitionResult(_ruleSet.Qualified, WriteData(next), terminal.IsTerminal, terminal.Result);
        }

        private void BindArguments(CompiledInput declared, InputRequest request, EvaluationContext context)
        {
            foreach (CompiledParameter parameter in declared.Parameters)
            {
                if (!request.Arguments.TryGetValue(parameter.Name, out RuleValue? value))
                {
                    throw new RuleDocumentException(
                        $"'{declared.Name}' takes a '{parameter.Name}', and the input document does not give one.");
                }

                context.Seed(parameter.Slot, value);
            }

            foreach (string supplied in request.Arguments.Keys)
            {
                if (declared.FindParameter(supplied) is null)
                {
                    throw new RuleDocumentException($"'{declared.Name}' has no parameter named '{supplied}'.");
                }
            }
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
