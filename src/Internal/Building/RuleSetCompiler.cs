// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text.Json;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Building;
using Rulealize.Abstraction.Node;
using Rulealize.Abstraction.Plugin;
using Rulealize.Abstraction.Value;
using Rulealize.Internal.Plugin;
using Rulealize.Internal.RuleSet;

namespace Rulealize.Internal.Building
{
    /// <summary>Reads a rule set document and turns it into nodes.</summary>
    /// <remarks>
    /// <para>
    /// The core reserves eight keys — <c>$schema</c>, <c>id</c>, <c>version</c>,
    /// <c>requires</c>, <c>state</c>, <c>definitions</c>, <c>inputs</c>, <c>terminal</c> —
    /// plus <c>op</c> for telling a node from anything else. Everything inside a node is
    /// vocabulary, and this class hands it straight to whichever plugin claimed the name.
    /// </para>
    /// <para>
    /// Order matters. The schema is built first because paths resolve against it; every
    /// definition is declared before any body is built so that a definition may refer to one
    /// written after it; and the cycle check comes after all the bodies, once the reference
    /// graph is complete.
    /// </para>
    /// </remarks>
    internal sealed class RuleSetCompiler(OperationTable operations)
    {
        private static readonly SourcePath Root = SourcePath.Root;

        public CompiledRuleSet Compile(JsonElement document)
        {
            if (document.ValueKind != JsonValueKind.Object)
            {
                throw new RuleSetBuildException(Root, "a rule set must be a JSON object.");
            }

            string id = RequireString(document, "id", Root);
            string version = RequireString(document, "version", Root);

            CheckRequirements(document);

            StateSchema schema = new();
            DefinitionTable definitions = new();
            NodeBuilder builder = new(operations, schema, definitions);

            JsonElement state = RequireProperty(document, "state", Root);
            ImmutableArray<RuleValue> initial = CompileState(builder, schema, state);
            CompiledDefinitions compiledDefinitions = CompileDefinitions(builder, definitions, document);
            ImmutableArray<CompiledInput> inputs = CompileInputs(builder, document);
            CompiledTerminal? terminal = CompileTerminal(builder, document);

            return new CompiledRuleSet
            {
                Id = id,
                Version = version,
                Schema = schema,
                InitialState = initial,
                Definitions = compiledDefinitions,
                Inputs = inputs,
                Terminal = terminal
            };
        }

        private void CheckRequirements(JsonElement document)
        {
            if (!document.TryGetProperty("requires", out JsonElement requires))
            {
                return;
            }

            SourcePath path = Root.Append("requires");
            if (requires.ValueKind != JsonValueKind.Array)
            {
                throw new RuleSetBuildException(path, "must be an array.");
            }

            int index = 0;
            foreach (JsonElement entry in requires.EnumerateArray())
            {
                SourcePath entryPath = path.Append(index);
                index++;

                if (entry.ValueKind != JsonValueKind.Object)
                {
                    throw new RuleSetBuildException(entryPath, "must be an object naming a plugin.");
                }

                string plugin = RequireString(entry, "plugin", entryPath);
                if (!operations.TryGetPlugin(plugin, out PluginManifest? manifest))
                {
                    throw new RuleSetBuildException(
                        entryPath,
                        $"this rule set needs '{plugin}', which is not loaded.");
                }

                if (!entry.TryGetProperty("version", out JsonElement constraint))
                {
                    continue;
                }

                if (constraint.ValueKind != JsonValueKind.String
                    || !VersionRequirement.TryParse(constraint.GetString()!, out VersionRequirement? requirement))
                {
                    throw new RuleSetBuildException(
                        entryPath.Append("version"),
                        "must be a constraint of the form ^1.0, >=1.0 or 1.0.0.");
                }

                if (!requirement.Value.IsSatisfiedBy(manifest!.Version))
                {
                    throw new RuleSetBuildException(
                        entryPath.Append("version"),
                        $"this rule set needs {plugin} {requirement.Value}, but {manifest.Version} is loaded.");
                }
            }
        }

