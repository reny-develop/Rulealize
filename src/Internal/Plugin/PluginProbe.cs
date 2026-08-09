// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Reflection;
using Rulealize.Abstraction.Plugin;

namespace Rulealize.Internal.Plugin
{
    /// <summary>Finds the plugins in an assembly or a folder of assemblies.</summary>
    /// <remarks>
    /// <para>
    /// A plugin is a public, concrete implementation of <see cref="IRulealizePlugin"/> with a
    /// parameterless constructor. Nothing else identifies one — no attribute, no naming
    /// convention, no manifest file beside the DLL — because the interface is already the
    /// contract and a second declaration could only disagree with it.
    /// </para>
    /// <para>
    /// Assemblies are loaded into the default context so that the
    /// <c>Rulealize.Abstraction</c> a plugin was compiled against resolves to the one
    /// already in memory. Without that unification a plugin's <c>ExpressionNode</c> would be
    /// a different type from the runtime's, and every registration would fail to bind.
    /// </para>
    /// <para>
    /// Naming a file and scanning a folder are held to different standards. A named file was
    /// meant to be a plugin, so failing to read it is an error. A folder is swept
    /// speculatively — an application's own output folder is a reasonable thing to point at,
    /// and it is full of assemblies that have nothing to do with this — so anything
    /// unreadable there is passed over. A plugin missed that way still surfaces, as the
    /// <c>requires</c> of the first rule set that wanted it.
    /// </para>
    /// </remarks>
    internal static class PluginProbe
    {
        /// <summary>Instantiates every plugin an assembly contains.</summary>
        /// <param name="assembly">The assembly to search.</param>
        /// <param name="sweeping">Whether this assembly turned up in a folder sweep.</param>
        /// <returns>One instance per implementation found, in a stable order.</returns>
        /// <exception cref="PluginLoadException">
        /// The assembly's types could not be read, or a plugin type could not be
        /// instantiated. Unreadable types are passed over while sweeping.
        /// </exception>
        public static IEnumerable<IRulealizePlugin> Discover(Assembly assembly, bool sweeping = false)
        {
            ArgumentNullException.ThrowIfNull(assembly);

            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                if (!sweeping)
                {
                    throw new PluginLoadException(
                        $"The types of '{assembly.GetName().Name}' could not be read. It was most likely built against "
                        + "a different version of Rulealize.Abstraction.",
                        exception);
                }

                // Some types failed to load; whatever did load may still hold a plugin.
                types = exception.Types;
            }

            IEnumerable<Type> candidates = types
                .Where(static type => type is not null && IsPlugin(type))
                .Select(static type => type!)
                .OrderBy(static type => type.FullName, StringComparer.Ordinal);

            foreach (Type type in candidates)
            {
                IRulealizePlugin plugin;
                try
                {
                    plugin = (IRulealizePlugin)Activator.CreateInstance(type)!;
                }
                catch (Exception exception) when (exception is not PluginLoadException)
                {
                    throw new PluginLoadException($"'{type.FullName}' could not be constructed.", exception);
                }

                yield return plugin;
            }
        }

        /// <summary>Loads an assembly that was named as a plugin.</summary>
        /// <param name="path">The path to the DLL.</param>
        /// <returns>The assembly.</returns>
        /// <exception cref="PluginLoadException">The file could not be loaded.</exception>
        public static Assembly Load(string path) =>
            TryLoad(path) ?? throw new PluginLoadException($"'{path}' could not be loaded as a plugin assembly.");

        /// <summary>Loads an assembly that turned up in a folder sweep.</summary>
        /// <param name="path">The path to the DLL.</param>
        /// <returns>The assembly, or <see langword="null"/> when it is not a managed assembly this host can read.</returns>
        public static Assembly? TryLoad(string path)
        {
            try
            {
                return Assembly.LoadFrom(Path.GetFullPath(path));
            }
            catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or IOException)
            {
                return null;
            }
        }

        /// <summary>Lists the candidate plugin assemblies in a folder.</summary>
        /// <param name="directory">The folder to search, not recursively.</param>
        /// <returns>The DLL paths, in a stable order.</returns>
        public static IEnumerable<string> Assemblies(string directory) =>
            Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(static path => path, StringComparer.Ordinal);

        private static bool IsPlugin(Type type) =>
            type is { IsAbstract: false, IsInterface: false, IsPublic: true }
            && typeof(IRulealizePlugin).IsAssignableFrom(type)
            && type.GetConstructor(Type.EmptyTypes) is not null;
    }
}
