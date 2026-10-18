// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize.Internal.Building
{
    /// <summary>Notices whether the expressions built while it watches read the rule set's own state.</summary>
    /// <remarks>
    /// <para>
    /// A read is a field reference resolved against <see cref="StateSchema"/>, or a call to a
    /// definition whose body — or a body it calls — makes one. Both are settled while the
    /// document is compiled, so which documents a transition reads is a fact about the
    /// document and never something found out by evaluating it.
    /// </para>
    /// <para>
    /// What it is for: a guard may read only documents its transition writes, because that is
    /// what lets a store check versions on what it writes and nothing else. A rule set that
    /// keeps instances is the one case where a transition can leave its holder's document
    /// unwritten, and so the one case this is asked about.
    /// </para>
    /// </remarks>
    internal sealed class ReadTrace(StateSchema schema, DefinitionTable definitions)
    {
        private readonly HashSet<int> _called = [];
        private bool _resolved;

        /// <summary>Gets whether anything built while watching reads the state.</summary>
        /// <remarks>Only meaningful once every definition body is built.</remarks>
        public bool ReadsState => _resolved || definitions.AnyReadsState(_called);

        /// <summary>Watches until the returned handle is disposed.</summary>
        /// <returns>The handle.</returns>
        public IDisposable Watch()
        {
            int before = schema.Resolved;
            HashSet<int>? outer = definitions.Traced;
            definitions.Traced = _called;
            return new Handle(() =>
            {
                _resolved |= schema.Resolved > before;
                definitions.Traced = outer;
            });
        }

        private sealed class Handle(Action stop) : IDisposable
        {
            public void Dispose() => stop();
        }
    }
}
