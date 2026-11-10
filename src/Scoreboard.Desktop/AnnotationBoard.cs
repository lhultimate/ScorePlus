using System.Windows.Ink;

namespace Scoreboard.Desktop;

/// <summary>
/// The ink shared by every audience window for one display. The operator draws on one window and
/// the same strokes appear on the others, so the projector and the operator's copy cannot disagree.
/// The collection belongs to the show, not to a window; closing one output never discards the ink
/// another output is showing.
/// </summary>
public sealed class AnnotationBoard
{
    public StrokeCollection Strokes { get; } = new();

    /// <summary>Zero means permanent; otherwise at least half a second.</summary>
    public TimeSpan Hold { get; private set; } = TimeSpan.FromSeconds(1);

    /// <summary>True while a draw session is up on any window for this display.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>True between the first point and the mouse-up of a live stroke.</summary>
    public bool IsDrawing { get; private set; }

    public event Action? HoldChanged;
    public event Action? Dismissed;
    public event Action? StrokeStarted;
    public event Action? StrokeFinished;

    public AnnotationBoard(TimeSpan? hold = null) => Hold = Normalize(hold ?? TimeSpan.FromSeconds(1));

    public static TimeSpan Normalize(TimeSpan hold) => hold <= TimeSpan.Zero
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Math.Clamp(hold.TotalSeconds, 0.5, 10));

    public void SetHold(TimeSpan hold)
    {
        var normalized = Normalize(hold);
        if (normalized == Hold) return;
        Hold = normalized;
        HoldChanged?.Invoke();
    }

    public void Open() => IsOpen = true;

    /// <summary>Starts a live stroke on every window, and holds the fade until it is finished.</summary>
    public void BeginStroke()
    {
        if (IsDrawing) return;
        IsDrawing = true;
        StrokeStarted?.Invoke();
    }

    public void EndStroke()
    {
        if (!IsDrawing) return;
        IsDrawing = false;
        StrokeFinished?.Invoke();
    }

    public void Dismiss()
    {
        if (IsDrawing) EndStroke();
        if (!IsOpen && Strokes.Count == 0) return;
        IsOpen = false;
        Dismissed?.Invoke();
    }
}
