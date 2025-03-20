using VirtualMemory.Interfaces;
using System.Text;

public class FixedStringSerializer : ISerializer<string>
{
    private readonly int fixedSize; // размер элемента в байтах

    public FixedStringSerializer(int fixedSize)
    {
        this.fixedSize = fixedSize;
    }

    public int Size => fixedSize;

    public void Serialize(string value, Span<byte> destination)
    {
        // Кодируем строку в Unicode (UTF-16)
        byte[] encoded = Encoding.Unicode.GetBytes(value);
        int count = Math.Min(encoded.Length, fixedSize);
        encoded.AsSpan(0, count).CopyTo(destination);
        if (count < fixedSize)
        {
            destination.Slice(count).Clear();
        }
    }

    public string Deserialize(ReadOnlySpan<byte> source)
    {
        // Получаем строку, затем удаляем завершающие нулевые символы
        return Encoding.Unicode.GetString(source).TrimEnd('\0');
    }
}
