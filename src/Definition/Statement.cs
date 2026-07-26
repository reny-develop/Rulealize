// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Plugin.Abstraction;

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
        /// Initializes a new instance of the <see cref="Statement"/> struct with a <see cref="FunctionDefinitionBase"/>.
        /// </summary>
        /// <param name="value"></param>
        public Statement(FunctionDefinitionBase value) => _value = value;

        /// <summary>
        /// Initializes a new instance of the <see cref="Statement"/> struct with a <see cref="TypeDefinitionBase"/>.
        /// </summary>
        /// <param name="value"></param>
        public Statement(TypeDefinitionBase value) => _value = value;

        /// <summary>
        /// Tries to get the underlying value as a <see cref="FunctionDefinitionBase"/>.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetFunction(out FunctionDefinitionBase? value)
        {
            if (_value is FunctionDefinitionBase f)
            {
                value = f;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Tries to get the underlying value as a <see cref="TypeDefinitionBase"/>.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetType(out TypeDefinitionBase? value)
        {
            if (_value is TypeDefinitionBase t)
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
            Func<FunctionDefinitionBase, T> function,
            Func<TypeDefinitionBase, T> type)
        {
            return _value switch
            {
                FunctionDefinitionBase x => function(x),
                TypeDefinitionBase x => type(x),
                _ => throw new InvalidOperationException()
            };
        }

        /// <summary>
        /// Defines an implicit conversion from <see cref="FunctionDefinitionBase"/> to <see cref="Statement"/>.
        /// </summary>
        /// <param name="value"></param>
        public static implicit operator Statement(FunctionDefinitionBase value) => new(value);

        /// <summary>
        /// Defines an implicit conversion from <see cref="TypeDefinitionBase"/> to <see cref="Statement"/>.
        /// </summary>
        /// <param name="value"></param>
        public static implicit operator Statement(TypeDefinitionBase value) => new(value);
    }
}
