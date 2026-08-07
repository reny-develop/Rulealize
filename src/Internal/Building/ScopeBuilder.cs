// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

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

            _declarations.Add(new Declaration(name, _depth, slot));
            return slot;
        }

        /// <inheritdoc />
        public bool TryResolve(string name, out LocalSlot slot)
        {
            for (int i = _declarations.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_declarations[i].Name, name, StringComparison.Ordinal))
                {
                    slot = _declarations[i].Slot;
                    return true;
                }
            }

            slot = default;
            return false;
        }

        private readonly record struct Declaration(string Name, int Depth, LocalSlot Slot);

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
