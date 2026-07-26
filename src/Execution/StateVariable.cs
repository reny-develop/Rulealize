// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Plugin.Abstraction;
using System.Text.Json;

namespace Rulealize.Execution
{
    public class StateVariable
    {
        public TypeDefinitionBase Type { get; }
        public IReadOnlyDictionary<string, JsonElement> Values { get; }
        public StateVariable(
            TypeDefinitionBase type,
            IReadOnlyDictionary<string, JsonElement> values)
        {
            Type = type;
            Values = values;
        }

        // todo: string型でTypeが定義してるプロパティ名を引数にして値を取得できるメソッドを実装すべき
        // 実態はType側だけど、そのラッパーとしてこの関数がある
        //public T GetValue<T>(string propertyName)
        //{
        //    return Type.GetValue<T>(this, propertyName);
        //}

        // todo: string型でTypeが定義してるプロパティ名を引数にして値を更新できるメソッドを実装すべき
        //public void UpdateValue<T>(string propertyName, JsonElement value)
        //{
        //    Type.UpdateValue<T>(this, propertyName, value);
        //}
    }
}
