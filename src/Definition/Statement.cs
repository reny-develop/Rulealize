// Copyright (c) 2026 Reny
// Licensed under the MIT License.

namespace Rulealize.Definition
{
    /// <summary>
    /// Represents a statement in the rule definition, which can be either a function definition or a type definition.
    /// </summary>
    public readonly struct Statement
    {
        /// <summary>
        /// The underlying value of the statement, which can be either a FunctionDefinition or a TypeDefinition.
        /// </summary>
        private readonly object _value;

        /// <summary>
        /// Initializes a new instance of the <see cref="Statement"/> struct with a <see cref="FunctionDefinition"/>.
        /// </summary>
        /// <param name="value"></param>
        public Statement(FunctionDefinition value) => _value = value;

        /// <summary>
        /// Initializes a new instance of the <see cref="Statement"/> struct with a <see cref="TypeDefinition"/>.
        /// </summary>
        /// <param name="value"></param>
        public Statement(TypeDefinition value) => _value = value;

        /// <summary>
        /// Tries to get the underlying value as a <see cref="FunctionDefinition"/>.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetFunction(out FunctionDefinition? value)
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
        public bool TryGetType(out TypeDefinition? value)
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
        /// Matches the underlying value of the statement and invokes the corresponding function based on its type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="function"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public T Match<T>(
            Func<FunctionDefinition, T> function,
            Func<TypeDefinition, T> type)
        {
            return _value switch
            {
                FunctionDefinition x => function(x),
                TypeDefinition x => type(x),
                _ => throw new InvalidOperationException()
            };
        }

        /// <summary>
        /// Defines an implicit conversion from <see cref="FunctionDefinition"/> to <see cref="Statement"/>.
        /// </summary>
        /// <param name="value"></param>
        public static implicit operator Statement(FunctionDefinition value) => new(value);

        /// <summary>
        /// Defines an implicit conversion from <see cref="TypeDefinition"/> to <see cref="Statement"/>.
        /// </summary>
        /// <param name="value"></param>
        public static implicit operator Statement(TypeDefinition value) => new(value);
    }
}
