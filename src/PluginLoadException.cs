// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize
{
    /// <summary>
    /// Thrown while plugins are being loaded: one that cannot be used, or a set that cannot be
    /// used together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two plugins claiming one identifier, or one operation namespace, are the cases about a
    /// set. Both are caught when the plugins are added rather than when a rule set first
    /// happens to use the contested name, because the fault is in the configuration and has
    /// nothing to do with any particular document.
    /// </para>
    /// <para>
    /// <b>A shorthand character is not one of them.</b> It is recorded rather than owned, so
    /// two plugins reserving one load together, and a rule set that would otherwise be
    /// ambiguous says which vocabulary it meant by naming it — <c>"$state:board"</c>. Nothing
    /// refuses the second claimant.
    /// </para>
    /// <para>
    /// The rest are about one plugin rather than a set: a folder or file that is not there, an
    /// assembly whose types cannot be read, a type that cannot be constructed, a manifest that
    /// is missing, and a registration that threw.
    /// </para>
    /// </remarks>
    public class PluginLoadException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="PluginLoadException"/> class.</summary>
        /// <param name="message">What is wrong.</param>
        public PluginLoadException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="PluginLoadException"/> class.</summary>
        /// <param name="message">What is wrong.</param>
        /// <param name="innerException">The underlying cause.</param>
        public PluginLoadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
