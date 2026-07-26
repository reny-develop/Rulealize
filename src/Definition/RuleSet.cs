// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize.Definition
{
    /// <summary>
    /// Represents a set of defined rules
    /// </summary>
    public class RuleSet
    {
        /// <summary>
        /// The collection of rules defined in this rule set
        /// </summary>
        public IEnumerable<Rule> Rules { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleSet"/> class with the specified collection of rules.
        /// </summary>
        /// <param name="rules"></param>
        public RuleSet(IEnumerable<Rule> rules)
        {
            Rules = rules;
        }

        ///// <summary>
        ///// Initializes a new instance of the <see cref="RuleSet"/> class with the specified JSON string representing the rules.
        ///// </summary>
        ///// <param name="json"></param>
        //public RuleSet(string json)
        //{
        //    // todo: Implement JSON deserialization
        //}
    }
}
