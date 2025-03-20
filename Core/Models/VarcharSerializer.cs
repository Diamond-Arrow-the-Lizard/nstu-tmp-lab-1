using System;
using System.Text;
using VirtualMemory.Interfaces;

public class VarcharSerializer : ISerializer<string>
{
    private readonly int maxAllocatedBytes;

    // maxAllocatedBytes = 4 + (maxLength * 2)
    public VarcharSerializer(int maxAllocatedBytes)
    {
        if (maxAllocatedBytes < 4)
            throw new ArgumentException("Размер блока должен быть не менее 4 байт для хранения длины строки.");
        this.maxAllocatedBytes = maxAllocatedBytes;
    }

    public int Size => maxAllocatedBytes;

    public void Serialize(string value, Span<byte> destination)
    {
        // Если значение null, трактуем как пустую строку
        value ??= string.Empty;

        // Вычисляем, сколько символов можно сохранить:
        int maxChars = (maxAllocatedBytes - 4) / 2;
        int len = Math.Min(value.Length, maxChars);
        byte[] lenBytes = BitConverter.GetBytes(len);
        lenBytes.CopyTo(destination.Slice(0, 4));

        // Кодируем строку в Unicode (UTF-16)
        byte[] strBytes = Encoding.Unicode.GetBytes(value.Substring(0, len));
        int available = maxAllocatedBytes - 4;
        int count = Math.Min(strBytes.Length, available);
        strBytes.AsSpan(0, count).CopyTo(destination.Slice(4));

        // Обнуляем остаток блока, если необходимо
        if (count < available)
        {
            destination.Slice(4 + count, available - count).Clear();
        }
    }

    public string Deserialize(ReadOnlySpan<byte> source)
    {
        int len = BitConverter.ToInt32(source.Slice(0, 4));
        int bytesCount = len * 2;
        string result = Encoding.Unicode.GetString(source.Slice(4, bytesCount));
        return result;
    }
}
