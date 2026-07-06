// Copyright (c) 2026 Reny
// Licensed under the MIT License.

namespace Rulealize.Definition
{
    /// <summary>
    /// Represents a rule that indicates an error condition in the rule evaluation process.
    /// </summary>
    public class ErrorRule : Rule
    {
        /// <summary>
        /// Gets the error code associated with this error rule.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the error message associated with this error rule.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorRule"/> class with the specified error code and message.
        /// </summary>
        /// <param name="code"></param>
        /// <param name="message"></param>
        public ErrorRule(string name, string code, string message)
        {
            Name = name;
            Code = code;
            Message = message;
        }
    }
}
