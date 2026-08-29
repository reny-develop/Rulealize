// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Evaluation;
using Rulealize.Abstraction.Node;
using Rulealize.Abstraction.Plugin;
using Rulealize.Abstraction.Value;

namespace Rulealize.Tests
{
    /// <summary>Which vocabulary a string literal beginning with a reserved character meant.</summary>
    /// <remarks>
    /// <para>
    /// A character is not spent by whoever reserved it first. Two plugins may reserve one,
    /// and a rule set that would otherwise be ambiguous says which it meant by writing the
    /// namespace between the character and a colon.
    /// </para>
    /// <para>
    /// A shorthand is vocabulary, so the candidates are what the document named in
    /// <c>requires</c> and never what a folder happens to hold. That is one rule doing two
    /// jobs: a character nobody the document requires reserved is refused, and a character
    /// two of them reserved is ambiguous. Which of the two a rule set is in does not change
    /// when a plugin is added beside it.
    /// </para>
    /// <para>
    /// The qualifier is read by its grammar, so what these documents mean does not change
    /// either. It still has to name a vocabulary the document requires.
    /// </para>
    /// </remarks>
    public class ShorthandTests
    {
        /// <summary>A rule set that copies a state field to another, through the shorthand under test.</summary>
        /// <remarks>
        /// The <c>requires</c> is not a parameter of these tests but a precondition of them:
        /// <c>type.int</c> and <c>state.set</c> are used by the template itself, so a document
        /// leaving their vocabularies out never reaches the literal under test. What a test
        /// varies is what is named <em>beyond</em> that.
        /// </remarks>
        private const string Template = """
            {
              "id": "t", "version": "1.0.0",
              "requires": [
                { "plugin": "Rulealize.Plugin.TypeSchema" }, { "plugin": "Rulealize.Plugin.State" },
                { "plugin": "Rulealize.Plugin.Binding" },    { "plugin": "Rulealize.Plugin.Definition" },
                { "plugin": "Rulealize.Plugin.Grid" }{{more}}
              ],
              "state": {
                "schema": { "n": { "op": "type.int" }, "m": { "op": "type.int" } },
                "initial": { "n": 7, "m": 0 }
              },
              "inputs": { "go": { "effects": [
                { "op": "state.set", "path": "m", "value": "{{value}}" } ] } }
            }
            """;

        /// <summary>The other vocabulary that reserves '$', as a <c>requires</c> entry.</summary>
        private const string Twin = """{ "plugin": "Twin", "version": "^1.0" }""";

        private static readonly RuleRuntime Standard =
            new RuleRuntime().LoadPluginsFrom(StandardRuntime.PluginFolder);

        /// <summary>The standard plugins, and one more vocabulary that also reserves '$'.</summary>
        /// <remarks>
        /// Its own runtime, because <c>AddPlugin</c> adds to the one it is called on and the
        /// tests above want a folder where '$' is still one vocabulary's.
        /// </remarks>
        private static readonly RuleRuntime Contested =
            new RuleRuntime().LoadPluginsFrom(StandardRuntime.PluginFolder).AddPlugin(new TwinPlugin());

        [Fact]
        public void AQualifiedShorthandBuildsWhatTheBareFormWould() =>
            Assert.Equal(Run(Standard, "$n"), Run(Standard, "$state:n"));

        [Fact]
        public void EachStandardShorthandAcceptsItsOwnNamespace()
        {
            // Not one mechanism per plugin: the qualifier is read the same way whichever
            // character carries it, and none of the three expanders knows it was written.
            const string Bare = """
                { "op": "bind.let", "bind": { "x": "$n" }, "in": "@x" }
                """;

            const string Qualified = """
                { "op": "bind.let", "bind": { "x": "$state:n" }, "in": "@bind:x" }
                """;

            Assert.Equal(7, Field(Standard, Bare));
            Assert.Equal(7, Field(Standard, Qualified));
        }

