// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

namespace Rulealize
{
    /// <summary>
    /// Thrown while plugins are being loaded, when the set of them cannot be used together.
    /// </summary>
    /// <remarks>
    /// Two plugins claiming one operation namespace, or one shorthand character, are the
    /// cases this exists for. Both are caught when the plugins are added rather than when a
    /// rule set first happens to use the contested name, because the fault is in the
    /// configuration and has nothing to do with any particular document.
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
