namespace VirtualMemory.Models;

using System;
using VirtualMemory.Interfaces;

public class IntSerializer : ISerializer<int>
{
    public int Size => sizeof(int); // 4 байта для int

    public void Serialize(int value, Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException("Destination span too small", nameof(destination));
        
        BitConverter.TryWriteBytes(destination, value);
    }

    public int Deserialize(ReadOnlySpan<byte> source)
    {
        if (source.Length < Size)
            throw new ArgumentException("Source span too small", nameof(source));
        
        return BitConverter.ToInt32(source);
    }
}