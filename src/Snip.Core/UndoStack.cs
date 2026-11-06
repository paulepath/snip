namespace Snip;

/// <summary>
/// Undo and redo over immutable versions: <see cref="Push"/> a new one, step back and forward. Remembers which
/// version was last saved, so <see cref="IsDirty"/> is false again after undoing back to it.
/// </summary>
public sealed class UndoStack<T>(T initial) where T : class
{
    readonly List<T> _back = [];
    readonly List<T> _forward = [];
    T? _saved = initial;

    public T Current { get; private set; } = initial;
    public bool CanUndo => _back.Count > 0;
    public bool CanRedo => _forward.Count > 0;
    public bool IsDirty => !ReferenceEquals(Current, _saved);

    /// <summary>Raised after <see cref="Current"/> changes.</summary>
    public event Action? Changed;

    /// <summary>A new version; the redo list is dropped. One equal to the current version is ignored.</summary>
    public void Push(T next)
    {
        if (ReferenceEquals(next, Current) || next.Equals(Current))
            return;
        _back.Add(Current);
        _forward.Clear();
        Current = next;
        Changed?.Invoke();
    }

    /// <summary>Replaces the current version without a new undo step (a drag in progress, say).</summary>
    public void Amend(T next)
    {
        Current = next;
        Changed?.Invoke();
    }

    public bool Undo()
    {
        if (_back.Count == 0) return false;
        _forward.Add(Current);
        Current = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (_forward.Count == 0) return false;
        _back.Add(Current);
        Current = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        Changed?.Invoke();
        return true;
    }

    public void MarkSaved() => _saved = Current;
}
