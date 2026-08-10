// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Plugin;
using Rulealize.Internal.Building;
using Rulealize.Internal.Plugin;

namespace Rulealize
{
    /// <summary>The set of plugins a rule set may be written against.</summary>
    /// <remarks>
    /// <para>
    /// A runtime is a vocabulary. It starts empty — the core provides no operations at all,
    /// not even booleans — and every name a rule set can use comes from a plugin loaded into
    /// it. That is why <c>requires</c> is worth reading: it lists what a rule set actually
    /// draws on, and it can do that only because the vocabulary is cut finely enough for the
    /// list to say something.
    /// </para>
    /// <para>
    /// Load plugins first, then compile rule sets. A context captures the operations
    /// available when it was created, so adding a plugin afterwards does not change one that
    /// already exists.
    /// </para>
    /// <example>
    /// <code>
    /// RuleRuntime runtime = new RuleRuntime()
    ///     .LoadPluginsFrom("plugin");
    ///
    /// RuleContext reversi = runtime.CreateContext(File.ReadAllText("reversi.json"));
    /// ValidInputSet moves = reversi.GetValidInputs(reversi.InitialState, validationLimit: 128);
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class RuleRuntime
    {
        /// <summary>
        /// How every document this runtime reads is parsed.
        /// </summary>
        /// <remarks>
        /// Comments and trailing commas are allowed. A rule set is a document a person
        /// writes and maintains, and one of any size needs somewhere to say why a rule is
        /// the way it is.
        /// </remarks>
        internal static readonly JsonDocumentOptions ParseOptions = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private readonly OperationTable _operations = new();

        /// <summary>Gets what each loaded plugin declares about itself, in load order.</summary>
        public ImmutableArray<PluginManifest> Plugins => _operations.Manifests;

        /// <summary>Adds one plugin.</summary>
        /// <param name="plugin">The plugin.</param>
        /// <returns>This runtime, so calls can be chained.</returns>
        /// <exception cref="PluginLoadException">
        /// Its identifier, its namespace, or the character it reserves is already claimed.
        /// </exception>
        /// <remarks>
        /// <para>
        /// This takes an instance, which is how an application adds vocabulary of its own
        /// without publishing it. A project using this library for its own rules will have
        /// operations worth writing and not worth packaging; implementing
        /// <see cref="IRulealizePlugin"/> in its own code and passing one here is the
        /// supported way to reach them, and it is the same contract a plugin on a feed
        /// signs — manifest, namespace claim, and a line in a rule set's <c>requires</c>.
        /// </para>
        /// <para>
        /// What an instance can do that <see cref="LoadPluginsFrom"/> cannot is arrive with
        /// something in it. A plugin discovered by scanning is built through its
        /// parameterless constructor, so a vocabulary that answers from a calendar, a rate
        /// table or an ownership map has nowhere to receive one; constructed here, it takes
        /// it as an argument.
        /// </para>
        /// <para>
        /// Whatever it carries must be an immutable snapshot, and every operation it
        /// registers must be a pure function of its arguments and that snapshot.
        /// <see cref="RuleContext.GetValidInputs(string, int, CancellationToken)"/> evaluates a guard once per
        /// candidate in a parameter's domain: an operation that reads a clock or a database
        /// turns a domain into a storm of queries and answers one question two ways inside a
        /// single call. Values that change belong in the state document — the current date
        /// is a state field passed to an operation, not something the operation finds out.
        /// </para>
        /// <para>
        /// Two conventions apply to a vocabulary that is never published. Vendor-qualify the
        /// identifier and the namespace, so a plugin released later cannot collide with it.
        /// And claim no reserved character: there is one per plugin and few that can ever be
        /// used, which makes them the wrong thing for a vocabulary with one consumer to
        /// spend.
        /// </para>
        /// </remarks>
        public RuleRuntime AddPlugin(IRulealizePlugin plugin)
        {
            ArgumentNullException.ThrowIfNull(plugin);

            PluginManifest manifest = plugin.Manifest
                ?? throw new PluginLoadException($"'{plugin.GetType().FullName}' has no manifest.");

            _operations.Claim(manifest);

            try
            {
                plugin.Register(new PluginRegistration(_operations, manifest));
            }
            catch (Exception exception) when (exception is not PluginLoadException)
            {
                throw new PluginLoadException($"{manifest.Id} failed while registering what it provides.", exception);
            }

            return this;
        }

        /// <summary>Adds every plugin an assembly contains.</summary>
        /// <param name="assembly">The assembly.</param>
        /// <returns>This runtime, so calls can be chained.</returns>
        public RuleRuntime LoadPlugins(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);

            foreach (IRulealizePlugin plugin in PluginProbe.Discover(assembly))
            {
                AddPlugin(plugin);
            }

            return this;
        }

