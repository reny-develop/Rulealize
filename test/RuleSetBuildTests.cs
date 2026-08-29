// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction;

namespace Rulealize.Tests
{
    /// <summary>What has to be settled before a rule set is allowed to run.</summary>
    /// <remarks>
    /// Everything decidable from the document is decided in <c>CreateContext</c>, and the
    /// reason is <c>GetValidInputs</c>: it evaluates a guard against every candidate in a
    /// domain, so a fault that first appears on the forty-first candidate is a fault that
    /// reaches production. Each of these is a fault that would otherwise do exactly that.
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class RuleSetBuildTests(StandardRuntime standard)
    {
        /// <summary>The vocabularies these documents reach for.</summary>
        private const string Requires = StandardRuntime.Requires;

        /// <summary>The same document with its <c>requires</c> left to whoever is writing it.</summary>
        private const string Bare = """
              "id": "t", "version": "1.0.0",
              "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
            """;

        /// <summary>A rule set with one integer field and nothing else, to hang a fault on.</summary>
        private const string Preamble = Requires + Bare;

        [Fact]
        public void AnUnknownOperationNamesThePluginList() =>
            Assert.Contains("requires", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "effects": [
                  { "op": "nope.thing" } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AVocabularyTheDocumentDoesNotRequireIsRefusedThoughItIsLoaded()
        {
            // The fault this catches has no symptom until the document is moved: a rule set
            // that reaches an operation it did not declare compiles wherever that plugin
            // happens to be loaded and fails wherever it is not, which is what makes
            // 'requires' worth reading only if it is complete.
            string refused = Rejects("""
                { "id": "t", "version": "1.0.0",
                  "requires": [ { "plugin": "Rulealize.Plugin.TypeSchema" },
                                { "plugin": "Rulealize.Plugin.State" } ],
                  "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
                  "inputs": { "go": { "when": { "op": "cmp.eq", "left": 1, "right": 1 }, "effects": [] } } }
                """);

            Assert.Contains("Rulealize.Plugin.Comparison", refused, StringComparison.Ordinal);
            Assert.Contains("does not name in 'requires'", refused, StringComparison.Ordinal);
        }

        [Fact]
        public void AnInputNameMayNotContainTheCharacterThatQualifiesOne() =>
            // Nothing holds another rule set yet. The name is reserved now because a document
            // written with a dot in it would become ambiguous the day one does, and a format
            // that takes a name away later takes it from documents already in production.
            Assert.Contains("may not contain '.'", Rejects($$"""
                { {{Preamble}} "inputs": { "req.raise": { "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AnEffectCannotAppearWhereAnExpressionBelongs() =>
            Assert.Contains("is an effect and cannot appear", Rejects($$"""
                { {{Preamble}} "inputs": { "go": {
                  "when": { "op": "state.set", "path": "n", "value": 1 }, "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AnExpressionCannotAppearWhereAnEffectBelongs() =>
            Assert.Contains("is an expression and cannot appear", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "effects": [ { "op": "state.get", "path": "n" } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void ASchemaCannotAppearWhereAnExpressionBelongs() =>
            Assert.Contains("is a schema and cannot appear", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "effects": [
                  { "op": "state.set", "path": "n", "value": { "op": "type.int" } } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AnUnboundLocalIsCaughtFromTheShapeOfTheDocument() =>
            Assert.Contains("not bound here", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "effects": [
                  { "op": "state.set", "path": "n", "value": "@nope" } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void ALocalIsVisibleOnlyInsideWhatIntroducedIt() =>
            // 'c' is bound over seq.any's predicate, and not over anything after it.
            Assert.Contains("not bound here", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "effects": [
                  { "op": "state.set", "path": "n", "value": { "op": "bind.let",
                    "bind": { "found": { "op": "seq.any", "source": { "op": "seq.empty" }, "as": "c",
                                         "predicate": true } },
                    "in": "@c" } } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void ADefinitionBodyCannotSeeItsCallersLocals() =>
            Assert.Contains("not bound here", Rejects($$"""
                { {{Preamble}}
                  "definitions": { "leaky": { "params": ["a"], "body": "@fromCaller" } },
                  "inputs": { "go": { "effects": [
                    { "op": "state.set", "path": "n", "value": { "op": "bind.let",
                      "bind": { "fromCaller": 1 },
                      "in": { "op": "def.call", "def": "leaky", "args": { "a": 1 } } } } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void DefinitionsCannotBeRecursive() =>
            Assert.Contains("must not be recursive", Rejects($$"""
                { {{Preamble}}
                  "definitions": { "a": { "op": "def.ref", "name": "b" },
                                   "b": { "op": "def.ref", "name": "a" } },
                  "inputs": { "go": { "effects": [ { "op": "state.set", "path": "n", "value": "#a" } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void ADefinitionMayReferToOneWrittenAfterIt()
        {
            // The names are all declared before any body is built, so order does not matter.
            RuleContext context = standard.Runtime.CreateContext($$"""
                { {{Preamble}}
                  "definitions": { "first": { "op": "def.ref", "name": "second" }, "second": 7 },
                  "inputs": { "go": { "effects": [ { "op": "state.set", "path": "n", "value": "#first" } ] } } }
                """);

            Assert.Equal("t@1.0.0", context.RuleSet);
        }

        [Fact]
        public void AnEntryWithNoBodyKeyIsItsOwnBody()
        {
            // The short form is recognised by the absence of 'body', not by the presence of
            // 'op', because a body is an expression and an expression need not be a node.
            RuleContext context = standard.Runtime.CreateContext($$"""
                { {{Preamble}}
                  "definitions": { "limit": 2, "flag": true, "name": "steady",
                                   "pair": { "a": 1, "b": 2 } },
                  "inputs": { "go": { "when": { "op": "cmp.lt", "left": "$n", "right": "#limit" },
                                      "effects": [] } } }
                """);

            Assert.Single(context.GetValidInputs(context.InitialState, 8));
        }

        [Fact]
        public void ParametersWithoutABodyAreRejected() =>
            Assert.Contains("no 'body'", Rejects($$"""
                { {{Preamble}}
                  "definitions": { "f": { "params": ["a"] } },
                  "inputs": { "go": { "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void CallingADefinitionWithTheWrongArgumentsIsRejected() =>
            Assert.Contains("no parameter named", Rejects($$"""
                { {{Preamble}}
                  "definitions": { "f": { "params": ["a"], "body": "@a" } },
                  "inputs": { "go": { "effects": [ { "op": "state.set", "path": "n",
                    "value": { "op": "def.call", "def": "f", "args": { "a": 1, "b": 2 } } } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void ReferencingADefinitionThatTakesParametersIsRejected() =>
            Assert.Contains("def.call", Rejects($$"""
                { {{Preamble}}
                  "definitions": { "f": { "params": ["a"], "body": "@a" } },
                  "inputs": { "go": { "effects": [ { "op": "state.set", "path": "n", "value": "#f" } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void CallingADefinitionThatTakesNoParametersIsRejected() =>
            Assert.Contains("def.ref", Rejects($$"""
                { {{Preamble}}
                  "definitions": { "f": 1 },
                  "inputs": { "go": { "effects": [ { "op": "state.set", "path": "n",
                    "value": { "op": "def.call", "def": "f", "args": {} } } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AStatePathIsCheckedAgainstTheSchema() =>
            Assert.Contains("not a field of the state schema", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "effects": [
                  { "op": "state.set", "path": "zzz", "value": 1 } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AStaticKeyRefusesAnExpression() =>
            Assert.Contains("not an expression", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "effects": [
                  { "op": "state.set", "path": { "op": "state.get", "path": "n" }, "value": 1 } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AMissingRequiredKeyNamesTheOperation() =>
            Assert.Contains("'grid.at' needs a 'coord'", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "effects": [
                  { "op": "state.set", "path": "n", "value": { "op": "grid.at", "grid": 1 } } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AnUnloadedPluginIsNamed() =>
            Assert.Contains("Rulealize.Plugin.Nope", Rejects($$"""
                { {{Bare}} "requires": [ { "plugin": "Rulealize.Plugin.Nope" } ],
                  "inputs": { "go": { "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AVersionConstraintIsEnforced() =>
            Assert.Contains("needs Rulealize.Plugin.Grid ^2.0", Rejects($$"""
                { {{Bare}} "requires": [ { "plugin": "Rulealize.Plugin.Grid", "version": "^2.0" } ],
                  "inputs": { "go": { "effects": [] } } }
                """), StringComparison.Ordinal);

        // Written against Logic rather than Grid, which the rest of this file leans on:
        // an exact-version constraint has to name a version, and pinning one to a plugin
        // whose vocabulary is still growing means editing this test every time it does.
        [Theory]
        [InlineData("^1.0")]
        [InlineData("^1.0.0")]
        [InlineData(">=0.9")]
        [InlineData("1.0.0")]
        public void SatisfiableConstraintsAreAccepted(string constraint)
        {
            RuleContext context = standard.Runtime.CreateContext($$"""
                { {{Bare}} "requires": [ { "plugin": "Rulealize.Plugin.TypeSchema" },
                    { "plugin": "Rulealize.Plugin.Logic", "version": "{{constraint}}" } ],
                  "inputs": { "go": { "effects": [] } } }
                """);

            Assert.Equal("t", context.Id);
        }

        [Fact]
        public void AnInitialStateThatBreaksItsOwnSchemaIsRejected() =>
            Assert.Contains("Expected at most 2", Rejects($$"""
                {
                {{Requires}}
                  "id": "t", "version": "1.0.0",
                  "state": { "schema": { "n": { "op": "type.int", "max": 2 } }, "initial": { "n": 9 } },
                  "inputs": { "go": { "effects": [] } }
                }
                """), StringComparison.Ordinal);

        [Fact]
        public void AWriteTargetIsCheckedAgainstItsSchema() =>
            // grid.set is pointed at a counter, which it can tell is not a board without
            // knowing anything about the plugin that owns the path.
            Assert.Contains("is not a board", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "effects": [
                  { "op": "grid.set", "target": "$n", "coord": "0,0", "value": 1 } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AWriteTargetMustDenoteAStateField() =>
            Assert.Contains("must denote a state field", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "effects": [
                  { "op": "grid.set", "target": 1, "coord": "0,0", "value": 1 } ] } } }
                """), StringComparison.Ordinal);

        [Theory]
        [InlineData("""{ "op": "type.enum", "values": [] }""", "must not be empty")]
        [InlineData("""{ "op": "type.enum", "values": ["a", "a"] }""", "listed more than once")]
        [InlineData("""{ "op": "grid.board", "width": 0, "height": 8, "cell": { "op": "type.bool" } }""", "at least 1")]
        [InlineData(
            """{ "op": "grid.board", "width": 30, "height": 8, "coord": "algebraic", "cell": { "op": "type.bool" } }""",
            "one letter per column")]
        [InlineData(
            """{ "op": "grid.board", "width": 8, "height": 8, "coord": "spiral", "cell": { "op": "type.bool" } }""",
            "index, algebraic")]
        public void SchemaNodesValidateTheirOwnKeys(string schema, string expected) =>
            Assert.Contains(expected, Rejects($$"""
                {
                {{Requires}}
                  "id": "t", "version": "1.0.0",
                  "state": { "schema": { "f": {{schema}} }, "initial": { "f": null } },
                  "inputs": { "go": { "effects": [] } }
                }
                """), StringComparison.OrdinalIgnoreCase);

        [Fact]
        public void AnEmptyLetIsRejected() =>
            Assert.Contains("must not be empty", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "effects": [
                  { "op": "state.set", "path": "n", "value": { "op": "bind.let", "bind": {}, "in": 1 } } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AnEmptyMatchIsRejected() =>
            Assert.Contains("must not be empty", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "effects": [
                  { "op": "state.set", "path": "n",
                    "value": { "op": "branch.match", "value": 1, "cases": {} } } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void ARuleSetNeedsInputs() =>
            Assert.Contains("needs a 'inputs'", Rejects($$"""{ {{Preamble}} "terminal": { "when": true } }"""),
                StringComparison.Ordinal);

        [Fact]
        public void AnArrayIsNotAnExpression() =>
            Assert.Contains("never written down", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "effects": [
                  { "op": "state.set", "path": "n", "value": [1, 2] } ] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void MalformedJsonIsReportedAsSuch() =>
            Assert.Contains("not valid JSON", Rejects("{ nope"), StringComparison.Ordinal);

        [Fact]
        public void CommentsAndTrailingCommasAreAccepted()
        {
            // A rule set of any size needs somewhere to say why a rule is the way it is.
            RuleContext context = standard.Runtime.CreateContext($$"""
                {
                {{Requires}}
                  // the identity of this rule set
                  "id": "t", "version": "1.0.0",
                  "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
                  "inputs": { "go": { "effects": [] } },   /* nothing happens */
                }
                """);

            Assert.Equal("t@1.0.0", context.RuleSet);
        }

        [Fact]
        public void ADiagnosticPointsAtWhereTheFaultIs()
        {
            RuleSetBuildException exception = Assert.Throws<RuleSetBuildException>(
                () => standard.Runtime.CreateContext($$"""
                    { {{Preamble}} "inputs": { "go": { "effects": [
                      { "op": "state.set", "path": "n", "value": "@nope" } ] } } }
                    """));

            Assert.Equal("/inputs/go/effects[0]/value", exception.Path.ToString());
        }

        /// <summary>Every position whose keys the core fixes, and the typo each one catches.</summary>
        /// <remarks>
        /// A misspelled key that happens to be optional is the fault worth having this for.
        /// <c>whn</c> is not a rule set that fails to compile — it is an input with no guard,
        /// which is an input that is always legal, and nothing downstream can tell that from
        /// a rule set that meant it.
        /// </remarks>
        [Theory]
        [InlineData("a rule set", """
            { "nonsense": 1, "id": "t", "version": "1.0.0",
              "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
              "inputs": { "go": { "effects": [] } } }
            """)]
        [InlineData("a requirement", """
            { "id": "t", "version": "1.0.0", "requires": [ { "plugin": "Rulealize.Plugin.State", "nonsense": 1 } ],
              "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
              "inputs": { "go": { "effects": [] } } }
            """)]
        [InlineData("the state section", """
            { "id": "t", "version": "1.0.0",
              "state": { "nonsense": 1, "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
              "inputs": { "go": { "effects": [] } } }
            """)]
        [InlineData("a definition", """
            { "id": "t", "version": "1.0.0", "requires": [ { "plugin": "Rulealize.Plugin.TypeSchema" } ],
              "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
              "definitions": { "d": { "body": 1, "nonsense": 1 } },
              "inputs": { "go": { "effects": [] } } }
            """)]
        [InlineData("an input", """
            { "id": "t", "version": "1.0.0", "requires": [ { "plugin": "Rulealize.Plugin.TypeSchema" } ],
              "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
              "inputs": { "go": { "nonsense": 1, "effects": [] } } }
            """)]
        [InlineData("a parameter", """
            { "id": "t", "version": "1.0.0",
              "requires": [ { "plugin": "Rulealize.Plugin.TypeSchema" }, { "plugin": "Rulealize.Plugin.Sequence" } ],
              "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
              "inputs": { "go": {
                "params": { "p": { "nonsense": 1, "domain": { "op": "seq.of", "of": [1] } } },
                "effects": [] } } }
            """)]
        [InlineData("the terminal section", """
            { "id": "t", "version": "1.0.0", "requires": [ { "plugin": "Rulealize.Plugin.TypeSchema" } ],
              "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
              "inputs": { "go": { "effects": [] } },
              "terminal": { "nonsense": 1, "when": true } }
            """)]
        public void AKeyTheCoreDoesNotKnowIsRefusedWhereTheCoreOwnsThemAll(string what, string ruleSet)
        {
            string refused = Rejects(ruleSet);

            Assert.Contains("nonsense", refused, StringComparison.Ordinal);
            Assert.Contains($"is not a key {what} takes", refused, StringComparison.Ordinal);
        }

        [Fact]
        public void AnOptionalKeyMisspeltIsRefusedRatherThanReadAsAbsent()
        {
            // Without this, 'go' is an input with no guard, so it is legal in every state.
            RuleSetBuildException refused = Assert.Throws<RuleSetBuildException>(
                () => standard.Runtime.CreateContext($$"""
                    { {{Preamble}} "inputs": { "go": {
                      "whn": { "op": "cmp.eq", "left": "$n", "right": 1 }, "effects": [] } } }
                    """));

            Assert.Equal("/inputs/go/whn", refused.Path.ToString());
        }

        [Fact]
        public void AKeyInsideANodeBelongsToItsPluginAndIsNotTheCoreToRefuse() =>
            // 'seq.of' reads 'of'. Whether it also reads 'unless' is that plugin's business,
            // and a core that refused the name would make adding an argument to an operation
            // a change to the runtime.
            standard.Runtime.CreateContext($$"""
                { {{Preamble}} "inputs": { "go": {
                  "params": { "p": { "domain": { "op": "seq.of", "of": [1], "unless": 2 } } },
                  "effects": [] } } }
                """);

        [Fact]
        public void AFieldStateInitialDeclaresAndTheSchemaDoesNotIsReportedWithTheRest()
        {
            string refused = Rejects($$"""
                { {{Requires}} "id": "t", "version": "1.0.0",
                  "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0, "m": 1 } },
                  "inputs": { "go": { "effects": [] } } }
                """);

            Assert.Contains("m: is not a field of the state schema.", refused, StringComparison.Ordinal);
        }

        [Fact]
        public void ADefinitionThatIsItsOwnBodyKeepsWhateverKeysItHas() =>
            // The short form: the value is the body, so a record literal written there is a
            // record and not a definition with keys the core would have an opinion about.
            standard.Runtime.CreateContext($$"""
                { {{Preamble}} "definitions": { "d": { "anything": 1, "at": "all" } },
                  "inputs": { "go": { "effects": [] } } }
                """);

        private string Rejects(string ruleSet) =>
            Assert.Throws<RuleSetBuildException>(() => standard.Runtime.CreateContext(ruleSet)).Message;
    }
}
