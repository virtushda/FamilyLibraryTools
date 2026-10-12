namespace FamilyLibraryTools.Core;

public enum DeletionState { Waiting, Verifying, Cancelling, Quarantining, Quarantined, Cancelled, Failed }

/// <summary>One explicit choice. Cancellation and the final quarantine handoff share a lock.</summary>
public sealed class PendingDeletion
{
    readonly object gate;
    readonly CancellationTokenSource cancellation = new();
    DeletionState state = DeletionState.Waiting;
    public MediaFile Keep { get; }
    public MediaFile Reject { get; }
    public DeletionState State { get { lock (gate) return state; } }
    public string Error { get; private set; } = "";
    public string Destination { get; private set; } = "";
    internal long Created { get; }
    public CancellationToken Token => cancellation.Token;
    public bool CanCancel => State is DeletionState.Waiting or DeletionState.Verifying;
    public bool IsActive => State is DeletionState.Waiting or DeletionState.Verifying or DeletionState.Cancelling or DeletionState.Quarantining;

    internal PendingDeletion(MediaFile keep, MediaFile reject, long created, object gate) => (Keep, Reject, Created, this.gate) = (keep, reject, created, gate);

    public bool Cancel()
    {
        lock (gate)
        {
            if (state is not (DeletionState.Waiting or DeletionState.Verifying)) return false;
            state = state == DeletionState.Waiting ? DeletionState.Cancelled : DeletionState.Cancelling;
            cancellation.Cancel();
            return true;
        }
    }

    internal bool BeginVerification()
    {
        lock (gate)
        {
            if (state != DeletionState.Waiting) return false;
            state = DeletionState.Verifying;
            return true;
        }
    }

    // After this boundary the atomic rename may finish; the file stays recoverable.
    internal void BeginQuarantine()
    {
        lock (gate)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (state != DeletionState.Verifying) throw new InvalidOperationException("This choice is not ready for quarantine.");
            state = DeletionState.Quarantining;
        }
    }

    internal void Finish(DeletionState result, string error = "", string destination = "")
    {
        lock (gate) { Error = error; Destination = destination; state = result; }
    }

    internal void Dispose() => cancellation.Dispose();
}

/// <summary>Session-only queue; choices never resume after restarting the application.</summary>
public sealed class DeletionQueue : IDisposable
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(60);
    readonly TimeProvider clock;
    readonly Func<PendingDeletion, Task<string>> quarantine;
    readonly List<PendingDeletion> entries = [];
    readonly Dictionary<MediaFile, int> membership = [];
    readonly object handoffGate = new();
    bool processing;
    public bool Stopped { get; private set; }
    public IReadOnlyList<PendingDeletion> Entries => entries.AsReadOnly();

    public DeletionQueue(IReadOnlyList<IReadOnlyList<MediaFile>> groups, Func<PendingDeletion, Task<string>> quarantine, TimeProvider? clock = null)
    {
        this.quarantine = quarantine;
        this.clock = clock ?? TimeProvider.System;
        for (int index = 0; index < groups.Count; index++)
            foreach (MediaFile file in groups[index]) membership.Add(file, index);
    }

    public PendingDeletion Enqueue(MediaFile keep, MediaFile reject)
    {
        if (Stopped) throw new InvalidOperationException("STOP ALL was pressed. Scan again to start a new review.");
        if (!membership.TryGetValue(keep, out int group) || !membership.TryGetValue(reject, out int other) || group != other ||
            keep.Path.Equals(reject.Path, StringComparison.OrdinalIgnoreCase) || keep.Identity == null || reject.Identity == null || keep.Identity == reject.Identity)
            throw new InvalidOperationException("Choose two distinct scanned files from the same duplicate group.");
        foreach (PendingDeletion entry in entries)
        {
            if (entry.State == DeletionState.Quarantined && (entry.Reject == keep || entry.Reject == reject))
                throw new InvalidOperationException("A selected file has already been quarantined. Scan again.");
            if (entry.IsActive && (entry.Keep == keep || entry.Keep == reject || entry.Reject == keep || entry.Reject == reject))
                throw new InvalidOperationException("One of these files already belongs to a pending choice. Cancel that choice first.");
        }
        var pending = new PendingDeletion(keep, reject, clock.GetTimestamp(), handoffGate);
        entries.Add(pending);
        return pending;
    }

    public TimeSpan Remaining(PendingDeletion entry)
    {
        TimeSpan remaining = GracePeriod - clock.GetElapsedTime(entry.Created);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public void StopAll()
    {
        lock (handoffGate)
        {
            Stopped = true;
            foreach (PendingDeletion entry in entries) entry.Cancel();
        }
    }

    // Called on the owning UI context. File work runs elsewhere; the UI remains able to cancel.
    public async Task ProcessDueAsync()
    {
        if (processing || Stopped) return;
        processing = true;
        try
        {
            for (int index = 0; index < entries.Count && !Stopped; index++)
            {
                PendingDeletion entry = entries[index];
                if (Remaining(entry) != TimeSpan.Zero || !entry.BeginVerification()) continue;
                try
                {
                    string destination = await quarantine(entry);
                    if (entry.State != DeletionState.Quarantining) throw new InvalidOperationException("No quarantine handoff was confirmed.");
                    entry.Finish(DeletionState.Quarantined, destination: destination);
                }
                catch (OperationCanceledException) when (entry.Token.IsCancellationRequested)
                {
                    entry.Finish(DeletionState.Cancelled);
                }
                catch (Exception error)
                {
                    entry.Finish(DeletionState.Failed, error.Message);
                    StopAll();
                }
            }
        }
        finally { processing = false; }
    }

    public void Dispose()
    {
        StopAll();
        if (processing) throw new InvalidOperationException("Wait for active file work before disposing the queue.");
        foreach (PendingDeletion entry in entries) entry.Dispose();
    }
}
