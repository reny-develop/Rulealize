// Copyright (c) 2026 Reny
// Licensed under the MIT License.

using System.Text.Json;

namespace Rulealize.Definition
{
    public class FunctionDefinition
    {
        /// <summary>
        /// Gets or sets the name of the function.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets or sets the function delegate that takes a JsonElement as input and returns a Task of an object.
        /// </summary>
        public Func<JsonElement, Task<object?>> Function { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FunctionDefinition"/> class with the specified name and function delegate.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="function"></param>
        public FunctionDefinition(
            string name,
            Func<JsonElement, Task<object?>> function)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(function);

            Name = name;
            Function = function;
        }
    }
}
