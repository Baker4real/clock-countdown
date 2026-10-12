namespace MinminCountdown.Core;

public static class NoteOrdering
{
    // Settings preserve insertion order, including in-place edits of existing notes.
    public static IReadOnlyList<NoteRecord> Sort(IReadOnlyList<NoteRecord> notes) =>
        notes.Select((note, index) => (Note: note, Index: index))
            .OrderBy(entry => entry.Note.IsCompleted)
            .ThenBy(entry => entry.Note.Date is null)
            .ThenByDescending(entry => entry.Note.Date, StringComparer.Ordinal)
            .ThenByDescending(entry => entry.Index)
            .Select(entry => entry.Note)
            .ToArray();
}
