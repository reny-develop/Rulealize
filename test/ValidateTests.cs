// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using Rulealize.Abstraction;

namespace Rulealize.Tests
{
    /// <summary>What an argument is held to once it arrives, which a guard could not ask.</summary>
    /// <remarks>
    /// <para>
    /// A guard decides whether an input is offered, and is evaluated before an open parameter
    /// has a value. A <c>validate</c> clause decides whether the value that came back is one
    /// the rules take. Keeping them apart is what leaves `GetValidInputs` saying what it always
    /// said: a complete move it offers is a move that will apply.
    /// </para>
    /// <para>
    /// The two refusals in the compiler are the whole of that guarantee. An input with no open
    /// parameter may not be validated, because a rule about a value the guard could already
    /// see belongs in the guard; and a clause that reads no open parameter is the same fault
    /// from the other side.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class ValidateTests(StandardRuntime standard)
    {
        private const string Requires = StandardRuntime.Requires;

        private const int Limit = 1000;

        /// <summary>Two clauses about one field, and one about a pair.</summary>
        private const string Forms = $$"""
            {
              "id": "forms", "version": "1.0.0",
            {{Requires}}
              "state": {
                "schema": {
                  "name": { "op": "type.string", "maxLength": 20 },
                  "n": { "op": "type.int", "min": 0, "max": 99 }
                },
                "initial": { "name": "start", "n": 0 }
              },
              "inputs": {
                "rename": {
                  "params": { "to": { "open": { "op": "type.string", "maxLength": 20 } } },
                  "validate": [
                    { "require": { "op": "logic.not", "value":
                        { "op": "cmp.eq", "left": "@to", "right": "$name" } },
                      "code": "name.unchanged" },
                    { "require": { "op": "logic.not", "value":
                        { "op": "cmp.eq", "left": "@to", "right": "admin" } },
                      "code": "name.reserved" }
                  ],
                  "effects": [ { "op": "state.set", "path": "name", "value": "@to" } ]
                },
                "span": {
                  "params": {
                    "from": { "open": { "op": "type.int", "min": 0, "max": 99 } },
                    "to": { "open": { "op": "type.int", "min": 0, "max": 99 } }
                  },
                  "validate": [
                    { "require": { "op": "cmp.lt", "left": "@from", "right": "@to" },
                      "code": "span.order" }
                  ],
                  "effects": [ { "op": "state.set", "path": "n", "value": "@to" } ]
                }
              }
            }
            """;

        [Fact]
        public void OnlyAnInputThatLeavesAParameterOpenMayBeValidated() =>
            // A rule about a value that is known when the input is offered belongs in the
            // guard. Allowing it here would offer a move and then refuse it, which is the
            // fault a domain that drew would have.
            Assert.Contains("only an input that leaves a parameter open", Rejects($$"""
                { {{Preamble}} "inputs": { "go": {
                  "params": { "x": { "domain": { "op": "seq.of", "of": [1, 2] } } },
                  "validate": [ { "require": { "op": "cmp.lt", "left": "@x", "right": 2 },
                                  "code": "x.big" } ],
                  "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AClauseThatReadsNoOpenParameterBelongsInTheGuard() =>
            // The same fault from the other side: this one could have been decided before the
            // input was offered, and leaving it here makes every answer a little less true.
            Assert.Contains("belongs in 'when'", Rejects($$"""
                { {{Preamble}} "inputs": { "go": {
                  "params": { "x": { "open": { "op": "type.int" } } },
                  "validate": [ { "require": { "op": "cmp.lt", "left": "$n", "right": 5 },
                                  "code": "n.big" } ],
                  "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void ACodeNamesOneRefusal() =>
            Assert.Contains("already used by validate[0]", Rejects($$"""
                { {{Preamble}} "inputs": { "go": {
                  "params": { "x": { "open": { "op": "type.int" } } },
                  "validate": [
                    { "require": { "op": "cmp.lt", "left": "@x", "right": 5 }, "code": "x.bad" },
                    { "require": { "op": "cmp.gt", "left": "@x", "right": 0 }, "code": "x.bad" } ],
                  "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AValueTheClausesAcceptApplies() =>
            Assert.Equal("second", Applied("rename", """ "to": "second" """));

        [Fact]
        public void AValueAClauseRefusesComesBackAsItsCode()
        {
            InputRejectedException refused = Assert.Throws<InputRejectedException>(
                () => Applied("rename", """ "to": "admin" """));

            InputRejection rejection = Assert.Single(refused.Rejections);
            Assert.Equal("name.reserved", rejection.Code);

            // Inferred from what the clause read, so a screen can put it against the field
            // without the document saying which field twice.
            Assert.Equal("to", rejection.Parameter);
        }

        [Fact]
        public void EveryClauseIsAskedRatherThanStoppingAtTheFirst()
        {
            // "start" is the current name, so it is unchanged; it is not "admin", so only one
            // clause refuses it. The both-at-once case needs a value that breaks both.
            InputRejectedException refused = Assert.Throws<InputRejectedException>(
                () => Applied("rename", """ "to": "start" """));

            Assert.Equal(["name.unchanged"], refused.Rejections.Select(r => r.Code));

            RuleContext context = standard.Runtime.CreateContext(
                Forms.Replace("\"name\": \"start\"", "\"name\": \"admin\"", StringComparison.Ordinal));

            InputRejectedException both = Assert.Throws<InputRejectedException>(
                () => context.ApplyToState(
                    """{ "input": "rename", "args": { "to": "admin" } }""",
                    context.InitialState));

            // Both, in the order the clauses are written: a form wrong in two places takes one
            // round trip to learn that.
            Assert.Equal(["name.unchanged", "name.reserved"], both.Rejections.Select(r => r.Code));
        }

        [Fact]
        public void AClauseAboutMoreThanOneFieldIsAboutTheForm()
        {
            InputRejectedException refused = Assert.Throws<InputRejectedException>(
                () => Context().ApplyToState(
                    """{ "input": "span", "args": { "from": 8, "to": 3 } }""",
                    Context().InitialState));

            Assert.Equal("span.order", Assert.Single(refused.Rejections).Code);
            Assert.Null(Assert.Single(refused.Rejections).Parameter);
        }

        [Fact]
        public void AClauseDoesNotDecideWhetherTheMoveIsOffered()
        {
            // The point of the division. "admin" would be refused, but nothing has been typed
            // when the move is listed, so the move is listed.
            ValidInputSet moves = Context().GetValidInputs(Context().InitialState, Limit);

            Assert.Contains(moves, move => move.Input == "rename");
            Assert.True(moves.HasOpenParameters);
        }

        [Fact]
        public void AnInputIsHeldToItsOwnClausesWhenSomethingElseDrivesIt()
        {
            // The composite supplies the argument from an expression, so there is no hole and
            // the component's clause can be asked while candidates are still being formed.
            Dictionary<string, string> held = new(StringComparer.Ordinal) { ["note"] = Note };
            RuleContext folder = standard.Runtime.CreateContext(Folder, held);

            IllegalInputException refused = Assert.Throws<IllegalInputException>(
                () => folder.ApplyToState(
                    """{ "input": "stamp" }""",
                    folder.InitialState));

            Assert.Contains("note.reserved", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AValueItsSchemaRefusesComesBackAsTheCodeTheParameterGivesIt()
        {
            InputRejectedException refused = Assert.Throws<InputRejectedException>(
                () => Registered(""" "to": "much-too-long-for-twenty", "size": 2 """));

            InputRejection rejection = Assert.Single(refused.Rejections);
            Assert.Equal("name.malformed", rejection.Code);
            Assert.Equal("to", rejection.Parameter);
            Assert.Empty(refused.Unexplained);
        }

        [Fact]
        public void AValueOfTheWrongKindIsTheSameRefusal() =>
            Assert.Equal(
                "name.malformed",
                Assert.Single(Assert.Throws<InputRejectedException>(
                    () => Registered(""" "to": 3, "size": 2 """)).Rejections).Code);

        [Fact]
        public void WhatTheRuleSetGaveNoCodeComesBackAsTheSchemasSentenceBesideWhatItDid()
        {
            // 'to' has a code and 'size' has none. What the rule set named is not lost because
            // something else it did not name is wrong as well.
            InputRejectedException refused = Assert.Throws<InputRejectedException>(
                () => Registered(""" "to": "much-too-long-for-twenty", "size": 12 """));

            Assert.Equal(["name.malformed"], refused.Rejections.Select(r => r.Code));

            // Worded by the schema, as doc/ruleset-guide.md quotes it.
            Assert.Equal("size: Expected at most 8 but got 12.", Assert.Single(refused.Unexplained));
        }

        [Fact]
        public void WhereNothingWasGivenACodeTheRefusalIsWhatItAlwaysWas()
        {
            // Every parameter is asked rather than stopping at the first, but a rule set that
            // named none of these has said nothing a host could key on.
            IllegalInputException refused = Assert.Throws<IllegalInputException>(
                () => Context().ApplyToState(
                    """{ "input": "span", "args": { "from": 100, "to": -1 } }""",
                    Context().InitialState));

            Assert.Contains("from: ", refused.Message, StringComparison.Ordinal);
            Assert.Contains("to: ", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AClauseAboutARefusedValueIsNotAskedAndTheOthersAre()
        {
            // "admin" is reserved, but 'to' is refused already and is not a value the clause
            // was written against. The clause about 'size' is about a value that was admitted.
            InputRejectedException refused = Assert.Throws<InputRejectedException>(
                () => Registered(""" "to": 3, "size": 7 """));

            Assert.Equal(["name.malformed", "size.large"], refused.Rejections.Select(r => r.Code));
        }

        [Fact]
        public void WhetherTheMoveWasOnOfferIsAnsweredBeforeWhatCameBackForIt()
        {
            RuleContext context = standard.Runtime.CreateContext(Register.Replace(
                "\"when\": true", "\"when\": false", StringComparison.Ordinal));

            IllegalInputException refused = Assert.Throws<IllegalInputException>(
                () => context.ApplyToState(
                    """{ "input": "register", "args": { "to": 3, "size": 2 } }""",
                    context.InitialState));

            Assert.Contains("is not allowed in this state", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void OnlyAnOpenParameterNamesTheRefusalOfAValueFromOutside() =>
            Assert.Contains("is never given one", Rejects($$"""
                { {{Preamble}} "inputs": { "go": {
                  "params": { "x": { "domain": { "op": "seq.of", "of": [1, 2] }, "invalid": "x.bad" } },
                  "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AParametersCodeAndAClausesCodeNameDifferentRefusals() =>
            Assert.Contains("already used by params.x.invalid", Rejects($$"""
                { {{Preamble}} "inputs": { "go": {
                  "params": { "x": { "open": { "op": "type.int" }, "invalid": "x.bad" } },
                  "validate": [ { "require": { "op": "cmp.lt", "left": "@x", "right": 5 }, "code": "x.bad" } ],
                  "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void ACodeIsALiteralString() =>
            Assert.Contains("must be a literal string", Rejects($$"""
                { {{Preamble}} "inputs": { "go": {
                  "params": { "x": { "open": { "op": "type.int" }, "invalid": 3 } },
                  "effects": [] } } }
                """), StringComparison.Ordinal);

        /// <summary>One parameter whose refusal the rule set names, and one whose it does not.</summary>
        private const string Register = $$"""
            {
              "id": "register", "version": "1.0.0",
            {{Requires}}
              "state": { "schema": { "name": { "op": "type.string", "maxLength": 20 } },
                         "initial": { "name": "" } },
              "inputs": {
                "register": {
                  "params": {
                    "to": { "open": { "op": "type.string", "maxLength": 20 }, "invalid": "name.malformed" },
                    "size": { "open": { "op": "type.int", "min": 1, "max": 8 } }
                  },
                  "when": true,
                  "validate": [
                    { "require": { "op": "logic.not", "value":
                        { "op": "cmp.eq", "left": "@to", "right": "admin" } },
                      "code": "name.reserved" },
                    { "require": { "op": "cmp.lt", "left": "@size", "right": 7 },
                      "code": "size.large" }
                  ],
                  "effects": [ { "op": "state.set", "path": "name", "value": "@to" } ]
                }
              }
            }
            """;

        /// <summary>A component that refuses one particular value.</summary>
        private const string Note = $$"""
            {
              "id": "note", "version": "1.0.0",
            {{Requires}}
              "state": { "schema": { "text": { "op": "type.string", "maxLength": 20 } },
                         "initial": { "text": "" } },
              "inputs": {
                "write": {
                  "params": { "to": { "open": { "op": "type.string", "maxLength": 20 } } },
                  "validate": [
                    { "require": { "op": "logic.not", "value":
                        { "op": "cmp.eq", "left": "@to", "right": "admin" } },
                      "code": "note.reserved" } ],
                  "effects": [ { "op": "state.set", "path": "text", "value": "@to" } ]
                }
              }
            }
            """;

        /// <summary>A composite that drives it with the one value it will not take.</summary>
        private const string Folder = $$"""
            {
              "id": "folder", "version": "1.0.0",
            {{Requires}}
              "uses": [ { "ruleSet": "note", "version": "^1.0", "as": "n" } ],
              "inputs": {
                "stamp": { "fires": [ { "held": "n", "input": "write", "args": { "to": "admin" } } ] }
              }
            }
            """;

        /// <summary>A rule set with one integer field and nothing else, to hang a fault on.</summary>
        private const string Preamble = Requires + """
              "id": "t", "version": "1.0.0",
              "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
            """;

        private RuleContext Context() => standard.Runtime.CreateContext(Forms);

        private string Applied(string input, string args)
        {
            RuleContext context = Context();
            string state = context
                .ApplyToState($$"""{ "input": "{{input}}", "args": { {{args}} } }""", context.InitialState)
                .State;

            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(state);
            return document.RootElement.GetProperty("data").GetProperty("name").GetString()!;
        }

        private void Registered(string args)
        {
            RuleContext context = standard.Runtime.CreateContext(Register);
            context.ApplyToState($$"""{ "input": "register", "args": { {{args}} } }""", context.InitialState);
        }

        private string Rejects(string ruleSet) =>
            Assert.Throws<RuleSetBuildException>(() => standard.Runtime.CreateContext(ruleSet)).Message;
    }
}
