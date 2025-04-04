namespace VirtualMemory.Interfaces;

public interface ISerializer<T>
{
    int Size { get; }
    void Serialize(T value, Span<byte> destination);
    T Deserialize(ReadOnlySpan<byte> source);
}