        [Fact]
        public void ADefinitionReferenceTakesTheQualifierToo()
        {
            string state = Go(Standard.CreateContext("""
                {
                  "id": "t", "version": "1.0.0",
                  "requires": [ { "plugin": "Rulealize.Plugin.TypeSchema" },
                                { "plugin": "Rulealize.Plugin.State" },
                                { "plugin": "Rulealize.Plugin.Definition" } ],
                  "state": { "schema": { "n": { "op": "type.int" }, "m": { "op": "type.int" } },
                             "initial": { "n": 7, "m": 0 } },
                  "definitions": { "seven": "$state:n" },
                  "inputs": { "go": { "effects": [
                    { "op": "state.set", "path": "m", "value": "#def:seven" } ] } }
                }
                """));

            Assert.Equal(7, Read(state));
        }

        [Fact]
        public void AQualifiedFormCarriesAColonPastTheQualifier() =>
            // The one thing the grammar takes away it also gives back. A vocabulary whose
            // shorthand text begins with something a qualifier looks like reaches it by
            // qualifying: 'state' is consumed, and what State is handed is "$a:b".
            Assert.Contains("'a:b' is not a field", Rejects(Standard, "$state:a:b"), StringComparison.Ordinal);

        [Fact]
        public void TheQualifierGrammarIsExactlyTheNamespaceGrammar() =>
            // 'State' is not a namespace — they are lowercase — so this is not a qualified
            // shorthand and the whole of it is a state path. Which plugins are loaded does
            // not enter into it.
            Assert.Contains("'State:n' is not a field", Rejects(Standard, "$State:n"), StringComparison.Ordinal);

        [Fact]
        public void TextWithNoReservedPrefixIsUntouched()
        {
            // The qualifier syntax is only ever read after a reserved character, so a colon
            // in an ordinary string is an ordinary colon.
            string state = Go(Standard.CreateContext("""
                {
                  "id": "t", "version": "1.0.0",
                  "requires": [ { "plugin": "Rulealize.Plugin.TypeSchema" },
                                { "plugin": "Rulealize.Plugin.State" } ],
                  "state": { "schema": { "s": { "op": "type.string" } }, "initial": { "s": "" } },
                  "inputs": { "go": { "effects": [
                    { "op": "state.set", "path": "s", "value": "state:n" } ] } }
                }
                """));

            Assert.Equal("state:n", Field(state, "s").GetString());
        }

        [Fact]
        public void AQualifierNamingANamespaceThatDoesNotReserveTheCharacterIsRefused()
        {
            string message = Rejects(Standard, "$grid:n");

            Assert.Contains("'grid' does not reserve '$'", message, StringComparison.Ordinal);
            Assert.Contains("'state' is the one vocabulary that does", message, StringComparison.Ordinal);
        }

        [Fact]
        public void AQualifierNamingNoLoadedNamespaceIsRefused() =>
            Assert.Contains("'nope' does not reserve '$'", Rejects(Standard, "$nope:n"), StringComparison.Ordinal);

        [Fact]
        public void TwoPluginsMayReserveTheSameCharacter() =>
            // The claim that used to be refused when the folder was assembled. Nothing about
            // loading these two together is wrong; only a document that cannot say which it
            // meant is.
            Assert.Equal(
                ["state", "twin"],
                Contested.Plugins
                    .Where(static plugin => plugin.ReservedPrefix == '$')
                    .Select(static plugin => plugin.Namespace)
                    .Order(StringComparer.Ordinal));

        [Fact]
        public void ABareShorthandIsRefusedWhereTheDocumentRequiresBothClaimants()
        {
            string message = Rejects(Contested, "$n", Twin);

            Assert.Contains("more than one vocabulary", message, StringComparison.Ordinal);
            Assert.Contains("state", message, StringComparison.Ordinal);
            Assert.Contains("twin", message, StringComparison.Ordinal);
        }

        [Fact]
        public void AQualifiedShorthandResolvesWhereTheBareFormIsAmbiguous() =>
            Assert.Equal(7, Read(Run(Contested, "$state:n", Twin)));

        [Fact]
        public void TheOtherClaimantIsReachableByItsOwnNamespace() =>
            // Whichever was loaded second is no less reachable than the first, which is the
            // whole of what not spending the character first-come comes to.
            Assert.Equal(1, Read(Run(Contested, "$twin:anything", Twin)));

