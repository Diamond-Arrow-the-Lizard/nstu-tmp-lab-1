namespace VirtualMemory.Models;

using System;
using System.Text;
using VirtualMemory.Interfaces;

public class FixedStringSerializer : ISerializer<string>
{
    private readonly int _fixedLength;
    private readonly Encoding _encoding;

    public FixedStringSerializer(int fixedLength, Encoding? encoding = null)
    {
        if (fixedLength <= 0)
            throw new ArgumentException("Fixed length must be positive", nameof(fixedLength));

        _fixedLength = fixedLength;
        _encoding = encoding ?? Encoding.ASCII; // По умолчанию ASCII
    }

    public int Size => _fixedLength;

    public void Serialize(string value, Span<byte> destination)
    {
        if (value.Length > _fixedLength)
            throw new ArgumentException($"String length exceeds fixed size of {_fixedLength}");

        // Дополняем строку нулями до фиксированной длины
        string paddedValue = value.PadRight(_fixedLength, '\0');
        _encoding.GetBytes(paddedValue, destination);
    }

    public string Deserialize(ReadOnlySpan<byte> source)
    {
        string value = _encoding.GetString(source);
        return value.TrimEnd('\0'); // Удаляем дополняющие нули
    }
}