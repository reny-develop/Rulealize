// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction;

namespace Rulealize.Tests
{
    /// <summary>A parameter whose value comes from outside, and the hole it leaves in a move.</summary>
    /// <remarks>
    /// <para>
    /// A domain answers per value: every candidate is formed and put to the guard, so a move
    /// that comes back out of <c>GetValidInputs</c> is one the rules have already allowed. An
    /// open parameter cannot work that way, because the value is somebody's to type. What it
    /// trades that for is one candidate instead of a domain's worth, and a schema saying what
    /// would be admissible in place of a list of what is.
    /// </para>
    /// <para>
    /// The promise these are mostly about is the one that did <em>not</em> change: a rule set
    /// with no open parameter has no incomplete moves, and every existing document is such a
    /// rule set.
    /// </para>
    /// </remarks>
    [Collection(StandardCollection.Name)]
    public class OpenParameterTests(StandardRuntime standard)
    {
        private const string Requires = StandardRuntime.Requires;

        /// <summary>More candidates than any of these documents can produce.</summary>
        private const int Limit = 1000;

        /// <summary>One open parameter, one closed one, and one that is a list.</summary>
        private const string Forms = $$"""
            {
              "id": "forms", "version": "1.0.0",
            {{Requires}}
              "state": {
                "schema": {
                  "name": { "op": "type.string", "maxLength": 20 },
                  "n": { "op": "type.int", "min": 0, "max": 99 },
                  "tags": { "op": "type.list", "maxLength": 3,
                            "element": { "op": "type.string", "maxLength": 4 } }
                },
                "initial": { "name": "", "n": 0, "tags": [] }
              },
              "inputs": {
                "rename": {
                  "params": { "to": { "open": { "op": "type.string", "minLength": 3, "maxLength": 20 } } },
                  "when": { "op": "cmp.lt", "left": "$n", "right": 99 },
                  "effects": [ { "op": "state.set", "path": "name", "value": "@to" } ]
                },
                "adjust": {
                  "params": {
                    "by": { "domain": { "op": "seq.range", "from": 1, "count": 3 } },
                    "why": { "open": { "op": "type.string", "maxLength": 4 } }
                  },
                  "effects": [
                    { "op": "state.set", "path": "n",
                      "value": { "op": "math.add", "of": ["$n", "@by"] } },
                    { "op": "state.set", "path": "name", "value": "@why" } ]
                },
                "retag": {
                  "params": { "tags": { "open": { "op": "type.list", "maxLength": 3,
                                                  "element": { "op": "type.string", "maxLength": 4 } } } },
                  "effects": [ { "op": "state.set", "path": "tags", "value": "@tags" } ]
                },
                "label": {
                  "params": { "text": { "open": { "field": "name" } } },
                  "effects": [ { "op": "state.set", "path": "name", "value": "@text" } ]
                }
              }
            }
            """;

