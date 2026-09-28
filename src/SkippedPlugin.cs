// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize
{
    /// <summary>An assembly a folder sweep took for a plugin and could not use.</summary>
    /// <remarks>
    /// <para>
    /// A sweep is speculative — pointing it at an application's own output folder is a
    /// reasonable thing to do, and such a folder is full of assemblies that have nothing to do
    /// with this — so an unreadable one is passed over rather than thrown. That is right for a
    /// DLL that was never a plugin and wrong for one that was: the folder then holds a plugin
    /// nobody can see, and what surfaces instead is the <c>requires</c> of the first rule set
    /// that wanted it, complaining that a plugin is missing while the file sits in the folder.
    /// </para>
    /// <para>
    /// The two are told apart by what the assembly references. One that names
    /// <c>Rulealize.Abstraction</c> and whose types will not load was built to be a plugin, and
    /// is recorded here with the reason. One that does not is somebody else's DLL and is passed
    /// over in the silence that is right for it.
    /// </para>
    /// </remarks>
    /// <param name="File">The path the sweep read it from.</param>
    /// <param name="Reason">Why it could not be used, in a sentence.</param>
    public sealed record SkippedPlugin(string File, string Reason)
    {
        /// <inheritdoc />
        public override string ToString() => $"{File}: {Reason}";
    }
}