        /// <summary>Adds every plugin found in an assembly file, or in a folder of them.</summary>
        /// <param name="path">A path to a DLL, or to a folder searched one level deep.</param>
        /// <returns>This runtime, so calls can be chained.</returns>
        /// <exception cref="PluginLoadException">A file could not be loaded as a plugin assembly.</exception>
        /// <remarks>
        /// <para>
        /// A named file was meant to be a plugin, so failing to read it is an error. A folder
        /// is swept speculatively — pointing this at an application's own output folder is a
        /// reasonable thing to do, and such a folder is full of assemblies that have nothing
        /// to do with this — so anything unreadable there is passed over.
        /// </para>
        /// <para>
        /// A plugin missed that way is not lost silently for long: the first rule set that
        /// wants it fails on its <c>requires</c>, naming it.
        /// </para>
        /// </remarks>
        public RuleRuntime LoadPluginsFrom(string path)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);

            if (File.Exists(path))
            {
                return LoadPlugins(PluginProbe.Load(path));
            }

            if (!Directory.Exists(path))
            {
                throw new PluginLoadException($"'{path}' is neither a file nor a folder.");
            }

            foreach (string file in PluginProbe.Assemblies(path))
            {
                if (PluginProbe.TryLoad(file) is not Assembly assembly)
                {
                    continue;
                }

                foreach (IRulealizePlugin plugin in PluginProbe.Discover(assembly, sweeping: true))
                {
                    AddPlugin(plugin);
                }
            }

            return this;
        }

        /// <summary>Compiles a rule set document.</summary>
        /// <param name="ruleSetDocument">The document.</param>
        /// <returns>A context that states can be run through.</returns>
        /// <exception cref="RuleSetBuildException">
        /// The document is not a valid rule set. Everything decidable from the document is
        /// decided here: unknown operations, missing keys, expressions where literals are
        /// required, unbound locals, undefined or cyclic definitions, and nodes used where
        /// their kind does not belong.
        /// </exception>
        public RuleContext CreateContext(string ruleSetDocument)
        {
            ArgumentNullException.ThrowIfNull(ruleSetDocument);

            using JsonDocument document = Parse(ruleSetDocument);
            return new RuleContext(new RuleSetCompiler(_operations).Compile(document.RootElement));
        }

        /// <summary>Compiles a rule set document read from a stream.</summary>
        /// <param name="ruleSetDocument">The document.</param>
        /// <param name="cancellationToken">Cancels reading.</param>
        /// <returns>A context that states can be run through.</returns>
        public async Task<RuleContext> CreateContextAsync(
            Stream ruleSetDocument,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(ruleSetDocument);

            JsonDocument document;
            try
            {
                document = await JsonDocument.ParseAsync(ruleSetDocument, ParseOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                throw new RuleSetBuildException(SourcePath.Root, "the rule set is not valid JSON.", exception);
            }

            using (document)
            {
                return new RuleContext(new RuleSetCompiler(_operations).Compile(document.RootElement));
            }
        }

        private static JsonDocument Parse(string json)
        {
            try
            {
                return JsonDocument.Parse(json, ParseOptions);
            }
            catch (JsonException exception)
            {
                throw new RuleSetBuildException(SourcePath.Root, "the rule set is not valid JSON.", exception);
            }
        }
    }
}
