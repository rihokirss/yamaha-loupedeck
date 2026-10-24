namespace Yamaha.Core;

/// <summary>One writer, one accumulated target, at least 200 ms between sends.</summary>
public sealed class VolumePump : IDisposable
{
    private readonly object _sync = new();
    private readonly Receiver _receiver;
    private readonly CancellationTokenSource _stop = new();
    private int? _target;
    private long _version;
    private readonly Task _loop;
    public event Action<Exception>? Failed;
    public event Action? Changed;
    public int? PendingTarget { get { lock (_sync) return _target; } }
    public VolumePump(Receiver receiver) { _receiver = receiver; _loop = Task.Run(Loop); }
    public bool Adjust(int diff)
    {
        lock (_sync)
        {
            if (!_receiver.State.Available || !_receiver.State.Power) { _target = null; return false; }
            _target = Math.Clamp((_target ?? _receiver.State.Volume) + diff, 0, Math.Clamp(_receiver.Settings.MaxVolume, 0, 100));
            _version++;
        }
        Changed?.Invoke();
        return true;
    }
    public void Discard() { lock (_sync) { _target = null; _version++; } Changed?.Invoke(); }
    private async Task Loop()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                int? target; long version;
                lock (_sync) { target = _target; version = _version; }
                if (target == null) continue;
                try
                {
                    if (!_receiver.State.Available || !_receiver.State.Power) { Discard(); continue; }
                    await _receiver.SetVolumeAsync(target.Value);
                    lock (_sync) { if (version == _version) _target = null; }
                    Changed?.Invoke();
                }
                catch (Exception ex) { Discard(); Failed?.Invoke(ex); }
            }
        }
        catch (OperationCanceledException) { }
    }
    public async Task DrainAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            lock (_sync) { if (_target == null) return; }
            await Task.Delay(20);
        }
        throw new TimeoutException("Volume pump did not drain");
    }
    public void Dispose() { _stop.Cancel(); }
}
