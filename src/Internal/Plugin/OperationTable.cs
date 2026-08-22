// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Plugin;

namespace Rulealize.Internal.Plugin
{
    /// <summary>Everything the loaded plugins between them provide.</summary>
    /// <remarks>
    /// <para>
    /// The core's entire knowledge of any plugin lives in these four dictionaries: an
    /// operation name to a factory, and a reserved character to an expander. It never sees a
    /// plugin's types, and it does not know what any operation does.
    /// </para>
    /// <para>
    /// A name may be registered as both an expression and an effect. The two are told apart
    /// by where they appear, so keeping them in separate tables is what makes
    /// <c>state.set</c> and a hypothetical expression of the same name able to coexist.
    /// </para>
    /// </remarks>
    internal sealed class OperationTable
    {
        private readonly Dictionary<string, ExpressionNodeFactory> _expressions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, EffectNodeFactory> _effects = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SchemaNodeFactory> _schemas = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DrawNodeFactory> _draws = new(StringComparer.Ordinal);
        private readonly Dictionary<char, ISugarExpander> _sugar = [];
        private readonly Dictionary<string, PluginManifest> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PluginManifest> _byNamespace = new(StringComparer.Ordinal);
        private readonly Dictionary<char, PluginManifest> _byPrefix = [];
        private readonly List<PluginManifest> _manifests = [];
        private readonly List<OperationDescriptor> _operations = [];

        /// <summary>Gets the manifests of every loaded plugin, in load order.</summary>
        public ImmutableArray<PluginManifest> Manifests => [.. _manifests];

        /// <summary>Gets a description of every operation registered, in registration order.</summary>
        /// <remarks>
        /// Kept alongside the four dictionaries rather than reconstructed from them, because
        /// which plugin registered a name is not recoverable from a factory, and a name
        /// registered as two kinds has to appear twice.
        /// </remarks>
        public ImmutableArray<OperationDescriptor> Operations => [.. _operations];

        /// <summary>Records a plugin's claims and rejects it when they clash with another's.</summary>
        /// <param name="manifest">The plugin's manifest.</param>
        /// <exception cref="PluginLoadException">The identifier, namespace or prefix is taken.</exception>
        public void Claim(PluginManifest manifest)
        {
            if (_byId.TryGetValue(manifest.Id, out PluginManifest? sameId))
            {
                throw new PluginLoadException(
                    $"'{manifest.Id}' is already loaded as {sameId}.");
            }

            if (_byNamespace.TryGetValue(manifest.Namespace, out PluginManifest? sameNamespace))
            {
                throw new PluginLoadException(
                    $"{manifest.Id} claims the namespace '{manifest.Namespace}', which {sameNamespace.Id} already provides.");
            }

            if (manifest.ReservedPrefix is char prefix
                && _byPrefix.TryGetValue(prefix, out PluginManifest? samePrefix))
            {
                throw new PluginLoadException(
                    $"{manifest.Id} reserves '{prefix}' for its shorthand, which {samePrefix.Id} already reserves.");
            }

            _byId.Add(manifest.Id, manifest);
            _byNamespace.Add(manifest.Namespace, manifest);
            if (manifest.ReservedPrefix is char reserved)
            {
                _byPrefix.Add(reserved, manifest);
            }

            _manifests.Add(manifest);
        }

        /// <summary>Finds a loaded plugin by identifier.</summary>
        /// <param name="id">The plugin identifier as written in a rule set's <c>requires</c>.</param>
        /// <param name="manifest">Receives the manifest when the plugin is loaded.</param>
        /// <returns><see langword="true"/> when the plugin is loaded.</returns>
        public bool TryGetPlugin(string id, out PluginManifest? manifest) => _byId.TryGetValue(id, out manifest);

        /// <summary>Registers an expression operation.</summary>
        /// <param name="manifest">The registering plugin.</param>
        /// <param name="name">The unqualified name.</param>
        /// <param name="factory">The factory.</param>
        public void AddExpression(PluginManifest manifest, string name, ExpressionNodeFactory factory) =>
            Add(_expressions, manifest, name, factory, OperationKind.Expression);

        /// <summary>Registers an effect operation.</summary>
        /// <param name="manifest">The registering plugin.</param>
        /// <param name="name">The unqualified name.</param>
        /// <param name="factory">The factory.</param>
        public void AddEffect(PluginManifest manifest, string name, EffectNodeFactory factory) =>
            Add(_effects, manifest, name, factory, OperationKind.Effect);

