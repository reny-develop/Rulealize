// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using Rulealize.Abstraction.Node;

namespace Rulealize.Internal.Building
{
    /// <summary>Gathers everything wrong with a state document.</summary>
    /// <remarks>
    /// Every violation is collected rather than the first one thrown, because a document
    /// written by hand or produced by another system is usually wrong in more than one place
    /// and finding them one round trip at a time is miserable.
    /// </remarks>
    internal sealed class SchemaViolations
    {
        private readonly List<string> _messages = [];

        public bool Any => _messages.Count > 0;

        public ImmutableArray<string> Messages => [.. _messages];

        /// <summary>Creates a sink that files everything it receives under one field.</summary>
        /// <param name="field">The field name.</param>
        /// <returns>The sink.</returns>
        public ISchemaValidationSink For(string field) => new FieldSink(this, field);

        /// <summary>Records a problem that is not a schema violation as such.</summary>
        /// <param name="message">The message, already located.</param>
        public void Add(string message) => _messages.Add(message);

        private void Add(string path, string message) => _messages.Add($"{path}: {message}");

        private sealed class FieldSink(SchemaViolations owner, string field) : ISchemaValidationSink
        {
            private int _count;

            public bool HasViolations => _count > 0;

            public void Violation(string message)
            {
                _count++;
                owner.Add(field, message);
            }

            public void Violation(string relativePath, string message)
            {
                _count++;
                owner.Add($"{field}.{relativePath}", message);
            }
        }
    }
}
