using Remora.Rest.Core;

namespace MO9;

public class ThreadRepository
{
    private readonly Dictionary<Snowflake, bool> threads = new();

    public void AddThread(Snowflake snowflake)
    {
        if (HasThread(snowflake))
            return;

        threads.Add(snowflake, false);
    }

    public void RemoveThread(Snowflake snowflake)
    {
        threads.Remove(snowflake);
    }

    public void MarkThreadProcessed(Snowflake snowflake)
    {
        threads[snowflake] = true;
    }

    public bool HasThread(Snowflake snowflake)
    {
        return threads.ContainsKey(snowflake);
    }

    public bool HasProcessedThread(Snowflake snowflake)
    {
        return threads.TryGetValue(snowflake, out bool value) && value;
    }
}