        private static ImmutableArray<RuleValue> CompileState(
            NodeBuilder builder,
            StateSchema schema,
            JsonElement state)
        {
            SourcePath statePath = Root.Append("state");
            if (state.ValueKind != JsonValueKind.Object)
            {
                throw new RuleSetBuildException(statePath, "must be an object with a 'schema' and an 'initial'.");
            }

            JsonElement fields = RequireProperty(state, "schema", statePath);
            SourcePath schemaPath = statePath.Append("schema");
            if (fields.ValueKind != JsonValueKind.Object)
            {
                throw new RuleSetBuildException(schemaPath, "must be an object mapping field names to schemas.");
            }

            builder.Scope.BeginFrame();
            foreach (JsonProperty field in fields.EnumerateObject())
            {
                SchemaNode node = builder.BuildSchema(field.Value, schemaPath.Append(field.Name));
                if (schema.Declare(field.Name, node) is null)
                {
                    throw new RuleSetBuildException(schemaPath, $"'{field.Name}' is declared more than once.");
                }
            }

            if (schema.FieldCount == 0)
            {
                throw new RuleSetBuildException(schemaPath, "must declare at least one field.");
            }

            JsonElement initial = RequireProperty(state, "initial", statePath);
            SourcePath initialPath = statePath.Append("initial");
            if (initial.ValueKind != JsonValueKind.Object)
            {
                throw new RuleSetBuildException(initialPath, "must be an object giving each field a value.");
            }

            SchemaViolations violations = new();
            ImmutableArray<RuleValue>.Builder values = ImmutableArray.CreateBuilder<RuleValue>(schema.FieldCount);
            foreach (StatePath field in schema.Fields)
            {
                if (initial.TryGetProperty(field.Text, out JsonElement value))
                {
                    values.Add(field.Schema.ReadJson(value, violations.For(field.Text)));
                }
                else
                {
                    violations.Add($"{field.Text}: is missing from state.initial.");
                    values.Add(RuleValue.Null);
                }
            }

            if (violations.Any)
            {
                throw new RuleSetBuildException(
                    initialPath,
                    $"does not satisfy state.schema.{Environment.NewLine}  "
                    + string.Join($"{Environment.NewLine}  ", violations.Messages));
            }

            return values.MoveToImmutable();
        }

        private static CompiledDefinitions CompileDefinitions(
            NodeBuilder builder,
            DefinitionTable table,
            JsonElement document)
        {
            if (!document.TryGetProperty("definitions", out JsonElement section))
            {
                return new CompiledDefinitions([], [], []);
            }

            SourcePath path = Root.Append("definitions");
            if (section.ValueKind != JsonValueKind.Object)
            {
                throw new RuleSetBuildException(path, "must be an object mapping names to definitions.");
            }

            // Every name first, so that a definition may refer to one written after it.
            List<(string Name, JsonElement Body, ImmutableArray<string> Parameters)> pending = [];
            foreach (JsonProperty entry in section.EnumerateObject())
            {
                SourcePath entryPath = path.Append(entry.Name);
                ImmutableArray<string> parameters = [];
                JsonElement body = entry.Value;

                // A 'body' key means the long form. Anything else is the body itself, so a
                // definition can be a node, a record, or a plain named constant.
                if (entry.Value.ValueKind == JsonValueKind.Object
                    && entry.Value.TryGetProperty("body", out JsonElement declared))
                {
                    body = declared;
                    if (entry.Value.TryGetProperty("params", out JsonElement declaredParameters))
                    {
                        parameters = ReadParameters(declaredParameters, entryPath.Append("params"));
                    }
                }
                else if (entry.Value.ValueKind == JsonValueKind.Object
                    && entry.Value.TryGetProperty("params", out _))
                {
                    throw new RuleSetBuildException(
                        entryPath,
                        "declares 'params' but no 'body'. Parameters need a body to be in scope over.");
                }

                if (table.Declare(entry.Name, parameters) is null)
                {
                    throw new RuleSetBuildException(entryPath, $"'{entry.Name}' is declared more than once.");
                }

                pending.Add((entry.Name, body, parameters));
            }

            ImmutableArray<ExpressionNode>.Builder bodies =
                ImmutableArray.CreateBuilder<ExpressionNode>(pending.Count);
            ImmutableArray<int>.Builder frameSizes = ImmutableArray.CreateBuilder<int>(pending.Count);
            ImmutableArray<ImmutableArray<LocalSlot>>.Builder slots =
                ImmutableArray.CreateBuilder<ImmutableArray<LocalSlot>>(pending.Count);

            for (int index = 0; index < pending.Count; index++)
            {
                (string name, JsonElement body, ImmutableArray<string> parameters) = pending[index];
                SourcePath bodyPath = path.Append(name);

                builder.Scope.BeginFrame();
                table.EnterDefinition(index);
                try
                {
                    using (builder.Scope.BeginScope())
                    {
                        // Parameters are the only thing a body can see from its caller, so
                        // they are declared into an otherwise empty frame.
                        ImmutableArray<LocalSlot>.Builder parameterSlots =
                            ImmutableArray.CreateBuilder<LocalSlot>(parameters.Length);

                        foreach (string parameter in parameters)
                        {
                            parameterSlots.Add(builder.Scope.Declare(parameter));
                        }

                        bodies.Add(builder.BuildExpression(body, bodyPath));
                        slots.Add(parameterSlots.MoveToImmutable());
                    }
                }
                finally
                {
                    table.EnterDefinition(-1);
                }

                frameSizes.Add(builder.Scope.FrameSize);
            }

            if (table.FindCycle() is ImmutableArray<string> cycle)
            {
                throw new RuleSetBuildException(
                    path,
                    $"definitions must not be recursive, but {string.Join(" → ", cycle)}.");
            }

            return new CompiledDefinitions(
                bodies.MoveToImmutable(),
                frameSizes.MoveToImmutable(),
                slots.MoveToImmutable());
        }

