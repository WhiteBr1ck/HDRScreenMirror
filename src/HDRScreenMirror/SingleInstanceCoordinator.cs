namespace HDRScreenMirror;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = "Local\\WhiteBr1ck.HDRScreenMirror.Mutex";
    private const string ActivationEventName = "Local\\WhiteBr1ck.HDRScreenMirror.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly bool _ownsMutex;
    private RegisteredWaitHandle? _activationRegistration;
    private bool _disposed;

    public SingleInstanceCoordinator()
    {
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        _ownsMutex = createdNew;
    }

    public bool IsPrimary => _ownsMutex;

    public void SignalPrimary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _activationEvent.Set();
    }

    public void StartListening(Action activationAction)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(activationAction);
        if (!_ownsMutex)
            throw new InvalidOperationException("Only the primary instance can listen for activation requests.");
        if (_activationRegistration is not null)
            return;

        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                    activationAction();
            },
            null,
            Timeout.Infinite,
            false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _activationRegistration?.Unregister(null);
        if (_ownsMutex)
            _mutex.ReleaseMutex();
        _activationEvent.Dispose();
        _mutex.Dispose();
    }
}
