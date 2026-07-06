// Copyright (c) 2026 Reny
// Licensed under the MIT License.

using Rulealize.Definition;

namespace Rulealize.Execution
{
    /// <summary>
    /// Represents the input for rule execution, including the applicable input rule and the input values.
    /// </summary>
    public class Input
    {
        /// <summary>
        /// The input rule that is applicable for this input.
        /// </summary>
        public InputRule ApplicableInputRule { get; }

        /// <summary>
        /// The input values provided for the execution of the applicable input rule.
        /// </summary>
        public Dictionary<string, string> InputValues { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Input"/> class with the specified applicable input rule and input values.
        /// </summary>
        /// <param name="applicableInputRule"></param>
        /// <param name="inputValues"></param>
        public Input(InputRule applicableInputRule, Dictionary<string, string> inputValues)
        {
            ApplicableInputRule = applicableInputRule;
            InputValues = inputValues;
        }
    }
}