        private static ImmutableArray<CompiledInput> CompileInputs(NodeBuilder builder, JsonElement document)
        {
            JsonElement section = RequireProperty(document, "inputs", Root);
            SourcePath path = Root.Append("inputs");
            if (section.ValueKind != JsonValueKind.Object)
            {
                throw new RuleSetBuildException(path, "must be an object mapping names to inputs.");
            }

            ImmutableArray<CompiledInput>.Builder inputs = ImmutableArray.CreateBuilder<CompiledInput>();
            HashSet<string> seen = new(StringComparer.Ordinal);

            foreach (JsonProperty entry in section.EnumerateObject())
            {
                SourcePath entryPath = path.Append(entry.Name);
                if (entry.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new RuleSetBuildException(entryPath, "must be an object.");
                }

                if (!seen.Add(entry.Name))
                {
                    throw new RuleSetBuildException(entryPath, $"'{entry.Name}' is declared more than once.");
                }

                inputs.Add(CompileInput(builder, entry.Name, entry.Value, entryPath));
            }

            if (inputs.Count == 0)
            {
                throw new RuleSetBuildException(path, "must declare at least one input.");
            }

            return inputs.ToImmutable();
        }

        private static CompiledInput CompileInput(
            NodeBuilder builder,
            string name,
            JsonElement element,
            SourcePath path)
        {
            builder.Scope.BeginFrame();

            // Domains are built outside the parameter scope: a candidate is the product of
            // the domains, so no domain may depend on another parameter's value.
            List<(string Name, ExpressionNode Domain)> domains = [];
            if (element.TryGetProperty("params", out JsonElement parameters))
            {
                SourcePath parametersPath = path.Append("params");
                if (parameters.ValueKind != JsonValueKind.Object)
                {
                    throw new RuleSetBuildException(parametersPath, "must be an object mapping names to parameters.");
                }

                foreach (JsonProperty parameter in parameters.EnumerateObject())
                {
                    SourcePath parameterPath = parametersPath.Append(parameter.Name);
                    if (parameter.Value.ValueKind != JsonValueKind.Object)
                    {
                        throw new RuleSetBuildException(parameterPath, "must be an object with a 'domain'.");
                    }

                    JsonElement domain = RequireProperty(parameter.Value, "domain", parameterPath);
                    domains.Add((parameter.Name, builder.BuildExpression(domain, parameterPath.Append("domain"))));
                }
            }

            ExpressionNode? actor;
            ExpressionNode? guard;
            ImmutableArray<EffectNode> effects;
            ImmutableArray<CompiledParameter>.Builder compiled =
                ImmutableArray.CreateBuilder<CompiledParameter>(domains.Count);

            using (builder.Scope.BeginScope())
            {
                foreach ((string parameterName, ExpressionNode domain) in domains)
                {
                    compiled.Add(new CompiledParameter
                    {
                        Name = parameterName,
                        Slot = builder.Scope.Declare(parameterName),
                        Domain = domain
                    });
                }

                actor = element.TryGetProperty("actor", out JsonElement actorElement)
                    ? builder.BuildExpression(actorElement, path.Append("actor"))
                    : null;

                guard = element.TryGetProperty("when", out JsonElement whenElement)
                    ? builder.BuildExpression(whenElement, path.Append("when"))
                    : null;

                effects = CompileEffects(builder, element, path);
            }

            return new CompiledInput
            {
                Name = name,
                Parameters = compiled.MoveToImmutable(),
                Actor = actor,
                Guard = guard,
                Effects = effects,
                FrameSize = builder.Scope.FrameSize
            };
        }

