// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Node;
using Rulealize.Abstraction.Value;

namespace Rulealize.Internal.Building
{
    /// <summary>What a collection resolves to: every instance, which nothing writes.</summary>
    /// <remarks>
    /// A <see cref="StatePath"/> carries a schema, and a collection's has nothing to check,
    /// because no document contains the sequence it reads as. A projection reads it and nothing
    /// writes it — an effect cannot even resolve it — so none of this is ever asked except
    /// <see cref="Validate"/>, which refuses whatever it is given rather than let a write pass.
    /// </remarks>
    internal sealed class CollectionSchema : SchemaNode
    {
        private CollectionSchema()
        {
        }

        public static CollectionSchema Instance { get; } = new();

        public override bool IsNullable => false;

        public override void Validate(RuleValue value, ISchemaValidationSink sink)
        {
            ArgumentNullException.ThrowIfNull(sink);
            sink.Violation("Every instance of a collection is read, never written.");
        }

        public override RuleValue ReadJson(JsonElement element, ISchemaValidationSink sink) =>
            throw new InvalidOperationException("A collection is not part of any document.");

        public override void WriteJson(Utf8JsonWriter writer, RuleValue value) =>
            throw new InvalidOperationException("A collection is not part of any document.");
    }
}
