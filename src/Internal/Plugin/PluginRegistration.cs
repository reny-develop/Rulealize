// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Plugin;

namespace Rulealize.Internal.Plugin
{
    /// <summary>What a plugin is handed while it registers, scoped to that one plugin.</summary>
    /// <remarks>
    /// The namespace comes from the manifest rather than from the call, so a plugin cannot
    /// register into another's namespace even by accident. Same for the shorthand
    /// character.
    /// </remarks>
    internal sealed class PluginRegistration(OperationTable table, PluginManifest manifest) : IPluginRegistry
    {
        public PluginManifest Manifest => manifest;

        public void AddExpression(string name, ExpressionNodeFactory factory) =>
            table.AddExpression(manifest, name, factory);

        public void AddEffect(string name, EffectNodeFactory factory) => table.AddEffect(manifest, name, factory);

        public void AddSchema(string name, SchemaNodeFactory factory) => table.AddSchema(manifest, name, factory);

        public void AddSugar(ISugarExpander expander)
        {
            ArgumentNullException.ThrowIfNull(expander);
            table.AddSugar(manifest, expander);
        }
    }
}
