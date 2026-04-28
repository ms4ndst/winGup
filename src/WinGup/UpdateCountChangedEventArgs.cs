namespace WinGup;

/// <summary>Event args carrying the new update count.</summary>
public sealed class UpdateCountChangedEventArgs(int count) : EventArgs
{
    /// <summary>Gets the new number of available updates.</summary>
    public int Count { get; } = count;
}
