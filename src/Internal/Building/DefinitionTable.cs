// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Rulealize.Abstraction.Building;

namespace Rulealize.Internal.Building
{
    /// <summary>Resolves definition names, and records what depends on what while it does.</summary>
    /// <remarks>
    /// <para>
    /// Every name is known before any body is built, so a definition may refer to one
    /// declared after it. What that permits, a cycle, is then ruled out by the graph these
    /// resolutions build up.
    /// </para>
    /// <para>
    /// Recursion is refused because termination could not otherwise be guaranteed, and
    /// <c>GetValidInputs</c> evaluates hundreds of candidates — one definition that does not
    /// terminate stops everything. Ruling it out also means the cost of an evaluation has an
    /// upper bound that can be estimated from the document.
    /// </para>
    /// </remarks>
    internal sealed class DefinitionTable : IDefinitionResolver
    {
        private readonly Dictionary<string, DefinitionDescriptor> _byName = new(StringComparer.Ordinal);
        private readonly List<DefinitionDescriptor> _ordered = [];
        private readonly List<HashSet<int>> _dependencies = [];
        private int _current = -1;

        /// <summary>Gets the descriptors, in declaration order.</summary>
        public ImmutableArray<DefinitionDescriptor> Descriptors => [.. _ordered];

        /// <summary>Declares a definition before its body is built.</summary>
        /// <param name="name">The name.</param>
        /// <param name="parameters">The parameter names, in declaration order.</param>
        /// <returns>The descriptor, or <see langword="null"/> when the name is already taken.</returns>
        public DefinitionDescriptor? Declare(string name, ImmutableArray<string> parameters)
        {
            if (_byName.ContainsKey(name))
            {
                return null;
            }

            DefinitionDescriptor descriptor = new(name, parameters, _ordered.Count);
            _byName.Add(name, descriptor);
            _ordered.Add(descriptor);
            _dependencies.Add([]);
            return descriptor;
        }

        /// <summary>Marks which definition's body is being built, so references from it are recorded.</summary>
        /// <param name="index">The definition's position, or -1 outside any definition.</param>
        public void EnterDefinition(int index) => _current = index;

        /// <inheritdoc />
        public bool TryResolve(string name, [NotNullWhen(true)] out DefinitionDescriptor? definition)
        {
            if (!_byName.TryGetValue(name, out definition))
            {
                return false;
            }

            if (_current >= 0)
            {
                _dependencies[_current].Add(definition.Index);
            }

            return true;
        }

        /// <summary>Looks for a cycle in the reference graph.</summary>
        /// <returns>
        /// The names on a cycle, in the order they refer to one another and ending back at
        /// the first, or <see langword="null"/> when the graph is acyclic.
        /// </returns>
        public ImmutableArray<string>? FindCycle()
        {
            byte[] state = new byte[_ordered.Count];
            List<int> path = [];

            for (int i = 0; i < _ordered.Count; i++)
            {
                ImmutableArray<string>? cycle = Visit(i, state, path);
                if (cycle is not null)
                {
                    return cycle;
                }
            }

            return null;
        }

        private ImmutableArray<string>? Visit(int index, byte[] state, List<int> path)
        {
            if (state[index] == 2)
            {
                return null;
            }

            if (state[index] == 1)
            {
                int start = path.IndexOf(index);
                return [.. path.Skip(start).Select(i => _ordered[i].Name), _ordered[index].Name];
            }

            state[index] = 1;
            path.Add(index);

            foreach (int dependency in _dependencies[index])
            {
                ImmutableArray<string>? cycle = Visit(dependency, state, path);
                if (cycle is not null)
                {
                    return cycle;
                }
            }

            path.RemoveAt(path.Count - 1);
            state[index] = 2;
            return null;
        }
    }
}
