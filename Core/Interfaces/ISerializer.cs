namespace VirtualMemory.Interfaces;

public interface ISerializer<T>
{
    // Фиксированный размер элемента в байтах (например, для int – 4, для фиксированной строки – заданное число)
    int Size { get; }
    // Сериализует значение в предоставленный буфер (длина буфера равна Size)
    void Serialize(T value, Span<byte> destination);
    // Десериализует значение из буфера
    T Deserialize(ReadOnlySpan<byte> source);
}