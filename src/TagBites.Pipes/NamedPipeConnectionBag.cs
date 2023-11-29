namespace TagBites.Pipes;

public class NamedPipeConnectionBag
{
    private IDictionary<string, object>? _cache;

    public object? this[string name]
    {
        get
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            return _cache?.TryGetValue(name, out var v) == true ? v : null;
        }
        set
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            _cache ??= new Dictionary<string, object>();

            if (value == null)
                _cache.Remove(name);
            else
                _cache[name] = value;
        }
    }
}
