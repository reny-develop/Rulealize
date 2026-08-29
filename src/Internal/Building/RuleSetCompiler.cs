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
    /// The core reserves ten keys — <c>$schema</c>, <c>id</c>, <c>version</c>,
    /// <c>requires</c>, <c>uses</c>, <c>state</c>, <c>definitions</c>, <c>held</c>,
    /// <c>inputs</c>, <c>terminal</c> — plus <c>op</c> for telling a node from anything else.
    /// Everything inside a node is vocabulary, and this class hands it straight to whichever
    /// plugin claimed the name.
    /// </para>
    /// <para>
    /// Order matters. What <c>uses</c> names is compiled first, because a held rule set's
    /// state is part of this one's and everything after may read it; the schema next, because
    /// paths resolve against it; every definition is declared before any body is built so
    /// that a definition may refer to one written after it; and the cycle check comes after
    /// all the bodies, once the reference graph is complete.
    /// </para>
    /// </remarks>
    internal sealed class RuleSetCompiler(
        OperationTable operations,
        IReadOnlyDictionary<string, string>? heldDocuments = null,
        ImmutableArray<string> ancestry = default)
    {
        private static readonly SourcePath Root = SourcePath.Root;

        private readonly ImmutableArray<string> _ancestry = ancestry.IsDefault ? [] : ancestry;

        public CompiledRuleSet Compile(JsonElement document)
        {
            if (document.ValueKind != JsonValueKind.Object)
            {
                throw new RuleSetBuildException(Root, "a rule set must be a JSON object.");
            }

            OnlyTheseKeys(
                document,
                Root,
                "a rule set",
                "$schema", "id", "version", "requires", "uses", "state", "definitions", "held", "inputs", "terminal");

            string id = RequireString(document, "id", Root);
            string version = RequireString(document, "version", Root);

            ImmutableArray<string> required = CheckRequirements(document);

            StateSchema schema = new();
            DefinitionTable definitions = new();
            NodeBuilder builder = new(operations, required, schema, definitions);

            // The components are compiled before anything of the composite's own, because
            // their state is part of the composite's and everything after this may read it.
            ImmutableArray<RuleSetRequirement> uses = ReadUses(document);
            ImmutableArray<CompiledRuleSet> components = CompileComponents(uses, id);

            ImmutableArray<RuleValue> initial = CompileState(builder, schema, document, components.Length > 0);
            ImmutableArray<HeldRuleSet> held = DeclareHeld(uses, schema, components, ref initial);

            CompiledDefinitions compiledDefinitions = CompileDefinitions(builder, definitions, document);
            ImmutableArray<CompiledInput> inputs = CompileInputs(builder, document, held);
            CompiledTerminal? terminal = CompileTerminal(builder, document);
            held = CompileHeldConstraints(builder, document, held);

            return new CompiledRuleSet
            {
                Id = id,
                Version = version,
                Schema = schema,
                InitialState = initial,
                Definitions = compiledDefinitions,
                Inputs = inputs,
                Terminal = terminal,
                Held = held
            };
        }

        /// <summary>Compiles the rule set each <c>uses</c> entry names.</summary>
        /// <remarks>
        /// <para>
        /// A component is compiled on its own terms — its own <c>requires</c>, its own
        /// schema, its own definitions — and the composite gets the result. Nothing about the
        /// composite reaches into that compilation, which is what makes a component's meaning
        /// the same whether it is held or published alone.
        /// </para>
        /// <para>
        /// A cycle is refused here, naming the documents in it. Two rule sets holding each
        /// other is not a composition, and the state it would describe has no size.
        /// </para>
        /// </remarks>
        private ImmutableArray<CompiledRuleSet> CompileComponents(
            ImmutableArray<RuleSetRequirement> uses,
            string id)
        {
            if (uses.Length == 0)
            {
                return [];
            }

            ImmutableArray<CompiledRuleSet>.Builder components =
                ImmutableArray.CreateBuilder<CompiledRuleSet>(uses.Length);

            for (int index = 0; index < uses.Length; index++)
            {
                RuleSetRequirement entry = uses[index];
                SourcePath path = Root.Append("uses").Append(index);

                if (_ancestry.Contains(entry.RuleSet, StringComparer.Ordinal)
                    || string.Equals(entry.RuleSet, id, StringComparison.Ordinal))
                {
                    throw new RuleSetBuildException(
                        path,
                        $"'{entry.RuleSet}' holds itself, through "
                        + string.Join(" → ", _ancestry.Add(id).Add(entry.RuleSet)));
                }

                if (heldDocuments is null || !heldDocuments.TryGetValue(entry.RuleSet, out string? text))
                {
                    throw new RuleSetBuildException(
                        path,
                        $"this rule set holds '{entry.RuleSet}', whose document was not supplied. "
                        + "CreateContext takes the documents a rule set holds alongside its own.");
                }

                using JsonDocument component = RuleRuntime.Parse(text);
                CompiledRuleSet compiled;
                try
                {
                    compiled = new RuleSetCompiler(operations, heldDocuments, _ancestry.Add(id))
                        .Compile(component.RootElement);
                }
                catch (RuleSetBuildException exception)
                {
                    throw new RuleSetBuildException(
                        path,
                        $"'{entry.RuleSet}' does not compile: {exception.Message}",
                        exception);
                }

                if (!string.Equals(compiled.Id, entry.RuleSet, StringComparison.Ordinal))
                {
                    throw new RuleSetBuildException(
                        path.Append("ruleSet"),
                        $"names '{entry.RuleSet}', and the document supplied for it is '{compiled.Id}'.");
                }

                if (!Version.TryParse(compiled.Version, out Version? supplied)
                    || !entry.IsSatisfiedBy(supplied))
                {
                    throw new RuleSetBuildException(
                        path.Append("version"),
                        $"this rule set needs {entry}, but {compiled.Version} was supplied.");
                }

                components.Add(compiled);
            }

            return components.MoveToImmutable();
        }

        /// <summary>Reads <c>uses</c>, without compiling or fetching anything.</summary>
        /// <remarks>
        /// Separated from the compilation below for the reason <see cref="ReadRequirements"/> is:
        /// a tool works out which documents to fetch before it has them, and the two must read
        /// <c>^1.0</c> the same way. Reached from outside through
        /// <see cref="RuleSetRequirement.ReadFrom"/>.
        /// </remarks>
        internal static ImmutableArray<RuleSetRequirement> ReadUses(JsonElement document)
        {
            if (!document.TryGetProperty("uses", out JsonElement uses))
            {
                return [];
            }

            SourcePath path = Root.Append("uses");
            if (uses.ValueKind != JsonValueKind.Array)
            {
                throw new RuleSetBuildException(path, "must be an array.");
            }

            ImmutableArray<RuleSetRequirement>.Builder entries = ImmutableArray.CreateBuilder<RuleSetRequirement>();
            HashSet<string> aliases = new(StringComparer.Ordinal);

            int index = 0;
            foreach (JsonElement entry in uses.EnumerateArray())
            {
                SourcePath entryPath = path.Append(index);
                index++;

                if (entry.ValueKind != JsonValueKind.Object)
                {
                    throw new RuleSetBuildException(entryPath, "must be an object naming a rule set.");
                }

                OnlyTheseKeys(entry, entryPath, "a use", "ruleSet", "version", "as");

                string ruleSet = RequireString(entry, "ruleSet", entryPath);
                string alias = entry.TryGetProperty("as", out JsonElement named)
                    ? named.ValueKind == JsonValueKind.String
                        ? named.GetString()!
                        : throw new RuleSetBuildException(entryPath.Append("as"), "must be a literal string.")
                    : ruleSet;

                if (alias.Length == 0 || alias.Contains('.', StringComparison.Ordinal))
                {
                    throw new RuleSetBuildException(
                        entryPath.Append("as"),
                        "must be a name without '.', which separates a held rule set from its input.");
                }

                if (!aliases.Add(alias))
                {
                    throw new RuleSetBuildException(entryPath, $"'{alias}' is held more than once.");
                }

                VersionRequirement requirement = VersionRequirement.Any;
                string? constraint = null;
                if (entry.TryGetProperty("version", out JsonElement version))
                {
                    if (version.ValueKind != JsonValueKind.String
                        || !VersionRequirement.TryParse(version.GetString()!, out VersionRequirement? parsed))
                    {
                        throw new RuleSetBuildException(
                            entryPath.Append("version"),
                            "must be a constraint of the form ^1.0, >=1.0 or 1.0.0.");
                    }

                    requirement = parsed.Value;
                    constraint = version.GetString();
                }

                entries.Add(new RuleSetRequirement(ruleSet, alias, constraint, requirement));
            }

            return entries.ToImmutable();
        }

        /// <summary>Gives each held rule set the state field it occupies.</summary>
        /// <remarks>
        /// The field is not written in <c>state.schema</c> and is not given a value in
        /// <c>state.initial</c>: <c>uses</c> declares it, and it opens where the component
        /// itself opens. A composite that restated either would be a composite that could
        /// disagree with the document it holds.
        /// </remarks>
        private static ImmutableArray<HeldRuleSet> DeclareHeld(
            ImmutableArray<RuleSetRequirement> uses,
            StateSchema schema,
            ImmutableArray<CompiledRuleSet> components,
            ref ImmutableArray<RuleValue> initial)
        {
            if (components.Length == 0)
            {
                return [];
            }

            ImmutableArray<HeldRuleSet>.Builder held = ImmutableArray.CreateBuilder<HeldRuleSet>(components.Length);
            ImmutableArray<RuleValue>.Builder values = initial.ToBuilder();

            for (int index = 0; index < components.Length; index++)
            {
                HeldStateSchema node = new(components[index]);
                StatePath? field = schema.Declare(uses[index].Alias, node)
                    ?? throw new RuleSetBuildException(
                        Root.Append("uses").Append(index).Append("as"),
                        $"'{uses[index].Alias}' is already a field of this rule set's state.");

                values.Add(node.Pack(components[index].InitialState));
                held.Add(new HeldRuleSet
                {
                    Alias = uses[index].Alias,
                    Rules = components[index],
                    Field = field,
                    Constraints = []
                });
            }

            initial = values.ToImmutable();
            return held.MoveToImmutable();
        }

        /// <summary>Reads <c>requires</c>, without needing any plugin to be loaded.</summary>
        /// <param name="document">The rule set document.</param>
        /// <returns>One requirement per entry, in the order written.</returns>
        /// <remarks>
        /// Separated from the check below because a tool works out what to fetch before there
        /// is a runtime to check anything against, and the two must read <c>^1.0</c> the same
        /// way. Reached from outside through <see cref="PluginRequirement.ReadFrom"/>.
        /// </remarks>
        internal static ImmutableArray<PluginRequirement> ReadRequirements(JsonElement document)
        {
            if (document.ValueKind != JsonValueKind.Object)
            {
                throw new RuleSetBuildException(Root, "a rule set must be a JSON object.");
            }

            if (!document.TryGetProperty("requires", out JsonElement requires))
            {
                return [];
            }

            SourcePath path = Root.Append("requires");
            if (requires.ValueKind != JsonValueKind.Array)
            {
                throw new RuleSetBuildException(path, "must be an array.");
            }

            ImmutableArray<PluginRequirement>.Builder required = ImmutableArray.CreateBuilder<PluginRequirement>();

            int index = 0;
            foreach (JsonElement entry in requires.EnumerateArray())
            {
                SourcePath entryPath = path.Append(index);
                index++;

                if (entry.ValueKind != JsonValueKind.Object)
                {
                    throw new RuleSetBuildException(entryPath, "must be an object naming a plugin.");
                }

                OnlyTheseKeys(entry, entryPath, "a requirement", "plugin", "version");

                string plugin = RequireString(entry, "plugin", entryPath);

                if (!entry.TryGetProperty("version", out JsonElement constraint))
                {
                    required.Add(new PluginRequirement(plugin, null, VersionRequirement.Any));
                    continue;
                }

                if (constraint.ValueKind != JsonValueKind.String
                    || !VersionRequirement.TryParse(constraint.GetString()!, out VersionRequirement? requirement))
                {
                    throw new RuleSetBuildException(
                        entryPath.Append("version"),
                        "must be a constraint of the form ^1.0, >=1.0 or 1.0.0.");
                }

                required.Add(new PluginRequirement(plugin, constraint.GetString()!, requirement.Value));
            }

            return required.ToImmutable();
        }

        /// <summary>Holds every <c>requires</c> entry to a loaded plugin, and says which they are.</summary>
        /// <param name="document">The rule set document.</param>
        /// <returns>The namespace of each plugin named, in the order written.</returns>
        /// <remarks>
        /// The namespaces are what the builder needs to tell one shorthand from another when
        /// two vocabularies reserved the same character. Read here rather than there because
        /// this is where a requirement has already been resolved to the plugin it names.
        /// </remarks>
        private ImmutableArray<string> CheckRequirements(JsonElement document)
        {
            ImmutableArray<string>.Builder namespaces = ImmutableArray.CreateBuilder<string>();

            int index = 0;
            foreach (PluginRequirement required in ReadRequirements(document))
            {
                SourcePath entryPath = Root.Append("requires").Append(index);
                index++;

                if (!operations.TryGetPlugin(required.Plugin, out PluginManifest? manifest))
                {
                    throw new RuleSetBuildException(
                        entryPath,
                        $"this rule set needs '{required.Plugin}', which is not loaded.");
                }

                if (!required.IsSatisfiedBy(manifest!.Version))
                {
                    throw new RuleSetBuildException(
                        entryPath.Append("version"),
                        $"this rule set needs {required}, but {manifest.Version} is loaded.");
                }

                namespaces.Add(manifest.Namespace);
            }

            return namespaces.ToImmutable();
        }

        /// <summary>Compiles <c>state</c>, which a composite need not have one of its own.</summary>
        /// <param name="builder">The node builder.</param>
        /// <param name="schema">The schema to declare into.</param>
        /// <param name="document">The rule set document.</param>
        /// <param name="holding">Whether <c>uses</c> declared anything.</param>
        /// <returns>The opening value of each field the section declared.</returns>
        /// <remarks>
        /// A rule set that holds two others and constrains them is the shape composition was
        /// asked for, and it has no state and no inputs written down anywhere. Both sections
        /// are therefore optional exactly when <c>uses</c> is not empty, and required
        /// otherwise for the reason they always were: a state document is a public interface,
        /// so there has to be something to check one against.
        /// </remarks>
        private static ImmutableArray<RuleValue> CompileState(
            NodeBuilder builder,
            StateSchema schema,
            JsonElement document,
            bool holding)
        {
            if (!document.TryGetProperty("state", out JsonElement state))
            {
                if (holding)
                {
                    builder.Scope.BeginFrame();
                    return [];
                }

                throw new RuleSetBuildException(Root, "needs a 'state'.");
            }

            SourcePath statePath = Root.Append("state");
            if (state.ValueKind != JsonValueKind.Object)
            {
                throw new RuleSetBuildException(statePath, "must be an object with a 'schema' and an 'initial'.");
            }

            OnlyTheseKeys(state, statePath, "the state section", "schema", "initial");

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

            foreach (JsonProperty property in initial.EnumerateObject())
            {
                if (!schema.TryResolve(property.Name, out _))
                {
                    violations.Add($"{property.Name}: is not a field of the state schema.");
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
                    OnlyTheseKeys(entry.Value, entryPath, "a definition", "body", "params");

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
                    $"definitions must not be recursive, but {string.Join(" -> ", cycle)}.");
            }

            return new CompiledDefinitions(
                bodies.MoveToImmutable(),
                frameSizes.MoveToImmutable(),
                slots.MoveToImmutable());
        }

        private static ImmutableArray<CompiledInput> CompileInputs(
            NodeBuilder builder,
            JsonElement document,
            ImmutableArray<HeldRuleSet> held)
        {
            bool holding = held.Length > 0;

            if (!document.TryGetProperty("inputs", out JsonElement section))
            {
                if (holding)
                {
                    return [];
                }

                throw new RuleSetBuildException(Root, "needs a 'inputs'.");
            }

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

                // Reserved rather than used: a rule set that holds another offers its inputs
                // under a qualified name, and a name that could be either would make which
                // one a caller meant depend on what the document happens to declare.
                if (entry.Name.Contains('.', StringComparison.Ordinal))
                {
                    throw new RuleSetBuildException(
                        entryPath,
                        "an input's name may not contain '.', which separates a held rule set from its input.");
                }

                inputs.Add(CompileInput(builder, entry.Name, entry.Value, entryPath, held));
            }

            if (inputs.Count == 0 && !holding)
            {
                throw new RuleSetBuildException(path, "must declare at least one input.");
            }

            return inputs.ToImmutable();
        }

        private static CompiledInput CompileInput(
            NodeBuilder builder,
            string name,
            JsonElement element,
            SourcePath path,
            ImmutableArray<HeldRuleSet> held)
        {
            OnlyTheseKeys(element, path, "an input", "params", "actor", "when", "effects", "fires");

            builder.Scope.BeginFrame();
            int drawsBefore = builder.Draws;

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

                    OnlyTheseKeys(parameter.Value, parameterPath, "a parameter", "domain");

                    JsonElement domain = RequireProperty(parameter.Value, "domain", parameterPath);
                    domains.Add((parameter.Name, builder.BuildExpression(domain, parameterPath.Append("domain"))));
                }
            }

            ExpressionNode? actor;
            ExpressionNode? guard;
            ImmutableArray<EffectNode> effects;
            ImmutableArray<CompiledFire> fires;
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

                fires = CompileFires(builder, element, path, held);
                effects = CompileEffects(builder, element, path, fires.Length > 0);
            }

            return new CompiledInput
            {
                Name = name,
                Parameters = compiled.MoveToImmutable(),
                Actor = actor,
                Guard = guard,
                Effects = effects,
                Fires = fires,

                // A component input that draws makes the composite input that drives it one
                // that draws, because what it arrives at is not settled by the move alone.
                HasDraw = builder.Draws > drawsBefore || fires.Any(static fire => fire.Declared.HasDraw),
                FrameSize = builder.Scope.FrameSize
            };
        }

        /// <summary>Compiles <c>fires</c>: the held inputs one of the composite's own drives.</summary>
        /// <remarks>
        /// <para>
        /// Every argument the component's input takes has to be given, and nothing else may
        /// be. The expressions are built where the composite input's own parameters are in
        /// scope, and they read the state the transition found — a fired input is one that is
        /// legal <em>now</em>, not a step in a script, so two of them cannot depend on each
        /// other's writes and neither has to be guarded against the other's.
        /// </para>
        /// <para>
        /// A composite input that fires need not write anything itself, which is the ordinary
        /// case: <c>grant</c> is two component inputs and no effects of its own.
        /// </para>
        /// </remarks>
        private static ImmutableArray<CompiledFire> CompileFires(
            NodeBuilder builder,
            JsonElement element,
            SourcePath path,
            ImmutableArray<HeldRuleSet> held)
        {
            if (!element.TryGetProperty("fires", out JsonElement section))
            {
                return [];
            }

            SourcePath firesPath = path.Append("fires");
            if (section.ValueKind != JsonValueKind.Array)
            {
                throw new RuleSetBuildException(firesPath, "must be an array of held inputs to drive.");
            }

            ImmutableArray<CompiledFire>.Builder fires = ImmutableArray.CreateBuilder<CompiledFire>();
            int index = 0;
            foreach (JsonElement entry in section.EnumerateArray())
            {
                SourcePath entryPath = firesPath.Append(index);
                index++;

                if (entry.ValueKind != JsonValueKind.Object)
                {
                    throw new RuleSetBuildException(entryPath, "must be an object naming a held rule set's input.");
                }

                OnlyTheseKeys(entry, entryPath, "a fired input", "held", "input", "args");

                string alias = RequireString(entry, "held", entryPath);
                HeldRuleSet? target = held.FirstOrDefault(
                    candidate => string.Equals(candidate.Alias, alias, StringComparison.Ordinal))
                    ?? throw new RuleSetBuildException(
                        entryPath.Append("held"),
                        $"'{alias}' is not a rule set this one holds.");

                string inputName = RequireString(entry, "input", entryPath);
                CompiledInput declared = target.Rules.FindInput(inputName)
                    ?? throw new RuleSetBuildException(
                        entryPath.Append("input"),
                        $"'{inputName}' is not an input of '{target.Rules.Qualified}'.");

                fires.Add(new CompiledFire
                {
                    Alias = alias,
                    Name = $"{alias}.{inputName}",
                    Declared = declared,
                    Arguments = CompileFireArguments(builder, entry, entryPath, declared)
                });
            }

            return fires.ToImmutable();
        }

        private static ImmutableArray<ExpressionNode> CompileFireArguments(
            NodeBuilder builder,
            JsonElement entry,
            SourcePath path,
            CompiledInput declared)
        {
            SourcePath argsPath = path.Append("args");
            JsonElement args = default;
            bool written = entry.TryGetProperty("args", out args);

            if (written && args.ValueKind != JsonValueKind.Object)
            {
                throw new RuleSetBuildException(argsPath, "must be an object mapping parameter names to values.");
            }

            if (!written && declared.Parameters.Length > 0)
            {
                throw new RuleSetBuildException(
                    path,
                    $"'{declared.Name}' takes {declared.Parameters.Length} argument(s), and none are given.");
            }

            ImmutableArray<ExpressionNode>.Builder arguments =
                ImmutableArray.CreateBuilder<ExpressionNode>(declared.Parameters.Length);

            foreach (CompiledParameter parameter in declared.Parameters)
            {
                if (!written || !args.TryGetProperty(parameter.Name, out JsonElement value))
                {
                    throw new RuleSetBuildException(
                        argsPath,
                        $"'{declared.Name}' takes a '{parameter.Name}', and no value is given for it.");
                }

                arguments.Add(builder.BuildExpression(value, argsPath.Append(parameter.Name)));
            }

            if (written)
            {
                foreach (JsonProperty supplied in args.EnumerateObject())
                {
                    if (declared.FindParameter(supplied.Name) is null)
                    {
                        throw new RuleSetBuildException(
                            argsPath.Append(supplied.Name),
                            $"'{declared.Name}' has no parameter named '{supplied.Name}'.");
                    }
                }
            }

            return arguments.MoveToImmutable();
        }

        private static ImmutableArray<EffectNode> CompileEffects(
            NodeBuilder builder,
            JsonElement element,
            SourcePath path,
            bool firing)
        {
            if (firing && !element.TryGetProperty("effects", out _))
            {
                return [];
            }

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

        /// <summary>Compiles <c>held</c>: what the composite refuses of what it holds.</summary>
        /// <remarks>
        /// <para>
        /// Each entry is a guard on one of a held rule set's inputs, written in the
        /// composite's terms — the whole composed state, the composite's definitions — with
        /// that input's own parameters in scope. This is the guard that could not be written
        /// while the two halves were two documents, and putting it here rather than in an
        /// effect is what makes "a composite may only narrow" a fact about the format instead
        /// of a convention.
        /// </para>
        /// <para>
        /// A parameter is declared under the name the component gave it, so the guard says
        /// <c>@shift</c> where the component's own <c>when</c> does. The composite is reading
        /// the component's published interface and nothing further in.
        /// </para>
        /// </remarks>
        private static ImmutableArray<HeldRuleSet> CompileHeldConstraints(
            NodeBuilder builder,
            JsonElement document,
            ImmutableArray<HeldRuleSet> held)
        {
            if (!document.TryGetProperty("held", out JsonElement section))
            {
                return held;
            }

            SourcePath path = Root.Append("held");
            if (section.ValueKind != JsonValueKind.Object)
            {
                throw new RuleSetBuildException(path, "must be an object mapping a held rule set to its inputs.");
            }

            Dictionary<string, ImmutableArray<HeldConstraint>> byAlias = new(StringComparer.Ordinal);

            foreach (JsonProperty entry in section.EnumerateObject())
            {
                SourcePath aliasPath = path.Append(entry.Name);
                HeldRuleSet? target = held.FirstOrDefault(
                    candidate => string.Equals(candidate.Alias, entry.Name, StringComparison.Ordinal));

                if (target is null)
                {
                    throw new RuleSetBuildException(aliasPath, $"'{entry.Name}' is not a rule set this one holds.");
                }

                if (entry.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new RuleSetBuildException(aliasPath, "must be an object mapping input names to guards.");
                }

                if (byAlias.ContainsKey(entry.Name))
                {
                    throw new RuleSetBuildException(aliasPath, $"'{entry.Name}' is constrained more than once.");
                }

                ImmutableArray<HeldConstraint>.Builder constraints = ImmutableArray.CreateBuilder<HeldConstraint>();
                foreach (JsonProperty input in entry.Value.EnumerateObject())
                {
                    SourcePath inputPath = aliasPath.Append(input.Name);
                    CompiledInput? declared = target.Rules.FindInput(input.Name)
                        ?? throw new RuleSetBuildException(
                            inputPath,
                            $"'{input.Name}' is not an input of '{target.Rules.Qualified}'.");

                    if (input.Value.ValueKind != JsonValueKind.Object)
                    {
                        throw new RuleSetBuildException(inputPath, "must be an object with a 'when'.");
                    }

                    OnlyTheseKeys(input.Value, inputPath, "a held input", "when");

                    builder.Scope.BeginFrame();
                    ImmutableArray<LocalSlot>.Builder slots =
                        ImmutableArray.CreateBuilder<LocalSlot>(declared.Parameters.Length);
                    foreach (CompiledParameter parameter in declared.Parameters)
                    {
                        slots.Add(builder.Scope.Declare(parameter.Name));
                    }

                    ExpressionNode when = builder.BuildExpression(
                        RequireProperty(input.Value, "when", inputPath),
                        inputPath.Append("when"));

                    constraints.Add(new HeldConstraint
                    {
                        Input = input.Name,
                        When = when,
                        Never = when is LiteralNode literal && literal.Value.Equals(RuleValue.False),
                        ParameterSlots = slots.MoveToImmutable(),
                        FrameSize = builder.Scope.FrameSize
                    });
                }

                byAlias[entry.Name] = constraints.ToImmutable();
            }

            return
            [
                .. held.Select(one => byAlias.TryGetValue(one.Alias, out ImmutableArray<HeldConstraint> constraints)
                    ? new HeldRuleSet
                    {
                        Alias = one.Alias,
                        Rules = one.Rules,
                        Field = one.Field,
                        Constraints = constraints
                    }
                    : one)
            ];
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

            OnlyTheseKeys(section, path, "the terminal section", "when", "result");

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

        /// <summary>Refuses a key at a position whose keys the core fixes entirely.</summary>
        /// <param name="element">The object.</param>
        /// <param name="path">Where it is in the document.</param>
        /// <param name="what">What the position is, for the message.</param>
        /// <param name="allowed">Every key the core reads here.</param>
        /// <remarks>
        /// <para>
        /// Only where the core owns the whole key set — the sections it reserves, and the
        /// objects inside them that it reads itself. Never inside a node: there the keys
        /// belong to whichever plugin claimed the <c>op</c>, and a core that refused an
        /// unfamiliar one would make adding an argument to an operation a change to the
        /// runtime.
        /// </para>
        /// <para>
        /// Worth the check because most keys here are optional, and an optional key
        /// misspelled is not a document that fails: <c>whn</c> is an input with no guard,
        /// which is an input that is always legal. That is decidable from the document, so
        /// it is decided here rather than found in production.
        /// </para>
        /// </remarks>
        private static void OnlyTheseKeys(
            JsonElement element,
            SourcePath path,
            string what,
            params string[] allowed)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (allowed.Contains(property.Name, StringComparer.Ordinal))
                {
                    continue;
                }

                throw new RuleSetBuildException(
                    path.Append(property.Name),
                    $"is not a key {what} takes; those are {Listed(allowed)}.");
            }
        }

        private static string Listed(string[] names) =>
            names.Length is 1
                ? $"'{names[0]}'"
                : string.Join(", ", names[..^1].Select(static name => $"'{name}'")) + $" and '{names[^1]}'";
    }
}
