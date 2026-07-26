// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize.Plugin.Abstraction
{
    public abstract class TypeDefinitionBase
    {
        public abstract string Name { get; }

        protected abstract void TypeRegister(ITypeRegistry registry);

        public virtual IEnumerable<string> PublishPropertySymbols
        => Enumerable.Empty<string>();

        public virtual IEnumerable<FunctionDefinitionBase> PublishFunctions
        => Enumerable.Empty<FunctionDefinitionBase>();

        public virtual ValueTask<object?> GetPropertyValueAsync(string propertySymbol)
        => ValueTask.FromResult<object?>(null);
    }
}
