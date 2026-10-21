// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Rulealize.Abstraction;
using Rulealize.Abstraction.Plugin;
using Rulealize.Abstraction.Store;
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
        private StoreBinding _stores = StoreBinding.None;
        private readonly List<SkippedPlugin> _skipped = [];

        /// <summary>Gets what each loaded plugin declares about itself, in load order.</summary>
        public ImmutableArray<PluginManifest> Plugins => _operations.Manifests;

        /// <summary>Gets what a folder sweep took for a plugin and could not use, in the order met.</summary>
        /// <remarks>
        /// Empty in the ordinary case. An entry means the folder holds an assembly built
        /// against this abstraction whose types would not load, which is very nearly always a
        /// plugin built against a different version of it. Without this the symptom is a rule
        /// set failing on a <c>requires</c> that names a plugin whose file is sitting in the
        /// folder, which sends a reader looking in the wrong place.
        /// </remarks>
        public ImmutableArray<SkippedPlugin> Skipped => [.. _skipped];

        /// <summary>Gets every operation the loaded plugins provide, in registration order.</summary>
        /// <remarks>
        /// <para>
        /// <see cref="Plugins"/> says which vocabularies are loaded; this says what is in
        /// them. Nothing here is declared anywhere — a plugin's operations exist only as the
        /// calls it made while registering, so this is the sole account of what a runtime can
        /// do, and it cannot disagree with what a rule set will be compiled against.
        /// </para>
        /// <para>
        /// A name registered as two kinds appears twice; see <see cref="OperationDescriptor"/>.
        /// </para>
        /// </remarks>
        public ImmutableArray<OperationDescriptor> Operations => _operations.Operations;

        /// <summary>Adds one plugin.</summary>
        /// <param name="plugin">The plugin.</param>
        /// <returns>This runtime, so calls can be chained.</returns>
        /// <exception cref="PluginLoadException">
        /// Its identifier or its namespace is already claimed. A reserved character is not
        /// among the things that can be taken: two plugins may reserve one and load together,
        /// and which of them a rule set meant is settled per document.
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
        /// What that costs is worth knowing before choosing it.
        /// <see cref="RuleContext.GetValidInputs(string, int, CancellationToken)"/> evaluates a guard once per
        /// candidate in a parameter's domain, so an operation that reads a clock or a
        /// database reads it once per candidate; and snapshot semantics cover the state
        /// document, so anything else an operation reads may answer one question two ways
        /// inside a single call. Neither is refused. An immutable snapshot and arguments —
        /// the current date as a state field rather than something the operation finds out —
        /// is the way around both, and the choice belongs to whoever writes the vocabulary.
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
        /// One that was built to be a plugin is passed over but not in silence: it is recorded
        /// in <see cref="Skipped"/>, with the reason. Without that the folder holds a plugin
        /// nobody can see, and what surfaces is a rule set complaining that it is missing while
        /// the file sits in the folder.
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

                string named = file;
                IEnumerable<IRulealizePlugin> found = PluginProbe.Discover(
                    assembly,
                    sweeping: true,
                    passedOver: reason => _skipped.Add(new SkippedPlugin(named, reason)));

                foreach (IRulealizePlugin plugin in found)
                {
                    AddPlugin(plugin);
                }
            }

            return this;
        }

        /// <summary>Gives the runtime a store a host opened, for the rule sets that name its plugin.</summary>
        /// <param name="plugin">The store plugin's identifier, as a rule set's <c>requires</c> names it.</param>
        /// <param name="store">The store, opened: connected, and ready for a session.</param>
        /// <returns>This runtime, so calls can be chained.</returns>
        /// <exception cref="InvalidOperationException">
        /// No plugin of that identifier is loaded, it is not a store, or this is not the kind of
        /// store it declared and not a <see cref="MemoryStateStore"/>, which stands in for any.
        /// </exception>
        /// <remarks>
        /// <para>
        /// A rule set whose <c>uses</c> declares a <c>many</c> keeps every state document in the
        /// store its <c>requires</c> names, its own included, and the runtime reads and writes
        /// them through what is given here. The plugin says what a store is; opening one, with
        /// whatever connection it takes, is the host's, and handing it over is the whole of what
        /// the host does with documents.
        /// </para>
        /// <para>
        /// Taken when a context is created, as the plugins are, so give the stores before
        /// <see cref="CreateContext(string)"/>. Giving another for the same plugin replaces it for
        /// the contexts created afterwards.
        /// </para>
        /// </remarks>
        public RuleRuntime UseStore(string plugin, IStateStore store)
        {
            ArgumentException.ThrowIfNullOrEmpty(plugin);
            ArgumentNullException.ThrowIfNull(store);

            if (!_operations.TryGetPlugin(plugin, out PluginManifest? manifest))
            {
                throw new InvalidOperationException($"'{plugin}' is not loaded, so there is no store of it to use.");
            }

            if (!_operations.TryGetStore(manifest!.Id, out Type? declared))
            {
                throw new InvalidOperationException($"{manifest.Id} is not a store.");
            }

            // A store kept in memory stands in for any: evaluation is pure, so where the
            // documents are kept changes no answer.
            if (store is not MemoryStateStore && !declared!.IsInstanceOfType(store))
            {
                throw new InvalidOperationException(
                    $"{manifest.Id} keeps documents in a '{declared.FullName}', and this is a '{store.GetType().FullName}'.");
            }

            _stores = _stores with { Stores = _stores.Stores.SetItem(manifest.Id, store) };
            return this;
        }

        /// <summary>Keeps the documents of every store a rule set names in memory, in place of the stores themselves.</summary>
        /// <param name="store">The store to keep them in.</param>
        /// <returns>This runtime, so calls can be chained.</returns>
        /// <remarks>
        /// <para>
        /// For walking a rule set, or testing a host, without the store its <c>requires</c>
        /// names: evaluation is pure, so where the documents are kept changes no answer. The
        /// store plugins are still loaded, because a rule set is built against them, and a store
        /// given to <see cref="UseStore(string, IStateStore)"/> for one of them is still used for
        /// that one.
        /// </para>
        /// <para>
        /// Every store plugin kept here is one store, so a transition that writes to two of them
        /// is committed through one session and no coordinator is asked. Taken when a context is
        /// created, as the stores are.
        /// </para>
        /// </remarks>
        public RuleRuntime UseStore(MemoryStateStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            _stores = _stores with { Memory = store };
            return this;
        }

        /// <summary>Gives the runtime what keeps a transition that writes to more than one store.</summary>
        /// <param name="coordinator">The coordinator.</param>
        /// <returns>This runtime, so calls can be chained.</returns>
        /// <remarks>
        /// Taken when a context is created, as the stores are. Without one, a transition that
        /// writes to more than one store fails rather than commit part of itself.
        /// </remarks>
        public RuleRuntime UseCoordinator(IStoreCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(coordinator);

            _stores = _stores with { Coordinator = coordinator };
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
        public RuleContext CreateContext(string ruleSetDocument) => CreateContext(ruleSetDocument, null);

        /// <summary>Compiles a rule set document, together with the documents it holds.</summary>
        /// <param name="ruleSetDocument">The document.</param>
        /// <param name="heldDocuments">
        /// The document of every rule set reachable through <c>uses</c>, by identifier.
        /// Anything a component holds in turn is looked up here too.
        /// </param>
        /// <returns>A context that states can be run through.</returns>
        /// <exception cref="RuleSetBuildException">
        /// The document is not a valid rule set, a document it holds was not supplied or does
        /// not satisfy the version its holder asks for, or the rule sets hold one another.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The one thing composition adds to this class's surface.
        /// <see cref="RuleContext.ApplyToState(string, string, CancellationToken)"/>,
        /// <see cref="RuleContext.GetValidInputs(string, int, CancellationToken)"/>,
        /// <see cref="RuleContext.GetOutcomes(string, string, int, CancellationToken)"/> and
        /// <see cref="RuleContext.GetTerminalStatus(string, CancellationToken)"/> are
        /// unchanged, and so is what a case is: one state document, one string, storable in a
        /// column and resumable on another machine. The exception is a <c>uses</c> entry with a
        /// <c>many</c>, whose instances are documents of their own; the overload taking start
        /// documents is the one such a rule set is compiled with.
        /// </para>
        /// <para>
        /// A dictionary rather than a callback, because fetching a document is somebody
        /// else's business for the reason fetching a plugin is —
        /// <see cref="PluginResolution"/> says the whole of it. What a rule set holds is
        /// readable from its <c>uses</c> before there is a runtime, the same way
        /// <c>requires</c> is.
        /// </para>
        /// </remarks>
        public RuleContext CreateContext(
            string ruleSetDocument,
            IReadOnlyDictionary<string, string>? heldDocuments) =>
            CreateContext(ruleSetDocument, heldDocuments, null);

        /// <summary>Compiles a rule set document, the documents it holds, and the states its instances start at.</summary>
        /// <param name="ruleSetDocument">The document.</param>
        /// <param name="heldDocuments">
        /// The document of every rule set reachable through <c>uses</c>, by identifier.
        /// </param>
        /// <param name="startDocuments">
        /// The state document each <c>many</c> names as its <c>start</c>, by that name.
        /// </param>
        /// <returns>A context that states can be run through.</returns>
        /// <exception cref="RuleSetBuildException">
        /// The document is not a valid rule set, a document it holds or starts an instance at
        /// was not supplied, or one that was is not one the rule set can take.
        /// </exception>
        /// <remarks>
        /// <para>
        /// A rule set that keeps instances says where a new one starts, by name, rather than
        /// writing the state out a second time: who may sign and what kinds of expense there
        /// are belong to no rule set, so they are said in one state document and handed in
        /// here, on the terms a held document is.
        /// </para>
        /// <para>
        /// The document is read when the context is built — its <c>ruleSet</c> checked against
        /// what <c>uses</c> asks for, its data against the component's schema — so a start
        /// that could not be a state of the component is refused before any instance is made
        /// from it.
        /// </para>
        /// </remarks>
        public RuleContext CreateContext(
            string ruleSetDocument,
            IReadOnlyDictionary<string, string>? heldDocuments,
            IReadOnlyDictionary<string, string>? startDocuments)
        {
            ArgumentNullException.ThrowIfNull(ruleSetDocument);

            using JsonDocument document = Parse(ruleSetDocument);
            return new RuleContext(
                new RuleSetCompiler(_operations, heldDocuments, startDocuments: startDocuments)
                    .Compile(document.RootElement),
                _stores);
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
                return new RuleContext(new RuleSetCompiler(_operations).Compile(document.RootElement), _stores);
            }
        }

        internal static JsonDocument Parse(string json)
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
