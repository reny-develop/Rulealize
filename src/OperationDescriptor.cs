// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction.Plugin;

namespace Rulealize
{
    /// <summary>What an operation is, which is to say where it may be written.</summary>
    /// <remarks>
    /// <para>
    /// Where each kind may appear is fixed by the core and checked when a rule set is
    /// compiled, which is why an operation's kind is worth reporting: it is the whole of
    /// what the core knows about an operation beyond its name.
    /// </para>
    /// <para>
    /// Three of the four name the kind of node the operation builds.
    /// <see cref="Draw"/> does not — it builds an expression like anything else that
    /// produces a value, and what makes it its own kind is that it may be written in one
    /// place and not the others.
    /// </para>
    /// </remarks>
    public enum OperationKind
    {
        /// <summary>Produces a value. A guard, a domain, an argument, the value of an effect.</summary>
        Expression,

        /// <summary>Changes the state. An entry in an input's <c>effects</c>.</summary>
        Effect,

        /// <summary>Declares the shape of a state field. An entry in <c>state.schema</c>.</summary>
        Schema,

        /// <summary>Resolves something nobody chose. Only inside an input's <c>effects</c>.</summary>
        /// <remarks>
        /// A draw produces a value and is an expression by every other measure. It is kept
        /// out of guards, domains, the actor, <c>terminal</c> and definition bodies because
        /// those are evaluated while candidates are being sifted and while results are being
        /// memoized, and neither survives a value the state snapshot does not settle.
        /// </remarks>
        Draw
    }

    /// <summary>One operation a loaded plugin registered.</summary>
    /// <remarks>
    /// <para>
    /// A runtime is a vocabulary, and <see cref="RuleRuntime.Plugins"/> says which
    /// vocabularies were loaded without saying a word about what they contain. This is the
    /// rest of that answer: the names a rule set compiled against this runtime is allowed to
    /// write in an <c>op</c>.
    /// </para>
    /// <para>
    /// One name can be described twice. A name registered as both an expression and an
    /// effect is two operations that happen to be spelled alike — they are told apart by
    /// where they are written, never by the name — so each appears here with its own kind.
    /// </para>
    /// <para>
    /// What is deliberately not here is the factory. Which operations exist is a question
    /// about the vocabulary and is answerable at any time; building a node out of one is a
    /// question about a document, and it belongs to
    /// <see cref="RuleRuntime.CreateContext(string)"/>, where the node's placement can be
    /// checked.
    /// </para>
    /// </remarks>
    public sealed class OperationDescriptor
    {
        internal OperationDescriptor(string op, OperationKind kind, PluginManifest plugin)
        {
            Op = op;
            Kind = kind;
            Plugin = plugin;
        }

        /// <summary>Gets the qualified name, exactly as it is written in a document's <c>op</c>.</summary>
        /// <remarks>
        /// The plugin's namespace and the name it registered, joined by a dot — <c>grid.ray</c>.
        /// The namespace comes from the manifest rather than from the registering call, so
        /// this cannot name a plugin other than <see cref="Plugin"/>.
        /// </remarks>
        public string Op { get; }

        /// <summary>Gets which kind of node the operation builds.</summary>
        public OperationKind Kind { get; }

        /// <summary>Gets what the plugin that registered it declares about itself.</summary>
        public PluginManifest Plugin { get; }

        /// <inheritdoc />
        public override string ToString() => $"{Op} ({Kind})";
    }
}
