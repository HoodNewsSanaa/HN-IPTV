namespace HnIptv.Services;

// Bounded EPG concurrency with promotion of a queued visible-row request when
// that channel becomes selected. No duplicate network request is needed.
public sealed class PriorityWorkGate(int concurrency)
{
    private readonly object _sync = new();
    private readonly LinkedList<Waiter> _queue = new();
    private int _available = concurrency > 0 ? concurrency : throw new ArgumentOutOfRangeException(nameof(concurrency));
    private sealed class Waiter(string key)
    {
        public string Key { get; } = key;
        public TaskCompletionSource<IDisposable> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<Waiter>? Node;
        public CancellationTokenRegistration Registration;
    }
    public Task<IDisposable> EnterAsync(string key, bool priority, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_available > 0) { _available--; return Task.FromResult<IDisposable>(new Lease(this)); }
            var waiter = new Waiter(key);
            waiter.Node = priority ? _queue.AddFirst(waiter) : _queue.AddLast(waiter);
            waiter.Registration = token.Register(() =>
            {
                lock (_sync)
                {
                    if (waiter.Node?.List != null) _queue.Remove(waiter.Node);
                    waiter.Ready.TrySetCanceled(token);
                }
            });
            return waiter.Ready.Task;
        }
    }
    public void Promote(string key)
    {
        lock (_sync)
        {
            for (var node = _queue.First; node != null; node = node.Next)
                if (node.Value.Key == key) { _queue.Remove(node); _queue.AddFirst(node); break; }
        }
    }
    private void Release()
    {
        Waiter? next;
        lock (_sync)
        {
            next = _queue.First?.Value;
            if (next == null) { _available++; return; }
            _queue.RemoveFirst();
        }
        next.Registration.Unregister();
        var lease = new Lease(this);
        if (!next.Ready.TrySetResult(lease)) lease.Dispose();
    }
    private sealed class Lease(PriorityWorkGate owner) : IDisposable
    {
        private PriorityWorkGate? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
