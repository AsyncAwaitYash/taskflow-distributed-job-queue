namespace TaskFlow.Application.Jobs.Handlers;

/// <summary>
/// The single list of job types. The API accepts only these, and the worker runs only these.
/// Names are case-sensitive.
/// </summary>
public sealed class JobHandlerRegistry
{
    private readonly Dictionary<string, IJobHandler> _handlers = new(StringComparer.Ordinal);

    public JobHandlerRegistry(IEnumerable<IJobHandler> handlers)
    {
        foreach (IJobHandler handler in handlers)
        {
            if (!_handlers.TryAdd(handler.Type, handler))
            {
                throw new InvalidOperationException($"Two job handlers are registered for type '{handler.Type}'.");
            }
        }
    }

    public IReadOnlyCollection<string> Types => _handlers.Keys;

    public bool IsRegistered(string type)
    {
        return _handlers.ContainsKey(type);
    }

    public IJobHandler? Find(string type)
    {
        return _handlers.GetValueOrDefault(type);
    }
}
