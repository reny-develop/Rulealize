// Copyright (c) 2026 Reny
// Licensed under the MIT License.

namespace Rulealize.Definition
{
    /// <summary>
    /// Represents a defined action rule, which consists of a name and a collection of statements to be executed when the rule is triggered.
    /// </summary>
    public class ActionRule : Rule
    {
        /// <summary>
        /// Gets the collection of statements that define the actions to be performed when this rule is triggered.
        /// </summary>
        public IEnumerable<Statement> Statements { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionRule"/> class with the specified name and collection of statements.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="statements"></param>
        public ActionRule(string name, IEnumerable<Statement> statements)
        {
            Name = name;
            Statements = statements;
        }
    }
}
