// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize.Definition
{
    /// <summary>
    /// Represents a defined input rule
    /// </summary>
    public class InputRule : Rule
    {
        /// <summary>
        /// Input parameters defined for this rule
        /// </summary>
        public IEnumerable<InputParameter> Parameters { get; }

        /// <summary>
        /// The rule to which the input of this rule will be passed
        /// </summary>
        public ActionRule DestinationRule { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InputRule"/> class with the specified name, parameters, and destination rule.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="parameters"></param>
        /// <param name="destinationRule"></param>
        public InputRule(string name, IEnumerable<InputParameter> parameters, ActionRule destinationRule)
        {
            Name = name;
            Parameters = parameters;
            DestinationRule = destinationRule;
        }
    }
}
