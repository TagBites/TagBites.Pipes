using System.Collections.Concurrent;

namespace TagBites.Pipes;

/// <summary>
/// Stores values that live as long as one connection, keyed by name.
/// </summary>
/// <remarks>Safe for concurrent use.</remarks>
[PublicAPI]
public class NamedPipeConnectionBag
{
    private readonly ConcurrentDictionary<string, object> _cache = new();

    /// <summary>
    /// Gets or sets the value stored under the given name.
    /// </summary>
    /// <remarks>Assigning <c>null</c> removes the entry. Reading a name that was never set returns <c>null</c>.</remarks>
    public object? this[string name]
    {
        get
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            return _cache.TryGetValue(name, out var value) ? value : null;
        }
        set
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            if (value == null)
                _cache.TryRemove(name, out _);
            else
                _cache[name] = value;
        }
    }
}
