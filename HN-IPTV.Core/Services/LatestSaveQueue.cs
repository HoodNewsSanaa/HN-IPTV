namespace HnIptv.Services;

// At most one active and one pending snapshot: a busy provider cannot build an
// unbounded queue of large catalog saves. A pending snapshot is always the newest.
public sealed class LatestSaveQueue<T>(Action<T> write) where T : class
{
    private readonly object _sync = new();
    private T? _pending;
    private Task _worker = Task.CompletedTask;
    private bool _running;
    public void Enqueue(T value)
    {
        lock (_sync)
        {
            _pending = value;
            if (!_running)
            {
                _running = true;
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _worker = completion.Task;
                _ = Task.Run(() => Drain(completion));
            }
        }
    }
    public Task FlushAsync() { lock (_sync) return _worker; }
    private void Drain(TaskCompletionSource completion)
    {
        Exception? failure = null;
        while (true)
        {
            T value;
            lock (_sync)
            {
                if (_pending == null)
                {
                    _running = false;
                    if (failure == null) completion.SetResult(); else completion.SetException(failure);
                    return;
                }
                value = _pending; _pending = null;
            }
            try { write(value); failure = null; }
            catch (Exception ex) { failure = ex; }
        }
    }
}
