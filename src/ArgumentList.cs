// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Rulealize
{
    /// <summary>The arguments one input was called with, in the order its parameters are declared.</summary>
    /// <remarks>
    /// <para>
    /// Addressable both ways, because both get asked. A host writing a move down walks it in
    /// order — the order the rule set declared the parameters in, and the order
    /// <see cref="ValidInput.ToInputDocument"/> writes them back out — and a host asking what
    /// one named parameter came out as looks it up by name.
    /// </para>
    /// <para>
    /// A dictionary answers the second question and loses the first. Its enumeration order is
    /// its hash order, and .NET reseeds string hashing per process, so the same move would
    /// read back one way today and the other way tomorrow: stable within a run, different
    /// between runs, invisible with one argument. A text form that varies between runs is not
    /// a text form, and a host that renders one and matches it back would be right about half
    /// the time.
    /// </para>
    /// <para>
    /// So order is the property that is kept and the lookup is a scan. An input has a handful
    /// of parameters at most — the cost is nothing, and it buys a rendering that is the same
    /// in every process.
    /// </para>
    /// </remarks>
    public sealed class ArgumentList : IReadOnlyList<KeyValuePair<string, string>>
    {
        private readonly ImmutableArray<KeyValuePair<string, string>> _arguments;

        internal ArgumentList(ImmutableArray<KeyValuePair<string, string>> arguments) => _arguments = arguments;

        /// <summary>Gets the number of arguments, one per declared parameter.</summary>
        public int Count => _arguments.Length;

        /// <summary>Gets a value indicating whether the input takes no arguments.</summary>
        public bool IsEmpty => _arguments.IsEmpty;

        /// <summary>Gets the argument at a position, counting in declared parameter order.</summary>
        public KeyValuePair<string, string> this[int index] => _arguments[index];

        /// <summary>Gets what a named parameter was called with.</summary>
        /// <param name="name">The parameter name.</param>
        /// <returns>The argument, rendered as text.</returns>
        /// <exception cref="KeyNotFoundException">This input declares no parameter of that name.</exception>
        public string this[string name] => TryGetValue(name, out string? value)
            ? value
            : throw new KeyNotFoundException($"'{name}' is not a parameter of this input.");

        /// <summary>Gets the parameter names, in declared order.</summary>
        public IEnumerable<string> Keys => _arguments.Select(static argument => argument.Key);

        /// <summary>Says whether this input declares a parameter of that name.</summary>
        /// <param name="name">The parameter name.</param>
        /// <returns><see langword="true"/> when it does.</returns>
        public bool ContainsKey(string name) => TryGetValue(name, out _);

        /// <summary>Gets what a named parameter was called with, where it has one.</summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="value">The argument, rendered as text.</param>
        /// <returns><see langword="true"/> when this input declares that parameter.</returns>
        public bool TryGetValue(string name, [MaybeNullWhen(false)] out string value)
        {
            foreach ((string declared, string argument) in _arguments)
            {
                if (string.Equals(declared, name, StringComparison.Ordinal))
                {
                    value = argument;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<string, string>>)_arguments).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
