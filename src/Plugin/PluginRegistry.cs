// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Plugin.Abstraction;

namespace Rulealize.Plugin
{
    internal sealed class PluginRegistry : IPluginRegistry
    {
        private readonly PluginBase _plugin;

        private readonly Dictionary<string, TypeDefinitionBase> _types = new();

        private readonly Dictionary<string, FunctionDefinitionBase> _functions = new();

        public PluginRegistry(PluginBase plugin)
        {
            _plugin = plugin;
        }

        public IReadOnlyDictionary<string, TypeDefinitionBase> Types => _types;

        public IReadOnlyDictionary<string, FunctionDefinitionBase> Functions => _functions;

        public void Type<T>()
            where T : TypeDefinitionBase, new()
        {
            var definition = new T();

            var symbol = $"{_plugin.Name}.{definition.Name}";

            if (!_types.TryAdd(symbol, definition))
            {
                throw new InvalidOperationException(
                    $"Type '{symbol}' is already registered.");
            }
        }

        public void Function<T>()
            where T : FunctionDefinitionBase, new()
        {
            var definition = new T();

            var symbol = $"{_plugin.Name}.{definition.Name}";

            if (!_functions.TryAdd(symbol, definition))
            {
                throw new InvalidOperationException(
                    $"Function '{symbol}' is already registered.");
            }
        }
    }
}
