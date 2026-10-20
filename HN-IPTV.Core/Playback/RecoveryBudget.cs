namespace HnIptv.Playback;

// One budget per user tune. Native errors and stalled progress share this budget.
public sealed class RecoveryBudget
{
    public int Attempts { get; private set; }
    public void Reset() => Attempts = 0;
    public TimeSpan? NextDelay() => Attempts < 3 ? TimeSpan.FromSeconds(1 << Attempts++) : null;
}