        private static ImmutableArray<EffectNode> CompileEffects(
            NodeBuilder builder,
            JsonElement element,
            SourcePath path)
        {
            JsonElement effects = RequireProperty(element, "effects", path);
            SourcePath effectsPath = path.Append("effects");
            if (effects.ValueKind != JsonValueKind.Array)
            {
                throw new RuleSetBuildException(effectsPath, "must be an array of effects.");
            }

            ImmutableArray<EffectNode>.Builder nodes = ImmutableArray.CreateBuilder<EffectNode>();
            int index = 0;
            foreach (JsonElement effect in effects.EnumerateArray())
            {
                nodes.Add(builder.BuildEffect(effect, effectsPath.Append(index)));
                index++;
            }

            return nodes.ToImmutable();
        }

        private static CompiledTerminal? CompileTerminal(NodeBuilder builder, JsonElement document)
        {
            if (!document.TryGetProperty("terminal", out JsonElement section))
            {
                return null;
            }

            SourcePath path = Root.Append("terminal");
            if (section.ValueKind != JsonValueKind.Object)
            {
                throw new RuleSetBuildException(path, "must be an object with a 'when'.");
            }

            builder.Scope.BeginFrame();
            ExpressionNode when = builder.BuildExpression(RequireProperty(section, "when", path), path.Append("when"));
            ExpressionNode? result = section.TryGetProperty("result", out JsonElement resultElement)
                ? builder.BuildExpression(resultElement, path.Append("result"))
                : null;

            return new CompiledTerminal
            {
                When = when,
                Result = result,
                FrameSize = builder.Scope.FrameSize
            };
        }

        private static ImmutableArray<string> ReadParameters(JsonElement element, SourcePath path)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw new RuleSetBuildException(path, "must be an array of literal parameter names.");
            }

            ImmutableArray<string>.Builder names = ImmutableArray.CreateBuilder<string>();
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new RuleSetBuildException(path, "must contain literal strings only.");
                }

                string name = item.GetString()!;
                if (!seen.Add(name))
                {
                    throw new RuleSetBuildException(path, $"'{name}' is declared more than once.");
                }

                names.Add(name);
            }

            return names.ToImmutable();
        }

        private static JsonElement RequireProperty(JsonElement element, string name, SourcePath path) =>
            element.TryGetProperty(name, out JsonElement value)
                ? value
                : throw new RuleSetBuildException(path, $"needs a '{name}'.");

        private static string RequireString(JsonElement element, string name, SourcePath path)
        {
            JsonElement value = RequireProperty(element, name, path);
            return value.ValueKind == JsonValueKind.String
                ? value.GetString()!
                : throw new RuleSetBuildException(path.Append(name), "must be a literal string.");
        }
    }
}