        [Fact]
        public void AParameterIsEitherEnumeratedOrOpenAndNotBoth() =>
            Assert.Contains("has both a 'domain' and an 'open'", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "params": { "x": {
                  "domain": { "op": "seq.of", "of": [1] },
                  "open": { "op": "type.int" } } }, "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AParameterWithNeitherIsRefused() =>
            Assert.Contains("needs a 'domain'", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "params": { "x": { } }, "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void OpenTakesASchemaNodeAndNotAnExpression() =>
            // The kind check already in place, reached through a position that did not exist
            // before: `open` is the second place a schema node may be written.
            Assert.Contains("seq.of", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "params": { "x": {
                  "open": { "op": "seq.of", "of": [1] } } }, "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AGuardMayNotReadAnOpenParameter() =>
            // The whole reason the feature is shaped this way. A guard is evaluated while
            // candidates are formed, and there is no value then — so a guard that read one
            // could only be answered by guessing, and the guess would be published as a legal
            // move.
            Assert.Contains("leaves open", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "params": { "x": { "open": { "op": "type.int" } } },
                  "when": { "op": "cmp.eq", "left": "@x", "right": 1 }, "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AnActorMayNotReadAnOpenParameter() =>
            // Evaluated beside the guard, so it is in exactly the same position.
            Assert.Contains("leaves open", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "params": { "x": { "open": { "op": "type.string" } } },
                  "actor": "@x", "effects": [] } } }
                """), StringComparison.Ordinal);

        [Fact]
        public void AHolderMayNotReadAnOpenParameterOfWhatItHolds()
        {
            // Whether a parameter is open is part of an input's public shape, so the refusal
            // crosses the `uses` boundary: a holder writing a guard over a held input is in
            // the same position as the rule set that declared it.
            Dictionary<string, string> held = new(StringComparer.Ordinal) { ["note"] = Note };

            Assert.Contains(
                "leaves open",
                Assert.Throws<RuleSetBuildException>(
                    () => standard.Runtime.CreateContext(Folder, held)).Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public void AnEffectMayReadAnOpenParameter() =>
            // The position that is not refused, and the only one that needs saying: by the
            // time effects run, the value has arrived.
            Assert.Equal("okay", Applied("rename", """ "to": "okay" """, "name"));

        [Fact]
        public void AnOpenParameterContributesOneCandidateRatherThanADomainsWorth()
        {
            ValidInputSet moves = Context().GetValidInputs(Context().InitialState, Limit);

            // rename is one, and adjust is three: the open `why` multiplies nothing.
            Assert.Equal(1, moves.Count(move => move.Input == "rename"));
            Assert.Equal(3, moves.Count(move => move.Input == "adjust"));
            Assert.Equal(1, moves.Count(move => move.Input == "retag"));
        }

        [Fact]
        public void AnAvailableMoveSaysWhatItIsStillWaitingFor()
        {
            ValidInput rename = Move("rename");

            Assert.False(rename.IsComplete);
            Assert.True(rename.Arguments.IsEmpty);
            OpenParameter open = Assert.Single(rename.Open);
            Assert.Equal("to", open.Name);
            Assert.Equal("type.string", open.Op);
        }

        [Fact]
        public void AMoveWithBothKindsOfParameterKeepsThemApartAndInOrder()
        {
            ValidInput adjust = Move("adjust");

            Assert.Equal("1", adjust.Arguments["by"]);
            Assert.Equal("why", Assert.Single(adjust.Open).Name);

            // Declared order, whether a parameter has a value or is still open.
            Assert.Equal("adjust(by: 1, why: <type.string>)", adjust.ToString());
        }

        [Fact]
        public void AnIncompleteMoveIsNotADocument()
        {
            // The round trip GetValidInputs opens is a promise about a complete move. Writing
            // the argument out as absent would make a document that names a different move, so
            // a caller walking moves is stopped here instead of being handed one.
            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
                () => Move("rename").ToInputDocument("forms@1.0.0"));

            Assert.Contains("'to'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ASetSaysInOneQuestionWhetherAnythingInItIsWaiting()
        {
            Assert.True(Context().GetValidInputs(Context().InitialState, Limit).HasOpenParameters);

            // And the promise that did not change: a rule set with no open parameter has no
            // incomplete moves, so a traversal written before any of this still holds.
            ValidInputSet countdown = standard.Countdown.GetValidInputs(standard.Countdown.InitialState, Limit);
            Assert.False(countdown.HasOpenParameters);
            Assert.All(countdown, move => Assert.True(move.IsComplete));
        }

        [Fact]
        public void TheSchemaAdmitsTheValueOrTheInputIsRefused()
        {
            // minLength 3, so two characters is not a value this parameter is open to. Refused
            // as an illegal input rather than a malformed document: the document was fine and
            // the rules said no.
            IllegalInputException refused = Assert.Throws<IllegalInputException>(
                () => Applied("rename", """ "to": "no" """, "name"));

            Assert.Contains("to", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AValueOfTheWrongKindIsRefusedRatherThanConverted() =>
            Assert.Throws<IllegalInputException>(() => Applied("rename", """ "to": 3 """, "name"));

        [Fact]
        public void TheSchemaReadsTheArgumentSoAnOpenParameterMayTakeAShapeNoDomainCould()
        {
            // An array is refused for a parameter with a domain, because the value model has
            // no literal for a sequence. An open parameter is read by its own schema node,
            // which owns the JSON its values are written as — so a list parameter takes a list.
            Assert.Equal("a,bb", Applied("retag", """ "tags": ["a", "bb"] """, "tags"));

            Assert.Throws<IllegalInputException>(() => Applied("retag", """ "tags": ["toolong"] """, "tags"));
        }

        [Fact]
        public void AParameterMayTakeTheSchemaOfTheFieldItIsEditedInto()
        {
            OpenParameter text = Assert.Single(Move("label").Open);

            Assert.Equal("name", text.Field);

            // The op is the field's, because the schema is the field's: a host builds the
            // editor from the same declaration the state is checked against.
            Assert.Equal("type.string", text.Op);
        }

        [Fact]
        public void AFieldsParameterAdmitsExactlyWhatTheFieldHolds()
        {
            Assert.Equal("fine", Applied("label", """ "text": "fine" """, "name"));

            // 21 characters, where the field holds 20. Refused as the argument arrives rather
            // than when the transition commits — which is the difference the form was for, and
            // it needs no check of its own because there is only ever one declaration.
            Assert.Throws<IllegalInputException>(() => Applied("label", """ "text": "123456789012345678901" """, "name"));
        }

        [Fact]
        public void AParameterWrittenOutNamesNoField() =>
            Assert.Null(Assert.Single(Move("rename").Open).Field);

        [Fact]
        public void AParameterCannotBeEditedIntoAFieldThatDoesNotExist() =>
            Assert.Contains("is not a field of this rule set's state", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "params": { "x": { "open": { "field": "nope" } } },
                  "effects": [] } } }
                """),
                StringComparison.Ordinal);

