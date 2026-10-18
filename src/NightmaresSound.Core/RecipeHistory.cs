namespace NightmaresSound.Core;

/// <summary>Immutable recipe snapshots; the UI groups gestures before committing.</summary>
public sealed class RecipeHistory
{
    private readonly List<SoundRecipe> undo = [];
    private readonly List<SoundRecipe> redo = [];
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public int UndoCount => undo.Count;

    public void Commit(SoundRecipe before, SoundRecipe after)
    {
        if (RecipeFile.Serialize(before) == RecipeFile.Serialize(after)) return;
        undo.Add(before);
        if (undo.Count > 100) undo.RemoveAt(0);
        redo.Clear();
    }

    public SoundRecipe Undo(SoundRecipe current)
    {
        if (!CanUndo) return current;
        redo.Add(current);
        var value = undo[^1]; undo.RemoveAt(undo.Count - 1);
        return value;
    }

    public SoundRecipe Redo(SoundRecipe current)
    {
        if (!CanRedo) return current;
        undo.Add(current);
        var value = redo[^1]; redo.RemoveAt(redo.Count - 1);
        return value;
    }
}
