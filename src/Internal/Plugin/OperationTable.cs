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
    /// operation name to a factory, and a reserved character to the expanders registered
    /// against it. It never sees a plugin's types, and it does not know what any operation
    /// does.
    /// </para>
    /// <para>
    /// More than one plugin may reserve the same character. Which of them a string literal
    /// meant is settled where the literal is read and not here, so a folder holding two of
    /// them still loads.
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
        private readonly Dictionary<char, List<SugarClaim>> _sugar = [];
        private readonly Dictionary<string, PluginManifest> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PluginManifest> _byNamespace = new(StringComparer.Ordinal);
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
        /// <exception cref="PluginLoadException">The identifier or namespace is taken.</exception>
        /// <remarks>
        /// A shorthand character is not among the things that can be taken. Two plugins
        /// reserving one character load together, and a rule set that writes the bare form
        /// while requiring both is asked which it meant — requiring one of them is what
        /// settles it, and requiring neither is refused for the same reason any undeclared
        /// vocabulary is.
        /// </remarks>
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

            _byId.Add(manifest.Id, manifest);
            _byNamespace.Add(manifest.Namespace, manifest);

            _manifests.Add(manifest);
        }

        /// <summary>Finds a loaded plugin by identifier.</summary>
        /// <param name="id">The plugin identifier as written in a rule set's <c>requires</c>.</param>
        /// <param name="manifest">Receives the manifest when the plugin is loaded.</param>
        /// <returns><see langword="true"/> when the plugin is loaded.</returns>
        public bool TryGetPlugin(string id, out PluginManifest? manifest) => _byId.TryGetValue(id, out manifest);

        /// <summary>Finds the loaded plugin that claimed a namespace.</summary>
        /// <param name="namespace">The part of an operation name before its first dot.</param>
        /// <param name="manifest">Receives the manifest when some plugin claimed it.</param>
        /// <returns><see langword="true"/> when a loaded plugin provides that namespace.</returns>
        /// <remarks>
        /// What this answers is <em>whose</em> an operation is, which is what lets a rule set
        /// using a vocabulary it did not name in <c>requires</c> be told which one it reached
        /// for rather than that the name is unknown.
        /// </remarks>
        public bool TryGetProvider(string @namespace, out PluginManifest? manifest) =>
            _byNamespace.TryGetValue(@namespace, out manifest);

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

            List<SugarClaim> claims = Expanders(prefix);
            if (claims.Any(claim => string.Equals(claim.Namespace, manifest.Namespace, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"{manifest.Id} has already registered a shorthand for '{prefix}'.");
            }

            claims.Add(new SugarClaim(manifest.Namespace, expander));
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

        /// <summary>Says whether any plugin reserved a character.</summary>
        /// <param name="prefix">The leading character of a string literal.</param>
        /// <returns><see langword="true"/> when at least one plugin reserved it.</returns>
        /// <remarks>
        /// Asked before anything else about a string literal, because a character nobody
        /// reserved makes the whole of it ordinary text and nothing further is read into it.
        /// </remarks>
        public bool IsReserved(char prefix) => _sugar.ContainsKey(prefix);

        /// <summary>Looks up the expander a namespace registered against a character.</summary>
        /// <param name="prefix">The leading character of a string literal.</param>
        /// <param name="namespace">The namespace written before the colon.</param>
        /// <param name="expander">Receives the expander.</param>
        /// <returns><see langword="true"/> when that namespace reserved that character.</returns>
        public bool TryGetSugar(char prefix, string @namespace, out ISugarExpander? expander)
        {
            expander = _sugar.TryGetValue(prefix, out List<SugarClaim>? claims)
                ? claims.FirstOrDefault(claim => string.Equals(claim.Namespace, @namespace, StringComparison.Ordinal))
                    .Expander
                : null;

            return expander is not null;
        }

        /// <summary>Lists the namespaces that reserved a character, in load order.</summary>
        /// <param name="prefix">The leading character of a string literal.</param>
        /// <returns>The namespaces, or empty when nobody reserved it.</returns>
        /// <remarks>
        /// Which of them a rule set meant is settled by the builder against the document's
        /// <c>requires</c>, so this reports the claims and decides nothing.
        /// </remarks>
        public ImmutableArray<string> Claimants(char prefix) =>
            _sugar.TryGetValue(prefix, out List<SugarClaim>? claims)
                ? [.. claims.Select(static claim => claim.Namespace)]
                : [];

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

        private List<SugarClaim> Expanders(char prefix)
        {
            if (!_sugar.TryGetValue(prefix, out List<SugarClaim>? claims))
            {
                claims = [];
                _sugar.Add(prefix, claims);
            }

            return claims;
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

        /// <summary>One plugin's shorthand for a character, kept under the namespace that names it.</summary>
        private readonly record struct SugarClaim(string Namespace, ISugarExpander Expander);
    }
}