        [Fact]
        public void RequiresDecidesABareShorthandBetweenTwoClaimants() =>
            // Twin is loaded and the document does not name it, so the bare form is not
            // ambiguous in this document — a rule set written before the second plugin
            // existed goes on building beside it.
            Assert.Equal(7, Read(Run(Contested, "$n")));

        [Fact]
        public void AShorthandOfAVocabularyTheDocumentDoesNotRequireIsRefused()
        {
            // Not the tie-break: one vocabulary reserves '$' here and the document still may
            // not write it, because 'requires' is what a rule set may draw on and a shorthand
            // is drawn on like anything else. Written out rather than run through the
            // template, which needs State for its own effect.
            string message = Assert.Throws<RuleSetBuildException>(() => Standard.CreateContext("""
                {
                  "id": "t", "version": "1.0.0",
                  "requires": [ { "plugin": "Rulealize.Plugin.TypeSchema" } ],
                  "state": { "schema": { "n": { "op": "type.int" } }, "initial": { "n": 7 } },
                  "inputs": { "go": { "when": "$n", "effects": [] } }
                }
                """)).Message;

            Assert.Contains("does not require", message, StringComparison.Ordinal);
            Assert.Contains("'state' is the one vocabulary that does", message, StringComparison.Ordinal);
        }

        [Fact]
        public void AQualifierNamingAVocabularyTheDocumentDoesNotRequireIsRefused() =>
            // The qualifier says which vocabulary, not whether the document may reach it.
            Assert.Contains(
                "does not name in 'requires'",
                Rejects(Contested, "$twin:anything"),
                StringComparison.Ordinal);

        private static string Document(string value, string more) =>
            Template
                .Replace("{{value}}", value, StringComparison.Ordinal)
                .Replace("{{more}}", more.Length == 0 ? string.Empty : $", {more}", StringComparison.Ordinal);

        private static string Run(RuleRuntime runtime, string value, string more = "") =>
            Go(runtime.CreateContext(Document(value, more)));

        private static string Rejects(RuleRuntime runtime, string value, string more = "") =>
            Assert.Throws<RuleSetBuildException>(() => runtime.CreateContext(Document(value, more))).Message;

        /// <summary>Runs the one input of a rule set built from a node written out in full.</summary>
        private static int Field(RuleRuntime runtime, string value) =>
            Read(Go(runtime.CreateContext(
                Template
                    .Replace("\"{{value}}\"", value, StringComparison.Ordinal)
                    .Replace("{{more}}", string.Empty, StringComparison.Ordinal))));

        private static string Go(RuleContext context) =>
            context.ApplyToState(
                $$"""
                { "$schema": "rulealize/input/v1", "ruleSet": "{{context.RuleSet}}", "input": "go", "args": {} }
                """,
                context.InitialState).State;

        private static int Read(string state) => Field(state, "m").GetInt32();

        private static JsonElement Field(string state, string name)
        {
            using JsonDocument document = JsonDocument.Parse(state);
            return document.RootElement.GetProperty("data").GetProperty(name).Clone();
        }

        /// <summary>A vocabulary that reserves '$' as well, which is now something it may do.</summary>
        /// <remarks>
        /// Its shorthand ignores the text and answers 1, so a test can tell which of the two
        /// expanded a literal from the state that came out.
        /// </remarks>
        private sealed class TwinPlugin : IRulealizePlugin
        {
            public PluginManifest Manifest { get; } = new("Twin", new Version(1, 0, 0), "twin", '$');

            public void Register(IPluginRegistry registry)
            {
                ArgumentNullException.ThrowIfNull(registry);
                registry.AddSugar(new OneExpander());
            }

            private sealed class OneNode : ExpressionNode
            {
                public override RuleValue Evaluate(IEvaluationContext context) => RuleValue.Number(1);
            }

            private sealed class OneExpander : ISugarExpander
            {
                public ExpressionNode Expand(IBuildContext context, string text) => new OneNode();
            }
        }
    }
}
