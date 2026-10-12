using System.ComponentModel;
using FamilyLibraryTools.Core;

namespace FamilyLibraryTools.Views;

public sealed class DeletionRow(PendingDeletion entry, List<MediaFile> group) : INotifyPropertyChanged
{
    public PendingDeletion Entry { get; } = entry;
    internal List<MediaFile> Group { get; } = group;
    internal bool Handled { get; set; }
    public string Description => $"Keep: {Entry.Keep.Name}    |    Quarantine: {Entry.Reject.Name}";
    public string Paths => $"Keep: {Entry.Keep.Path}\nQuarantine from: {Entry.Reject.Path}";
    public bool CanCancel => Entry.CanCancel;
    public string Status { get; private set; } = "Waiting 60 seconds";
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh(TimeSpan remaining)
    {
        Status = Entry.State switch
        {
            DeletionState.Waiting => $"Quarantine in {Math.Ceiling(remaining.TotalSeconds):0} seconds",
            DeletionState.Verifying => "Rechecking both files; cancellation is still available",
            DeletionState.Cancelling => "Cancelling; the file stays in its original location",
            DeletionState.Quarantining => "Moving to recoverable quarantine; this move can no longer be cancelled",
            DeletionState.Quarantined => "Recoverable file: " + Entry.Destination,
            DeletionState.Cancelled => "Cancelled; choose again to start a new 60-second countdown",
            _ => "Stopped: " + Entry.Error
        };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCancel)));
    }
}
