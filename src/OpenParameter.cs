// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Rulealize.Abstraction.Value;

namespace Rulealize
{
    /// <summary>A parameter an available move has not been given a value for.</summary>
    /// <remarks>
    /// <para>
    /// A parameter with a domain arrives already chosen: <c>GetValidInputs</c> formed a
    /// candidate per value and put each one to the guard, so what comes back is a move the
    /// rules have allowed. A parameter left <c>open</c> cannot work that way — the value comes
    /// from outside, and there was nothing to enumerate — so the move is offered with this
    /// standing where its argument will go.
    /// </para>
    /// <para>
    /// What it carries is the name and the operation the rule set wrote the admitting schema
    /// as. A host offers an editor against the operation, which is the name it already knows
    /// the vocabulary by, in the same way it draws a board against <c>grid.board</c>.
    /// </para>
    /// </remarks>
    public sealed class OpenParameter
    {
        internal OpenParameter(string name, string? op, string? field, RecordValue description)
        {
            Name = name;
            Op = op;
            Field = field;
            Description = description;
        }

        /// <summary>Gets the parameter name, as the rule set declared it.</summary>
        public string Name { get; }

        /// <summary>Gets the <c>op</c> of the schema admitting its value.</summary>
        public string? Op { get; }

        /// <summary>Gets the bounds the admitting schema declares, as a record.</summary>
        /// <remarks>
        /// <para>
        /// What a caller needs to ask somebody for a value without restating the rules: a
        /// length, a range, the values an enumeration allows. Empty where the schema declares
        /// nothing, and the keys are the schema's own — read them against <see cref="Op"/>,
        /// which is the name the rule set knows that vocabulary by. Nothing interpreted it on
        /// the way out.
        /// </para>
        /// <para>
        /// This is the half of a refusal that can be settled before asking. Whether the value
        /// is one the rules admit is still <c>ApplyToState</c>'s answer, and a bound checked
        /// here is checked again there — the point is not to skip it but to know it in time to
        /// say so.
        /// </para>
        /// <para>
        /// Bounds only. What the field is called and how it is drawn are the caller's own.
        /// </para>
        /// </remarks>
        public RecordValue Description { get; }

        /// <summary>Gets the state field this parameter is edited into, where it names one.</summary>
        /// <remarks>
        /// A parameter written as <c>{ "field": "name" }</c> admits exactly what that field
        /// holds, because it is that field's schema node. The name is here because a screen
        /// asking somebody for a value needs to call it something, and the field already has
        /// wording — wording is keyed by pointer into the rule set, and this is the pointer.
        /// </remarks>
        public string? Field { get; }

        /// <inheritdoc />
        public override string ToString() => Op is null ? Name : $"{Name}: <{Op}>";
    }

    /// <summary>The parameters of one move that are still open, in declared order.</summary>
    /// <remarks>
    /// Addressable both ways for the reason <see cref="ArgumentList"/> is: a host walking a
    /// form goes in order, and one asking about a named field looks it up. Order is what is
    /// kept and the lookup is a scan, because an input has a handful of parameters at most.
    /// </remarks>
    public sealed class OpenParameterList : IReadOnlyList<OpenParameter>
    {
        private readonly ImmutableArray<OpenParameter> _parameters;

        internal OpenParameterList(ImmutableArray<OpenParameter> parameters) => _parameters = parameters;

        /// <summary>Gets the number of parameters still open.</summary>
        public int Count => _parameters.Length;

        /// <summary>Gets a value indicating whether nothing here is waiting for a value.</summary>
        public bool IsEmpty => _parameters.IsEmpty;

        /// <summary>Gets the open parameter at a position, counting in declared parameter order.</summary>
        public OpenParameter this[int index] => _parameters[index];

        /// <summary>Gets the open parameter of that name.</summary>
        /// <param name="name">The parameter name.</param>
        /// <returns>The parameter.</returns>
        /// <exception cref="KeyNotFoundException">This move has no open parameter of that name.</exception>
        public OpenParameter this[string name] => TryGetValue(name, out OpenParameter? parameter)
            ? parameter
            : throw new KeyNotFoundException($"'{name}' is not an open parameter of this move.");

        /// <summary>Says whether a named parameter of this move is still open.</summary>
        /// <param name="name">The parameter name.</param>
        /// <returns><see langword="true"/> when it is.</returns>
        public bool ContainsKey(string name) => TryGetValue(name, out _);

        /// <summary>Gets the open parameter of that name, where there is one.</summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="parameter">The parameter.</param>
        /// <returns><see langword="true"/> when that parameter is open.</returns>
        public bool TryGetValue(string name, [MaybeNullWhen(false)] out OpenParameter parameter)
        {
            foreach (OpenParameter candidate in _parameters)
            {
                if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
                {
                    parameter = candidate;
                    return true;
                }
            }

            parameter = null;
            return false;
        }

        /// <inheritdoc />
        public IEnumerator<OpenParameter> GetEnumerator() =>
            ((IEnumerable<OpenParameter>)_parameters).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
