// Copyright (c) 2026 Reny
// Licensed under the MIT License.

namespace Rulealize.Definition
{
    /// <summary>
    /// Represents an input parameter that can hold a value of type FunctionDefinition, TypeDefinition, or string.
    /// </summary>
    public readonly struct InputParameter
    {
        /// <summary>
        /// Parameter name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The underlying value of the input parameter, which can be a FunctionDefinition, a TypeDefinition, or a string.
        /// </summary>
        private readonly object _value;

        /// <summary>
        /// Initializes a new instance of the <see cref="InputParameter"/> class with a <see cref="FunctionDefinition"/>.
        /// </summary>
        /// <param name="value"></param>
        public InputParameter(string name, FunctionDefinition value)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InputParameter"/> class with a <see cref="TypeDefinition"/>.
        /// </summary>
        /// <param name="value"></param>
        public InputParameter(string name, TypeDefinition value)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InputParameter"/> class with a string.
        /// </summary>
        public InputParameter(string name, string value)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Tries to get the underlying value as a <see cref="FunctionDefinition"/>.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetFunctionDefinitionValue(out FunctionDefinition? value)
        {
            if (_value is FunctionDefinition f)
            {
                value = f;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Tries to get the underlying value as a <see cref="TypeDefinition"/>.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetTypeDefinitionValue(out TypeDefinition? value)
        {
            if (_value is TypeDefinition t)
            {
                value = t;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Tries to get the underlying value as a string.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetStringValue(out string? value)
        {
            if (_value is string s)
            {
                value = s;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Matches the underlying value of the input parameter and invokes the corresponding function based on its type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="function"></param>
        /// <param name="type"></param>
        /// <param name="text"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public T MatchValueType<T>(
            Func<FunctionDefinition, T> function,
            Func<TypeDefinition, T> type,
            Func<string, T> text)
        {
            return _value switch
            {
                FunctionDefinition x => function(x),
                TypeDefinition x => type(x),
                string x => text(x),
                _ => throw new InvalidOperationException()
            };
        }
    }
}
