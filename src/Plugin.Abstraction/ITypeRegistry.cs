// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text;

namespace Rulealize.Plugin.Abstraction
{
    public interface ITypeRegistry
    {
        void Property<T>()
        where T : PropertyDefinitionBase, new();

        void Method<T>()
            where T : MethodDefinitionBase, new();
    }
}
