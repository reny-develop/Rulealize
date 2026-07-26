// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize.Plugin.Abstraction
{
    public abstract class PluginBase
    {
        public abstract string Name { get; }

        protected abstract void Register(IPluginRegistry registry);
    }
}
