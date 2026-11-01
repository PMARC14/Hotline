namespace Hotline.Core.Text;

/// <summary>Read-only list with value equality (so records containing lists compare structurally).</summary>
public sealed class Seq<T>(IEnumerable<T> items) : IReadOnlyList<T>, IEquatable<IReadOnlyList<T>>
{
    private readonly T[] _items = items.ToArray();
    public T this[int index] => _items[index];
    public int Count => _items.Length;
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
    public bool Equals(IReadOnlyList<T>? other) => other is not null && this.SequenceEqual(other);
    public override bool Equals(object? obj) => obj is IReadOnlyList<T> other && Equals(other);
    public override int GetHashCode() => _items.Aggregate(0, (h, x) => HashCode.Combine(h, x));
}
