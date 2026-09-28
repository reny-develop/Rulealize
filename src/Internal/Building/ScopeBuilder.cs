// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Building;

namespace Rulealize.Internal.Building
{
    /// <summary>Tracks which local names are in scope, and hands out the slots they read from.</summary>
    /// <remarks>
    /// <para>
    /// This is the meeting point for plugins that never reference one another. A sequence
    /// operation declares the name its <c>as</c> introduces; the binding plugin resolves the
    /// name a <c>@</c> shorthand mentions. Neither knows the other exists, and what connects
    /// them is that both reach the runtime through the abstraction.
    /// </para>
    /// <para>
    /// Slots are numbered within a frame and reused between sibling scopes. Reuse is safe
    /// because an evaluation context is copied rather than amended when a value is bound, so
    /// a lazy sequence holding an old context keeps the value it captured even after the
    /// slot has been given to something else.
    /// </para>
    /// </remarks>
    internal sealed class ScopeBuilder(Func<SourcePath> currentPath) : IScopeBuilder
    {
        private readonly List<Declaration> _declarations = [];
        private int _depth;
        private int _next;
        private int _high;
        private bool _openBarred;
        private List<string>? _openRead;

        /// <summary>Gets the number of slots the frame being built has needed so far.</summary>
        public int FrameSize => _high;

        /// <summary>Starts a new frame, discarding everything the previous one declared.</summary>
        /// <remarks>
        /// Frames are per definition body, per input, and per section, so that slot numbers
        /// from one never collide with another's.
        /// </remarks>
        public void BeginFrame()
        {
            _declarations.Clear();
            _depth = 0;
            _next = 0;
            _high = 0;
            _openBarred = false;
            _openRead = null;
        }

        /// <inheritdoc />
        public IDisposable BeginScope()
        {
            ScopeHandle handle = new(this, _declarations.Count, _next);
            _depth++;
            return handle;
        }

        /// <inheritdoc />
        public LocalSlot Declare(string name)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);

            for (int i = _declarations.Count - 1; i >= 0; i--)
            {
                if (_declarations[i].Depth != _depth)
                {
                    break;
                }

                if (string.Equals(_declarations[i].Name, name, StringComparison.Ordinal))
                {
                    throw new RuleSetBuildException(
                        currentPath(),
                        $"'{name}' is already declared in this scope. Shadowing an outer binding is allowed; "
                        + "declaring one name twice in the same scope is not.");
                }
            }

            LocalSlot slot = new(_next++);
            if (_next > _high)
            {
                _high = _next;
            }

            _declarations.Add(new Declaration(name, _depth, slot, false));
            return slot;
        }

        /// <summary>Declares a parameter the input leaves open, which has no value until one arrives.</summary>
        /// <param name="name">The parameter name, as written in the document.</param>
        /// <returns>The slot to read it from at evaluation time.</returns>
        /// <remarks>
        /// <para>
        /// The slot is handed out exactly as <see cref="Declare"/> hands one out, so numbering
        /// does not depend on which parameters are open and every position that may read the
        /// name reads it the same way. What differs is only that reading it is refused while
        /// <see cref="BarOpen"/> is in force.
        /// </para>
        /// <para>
        /// Only the compiler calls this. A plugin declaring a binding is declaring one it is
        /// about to give a value to, which is never this.
        /// </para>
        /// </remarks>
        public LocalSlot DeclareOpen(string name)
        {
            LocalSlot slot = Declare(name);
            _declarations[^1] = _declarations[^1] with { Open = true };
            return slot;
        }

        /// <summary>Refuses, until disposed, any reference to a parameter left open.</summary>
        /// <returns>A handle that lifts the refusal when disposed.</returns>
        /// <remarks>
        /// <para>
        /// In force while the positions evaluated as candidates are formed are built —
        /// <c>actor</c>, <c>when</c>, and the arguments of <c>fires</c>. An open parameter has
        /// no value then: <c>GetValidInputs</c> offers the input with the argument still to
        /// come. A guard reading it could only be answered by guessing, and the answer would
        /// be published as a legal move.
        /// </para>
        /// <para>
        /// This is the same reasoning that keeps a draw out of a guard and out of a domain,
        /// and it lands in the same place: a position offered and then refused is worse than
        /// one never offered.
        /// </para>
        /// </remarks>
        public IDisposable BarOpen()
        {
            _openBarred = true;
            return new Bar(this);
        }

        /// <summary>Records which open parameters are read, until the trace is disposed.</summary>
        /// <returns>The trace, which names them.</returns>
        /// <remarks>
        /// <para>
        /// What a <c>validate</c> clause reads is the clause's subject. A clause naming one
        /// open parameter is about that parameter and its refusal can be shown against that
        /// field; a clause naming several is about the form. A clause naming none is not about
        /// an argument at all — it could have been decided before the input was offered — and
        /// is refused, because letting it through would move guards out of <c>when</c> and
        /// quietly make <c>GetValidInputs</c> a worse answer.
        /// </para>
        /// <para>
        /// Recorded here rather than by walking the built node because the node belongs to
        /// whichever vocabulary built it, and the runtime does not read inside one. What it
        /// does own is the resolution, which every reader has to come through.
        /// </para>
        /// </remarks>
        public OpenTrace TraceOpen()
        {
            _openRead = [];
            return new OpenTrace(this);
        }

        /// <inheritdoc />
        /// <exception cref="RuleSetBuildException">
        /// The name is a parameter left open and this position is evaluated before its value
        /// arrives. See <see cref="BarOpen"/>.
        /// </exception>
        public bool TryResolve(string name, out LocalSlot slot)
        {
            for (int i = _declarations.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(_declarations[i].Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (_declarations[i].Open && _openRead is not null
                    && !_openRead.Contains(_declarations[i].Name, StringComparer.Ordinal))
                {
                    _openRead.Add(_declarations[i].Name);
                }

                if (_declarations[i].Open && _openBarred)
                {
                    throw new RuleSetBuildException(
                        currentPath(),
                        $"'{name}' is a parameter this input leaves open, and there is no value for it "
                        + "here: this position is evaluated while candidates are being formed, before the "
                        + "argument arrives. A rule about an open parameter's value belongs in 'validate'.");
                }

                slot = _declarations[i].Slot;
                return true;
            }

            slot = default;
            return false;
        }

        private readonly record struct Declaration(string Name, int Depth, LocalSlot Slot, bool Open);

        /// <summary>The open parameters read while it was live.</summary>
        internal sealed class OpenTrace(ScopeBuilder owner) : IDisposable
        {
            private ImmutableArray<string> _names;
            private bool _closed;

            /// <summary>Gets the names, in the order they were first read.</summary>
            public ImmutableArray<string> Names => _closed ? _names : [.. owner._openRead ?? []];

            public void Dispose()
            {
                if (_closed)
                {
                    return;
                }

                _closed = true;
                _names = [.. owner._openRead ?? []];
                owner._openRead = null;
            }
        }

        private sealed class Bar(ScopeBuilder owner) : IDisposable
        {
            private bool _lifted;

            public void Dispose()
            {
                if (_lifted)
                {
                    return;
                }

                _lifted = true;
                owner._openBarred = false;
            }
        }

        private sealed class ScopeHandle(ScopeBuilder owner, int declarationCount, int nextSlot) : IDisposable
        {
            private bool _closed;

            public void Dispose()
            {
                if (_closed)
                {
                    return;
                }

                _closed = true;
                owner._declarations.RemoveRange(declarationCount, owner._declarations.Count - declarationCount);
                owner._next = nextSlot;
                owner._depth--;
            }
        }
    }
}
