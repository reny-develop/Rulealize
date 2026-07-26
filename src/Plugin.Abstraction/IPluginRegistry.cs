// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize.Plugin.Abstraction
{
    public interface IPluginRegistry
    {
        void Type<T>()
        where T : TypeDefinitionBase, new();

        void Function<T>()
            where T : FunctionDefinitionBase, new();
    }
}