        /// <summary>Registers a schema operation.</summary>
        /// <param name="manifest">The registering plugin.</param>
        /// <param name="name">The unqualified name.</param>
        /// <param name="factory">The factory.</param>
        public void AddSchema(PluginManifest manifest, string name, SchemaNodeFactory factory) =>
            Add(_schemas, manifest, name, factory, OperationKind.Schema);

        /// <summary>Registers a draw operation.</summary>
        /// <param name="manifest">The registering plugin.</param>
        /// <param name="name">The unqualified name.</param>
        /// <param name="factory">The factory.</param>
        /// <remarks>
        /// A table of its own even though what it builds is an expression node, because that
        /// is the whole of how the builder knows to refuse it outside an input's effects.
        /// </remarks>
        public void AddDraw(PluginManifest manifest, string name, DrawNodeFactory factory) =>
            Add(_draws, manifest, name, factory, OperationKind.Draw);

        /// <summary>Registers a shorthand expander for the character a plugin reserved.</summary>
        /// <param name="manifest">The registering plugin.</param>
        /// <param name="expander">The expander.</param>
        public void AddSugar(PluginManifest manifest, ISugarExpander expander)
        {
            if (manifest.ReservedPrefix is not char prefix)
            {
                throw new InvalidOperationException(
                    $"{manifest.Id} did not declare a reserved prefix, so it cannot register a shorthand.");
            }

            if (_sugar.ContainsKey(prefix))
            {
                throw new InvalidOperationException($"{manifest.Id} has already registered a shorthand for '{prefix}'.");
            }

            _sugar.Add(prefix, expander);
        }

        /// <summary>Looks up an expression operation.</summary>
        /// <param name="op">The qualified operation name.</param>
        /// <param name="factory">Receives the factory.</param>
        /// <returns><see langword="true"/> when the operation is an expression.</returns>
        public bool TryGetExpression(string op, out ExpressionNodeFactory? factory) =>
            _expressions.TryGetValue(op, out factory);

        /// <summary>Looks up an effect operation.</summary>
        /// <param name="op">The qualified operation name.</param>
        /// <param name="factory">Receives the factory.</param>
        /// <returns><see langword="true"/> when the operation is an effect.</returns>
        public bool TryGetEffect(string op, out EffectNodeFactory? factory) => _effects.TryGetValue(op, out factory);

        /// <summary>Looks up a schema operation.</summary>
        /// <param name="op">The qualified operation name.</param>
        /// <param name="factory">Receives the factory.</param>
        /// <returns><see langword="true"/> when the operation is a schema.</returns>
        public bool TryGetSchema(string op, out SchemaNodeFactory? factory) => _schemas.TryGetValue(op, out factory);

        /// <summary>Looks up a draw operation.</summary>
        /// <param name="op">The qualified operation name.</param>
        /// <param name="factory">Receives the factory.</param>
        /// <returns><see langword="true"/> when the operation is a draw.</returns>
        public bool TryGetDraw(string op, out DrawNodeFactory? factory) => _draws.TryGetValue(op, out factory);

        /// <summary>Looks up the expander for a shorthand character.</summary>
        /// <param name="prefix">The leading character of a string literal.</param>
        /// <param name="expander">Receives the expander.</param>
        /// <returns><see langword="true"/> when some plugin reserved the character.</returns>
        public bool TryGetSugar(char prefix, out ISugarExpander? expander) => _sugar.TryGetValue(prefix, out expander);

        /// <summary>
        /// Says what an operation is, for the message that explains why it cannot appear
        /// where it was written.
        /// </summary>
        /// <param name="op">The qualified operation name.</param>
        /// <returns>The kind, or <see langword="null"/> when nothing registered the name.</returns>
        public string? DescribeKind(string op)
        {
            if (_expressions.ContainsKey(op))
            {
                return "an expression";
            }

            if (_effects.ContainsKey(op))
            {
                return "an effect";
            }

            if (_schemas.ContainsKey(op))
            {
                return "a schema";
            }

            return _draws.ContainsKey(op) ? "a draw" : null;
        }

        private static string Describe(OperationKind kind) => kind switch
        {
            OperationKind.Expression => "expression",
            OperationKind.Effect => "effect",
            OperationKind.Draw => "draw",
            _ => "schema"
        };

        private void Add<TFactory>(
            Dictionary<string, TFactory> target,
            PluginManifest manifest,
            string name,
            TFactory factory,
            OperationKind kind)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(factory);

            string qualified = $"{manifest.Namespace}.{name}";
            if (!target.TryAdd(qualified, factory))
            {
                throw new ArgumentException(
                    $"{manifest.Id} has already registered the {Describe(kind)} '{qualified}'.",
                    nameof(name));
            }

            _operations.Add(new OperationDescriptor(qualified, kind, manifest));
        }
    }
}
