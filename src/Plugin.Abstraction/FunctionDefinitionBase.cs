// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;

namespace Rulealize.Plugin.Abstraction
{
    public abstract class FunctionDefinitionBase
    {
        /// <summary>
        /// Gets or sets the name of the function.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Executes the function asynchronously with the specified JSON element as input.
        /// </summary>
        /// <param name="jsonElement"></param>
        /// <returns></returns>
        public abstract ValueTask<object?> ExecuteAsync(JsonElement jsonElement);
    }
}
