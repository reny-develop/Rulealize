// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Rulealize.Abstraction;
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
        private readonly Dictionary<string, string> _ops = new(StringComparer.Ordinal);
        private readonly List<StatePath> _fields = [];
        private readonly List<string> _collections = [];
        private readonly Dictionary<string, StatePath> _collectionPaths = new(StringComparer.Ordinal);
        private HashSet<string>? _collectionsRead;

        /// <summary>Gets the fields, in declaration order.</summary>
        public ImmutableArray<StatePath> Fields => [.. _fields];

        /// <summary>Gets the number of fields.</summary>
        public int FieldCount => _fields.Count;

        /// <summary>Declares a field.</summary>
        /// <param name="name">The field name.</param>
        /// <param name="schema">Its schema node.</param>
        /// <param name="op">The operation the schema was written as, where a document wrote one.</param>
        /// <returns>The resolved path, or <see langword="null"/> when the name is already taken.</returns>
        public StatePath? Declare(string name, SchemaNode schema, string? op = null)
        {
            if (_byName.ContainsKey(name))
            {
                return null;
            }

            StatePath path = new(name, _fields.Count, schema);
            _byName.Add(name, path);
            if (op is not null)
            {
                _ops.Add(name, op);
            }

            _fields.Add(path);
            return path;
        }

        /// <summary>Declares a collection: the alias of a <c>uses</c> entry held <c>many</c>.</summary>
        /// <param name="alias">The alias.</param>
        /// <returns><see langword="false"/> when the name is already a field.</returns>
        /// <remarks>
        /// <para>
        /// A collection is not a field. The holder's document does not contain its instances,
        /// so nothing writes it and <see cref="Fields"/> does not list it. What it is, is
        /// something a projection can read: <c>$claims</c> is every instance, a sequence in key
        /// order of the records <c>@claims</c> is one of.
        /// </para>
        /// <para>
        /// It resolves to a position past the last field, which a snapshot built for a
        /// projection fills and no other snapshot has. Resolving it anywhere else is refused
        /// while the document is compiled, so the position is never read where it is missing.
        /// </para>
        /// </remarks>
        public bool DeclareCollection(string alias)
        {
            if (_byName.ContainsKey(alias))
            {
                return false;
            }

            _collections.Add(alias);
            return true;
        }

        /// <summary>Gets the collections, in declaration order — the order their positions follow the fields in.</summary>
        public ImmutableArray<string> Collections => [.. _collections];

        /// <summary>Gets where the location being built is, for a refusal.</summary>
        /// <remarks>Set by the <see cref="NodeBuilder"/> that resolves against this schema.</remarks>
        public Func<SourcePath>? CurrentPath { get; set; }

        /// <summary>Lets collections be read until the returned handle is disposed, and records which are.</summary>
        /// <param name="read">Where to record the aliases read.</param>
        /// <returns>The handle.</returns>
        /// <remarks>
        /// In force while a projection is built and at no other time. A guard or an effect
        /// that read every instance would read documents its transition does not write, and a
        /// definition body, <c>terminal</c> and a held guard are each evaluated where one of
        /// those is, or against the holder's document alone.
        /// </remarks>
        public IDisposable ReadCollections(HashSet<string> read)
        {
            _collectionsRead = read;
            return new Handle(() => _collectionsRead = null);
        }

        /// <summary>Gets what a field's schema was written as, where a document wrote it.</summary>
        /// <param name="name">The field name.</param>
        /// <returns>The <c>op</c>, or <see langword="null"/> — a held rule set's field has none.</returns>
        /// <remarks>
        /// Kept because a parameter may take its schema from a field, and a host offering an
        /// editor for one reads <c>SchemaNode.Describe</c> against the operation the schema
        /// was written as. The node itself does not carry the name it was built from, and
        /// nothing else in the runtime needs it.
        /// </remarks>
        public string? OpOf(string name) => _ops.GetValueOrDefault(name);

        /// <summary>Gets how many references to a field have been resolved so far.</summary>
        /// <remarks>
        /// Every read of the state is resolved here while the rule set is built, so a count
        /// that moved across the building of an expression says that expression reads the
        /// state. That is how the compiler tells which documents a transition reads before
        /// anything is evaluated. See <see cref="ReadTrace"/>.
        /// </remarks>
        public int Resolved { get; private set; }

        /// <inheritdoc />
        public bool TryResolve(string path, [NotNullWhen(true)] out StatePath? resolved)
        {
            if (!_byName.TryGetValue(path, out resolved))
            {
                return TryResolveCollection(path, out resolved);
            }

            Resolved++;
            return true;
        }

        private bool TryResolveCollection(string alias, [NotNullWhen(true)] out StatePath? resolved)
        {
            int ordinal = _collections.IndexOf(alias);
            if (ordinal < 0)
            {
                resolved = null;
                return false;
            }

            if (_collectionsRead is null)
            {
                throw new RuleSetBuildException(
                    CurrentPath?.Invoke() ?? SourcePath.Root,
                    $"'{alias}' is every instance of a collection, and only a projection may read that. "
                    + "A guard or an effect reading it would read documents its transition does not write, "
                    + "which no store's version check would see move. "
                    + $"An input at one instance reads that one as '@{alias}'.");
            }

            if (!_collectionPaths.TryGetValue(alias, out resolved))
            {
                resolved = new StatePath(alias, _fields.Count + ordinal, CollectionSchema.Instance);
                _collectionPaths.Add(alias, resolved);
            }

            _collectionsRead.Add(alias);
            Resolved++;
            return true;
        }

        private sealed class Handle(Action stop) : IDisposable
        {
            public void Dispose() => stop();
        }
    }
}
