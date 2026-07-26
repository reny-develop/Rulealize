// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Definition;
using Rulealize.Execution;

namespace Rulealize
{
    public class RuleContext
    {
        /// <summary>
        /// 
        /// </summary>
        private readonly RuleSet _ruleset;

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleContext"/> class.
        /// </summary>
        /// <param name="ruleset"></param>
        public RuleContext(RuleSet ruleset)
        {
            _ruleset = ruleset;
        }

        /// <summary>
        /// Applies the ruleset to the state asynchronously.
        /// </summary>
        /// <param name="state">State JSON</param>
        /// <param name="input">Input JSON</param>
        /// <returns></returns>
        public string ApplyToStateAsync(string state, string input)
        {
            // todo: JSONとしてStateとInputを受け取り、RuleSetに沿ってInputを元にStateを更新する形でルール適用
            // State JSON を State型に変換
            // Input JSON を Input型に変換
            // ApplyToStateAsync(State state, Input input)実行
            // ApplyToStateAsync(State state, Input input)の結果をJSONに変換して返す
            return "";
        }

        /// <summary>
        /// Applies the ruleset to the state asynchronously.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        public State ApplyToStateAsync(State state, Input input)
        {
            // todo
            return state;
        }
    }
}
