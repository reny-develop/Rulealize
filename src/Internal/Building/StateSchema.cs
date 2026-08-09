// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Node;

namespace Rulealize.Internal.Building
{
    /// <summary>The fields declared by <c>state.schema</c>.</summary>
    /// <remarks>
    /// <para>
    /// Each field gets a position, and every reference to it — from a read, from a write,
    /// from an effect that needs somewhere to put a board — is resolved to that position
    /// while the rule set is built. At evaluation time reading the state is an array index.
    /// </para>
    /// <para>
    /// Paths are dotted for the sake of a nesting the schema vocabulary does not have yet,
    /// so at present only a bare field name resolves.
    /// </para>
    /// </remarks>
    internal sealed class StateSchema : IStateSchemaResolver
    {
        private readonly Dictionary<string, StatePath> _byName = new(StringComparer.Ordinal);
        private readonly List<StatePath> _fields = [];

        /// <summary>Gets the fields, in declaration order.</summary>
        public ImmutableArray<StatePath> Fields => [.. _fields];

        /// <summary>Gets the number of fields.</summary>
        public int FieldCount => _fields.Count;

        /// <summary>Declares a field.</summary>
        /// <param name="name">The field name.</param>
        /// <param name="schema">Its schema node.</param>
        /// <returns>The resolved path, or <see langword="null"/> when the name is already taken.</returns>
        public StatePath? Declare(string name, SchemaNode schema)
        {
            if (_byName.ContainsKey(name))
            {
                return null;
            }

            StatePath path = new(name, _fields.Count, schema);
            _byName.Add(name, path);
            _fields.Add(path);
            return path;
        }

        /// <inheritdoc />
        public bool TryResolve(string path, [NotNullWhen(true)] out StatePath? resolved) =>
            _byName.TryGetValue(path, out resolved);
    }
}