        [Fact]
        public void AFieldIsTheWholeOfWhatThatFormSays() =>
            // Naming a field and then qualifying it would be two declarations again, which is
            // the thing this form exists to make unsayable.
            Assert.Contains("is not a key", Rejects($$"""
                { {{Preamble}} "inputs": { "go": { "params": { "x": {
                  "open": { "field": "n", "op": "type.int" } } }, "effects": [] } } }
                """), StringComparison.Ordinal);

        /// <summary>A rule set that leaves a parameter open, to be held by another.</summary>
        private const string Note = $$"""
            {
              "id": "note", "version": "1.0.0",
            {{Requires}}
              "state": { "schema": { "text": { "op": "type.string", "maxLength": 20 } },
                         "initial": { "text": "" } },
              "inputs": {
                "write": {
                  "params": { "to": { "open": { "op": "type.string", "maxLength": 20 } } },
                  "effects": [ { "op": "state.set", "path": "text", "value": "@to" } ]
                }
              }
            }
            """;

        /// <summary>A holder reaching for what it is not allowed to see.</summary>
        private const string Folder = $$"""
            {
              "id": "folder", "version": "1.0.0",
            {{Requires}}
              "uses": [ { "ruleSet": "note", "version": "^1.0", "as": "n" } ],
              "held": { "n": { "write": {
                "when": { "op": "cmp.eq", "left": "@to", "right": "x" } } } }
            }
            """;

        /// <summary>A rule set with one integer field and nothing else, to hang a fault on.</summary>
        private const string Preamble = Requires + """
              "id": "t", "version": "1.0.0",
              "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 0 } },
            """;

        private RuleContext Context() => standard.Runtime.CreateContext(Forms);

        private ValidInput Move(string input)
        {
            RuleContext context = Context();
            return context.GetValidInputs(context.InitialState, Limit).First(move => move.Input == input);
        }

        private string Applied(string input, string args, string field)
        {
            RuleContext context = Context();
            string state = context
                .ApplyToState($$"""{ "input": "{{input}}", "args": { {{args}} } }""", context.InitialState)
                .State;

            using JsonDocument document = JsonDocument.Parse(state);
            JsonElement value = document.RootElement.GetProperty("data").GetProperty(field);
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString()!,
                JsonValueKind.Array => string.Join(",", value.EnumerateArray().Select(item => item.ToString())),
                _ => value.GetRawText()
            };
        }

        private string Rejects(string ruleSet) =>
            Assert.Throws<RuleSetBuildException>(() => standard.Runtime.CreateContext(ruleSet)).Message;
    }
}
