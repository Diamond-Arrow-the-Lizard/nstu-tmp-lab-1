using VirtualMemory.Interfaces;
using System;
using System.Buffers.Binary;

namespace VirtualMemory.Models;
public class VarCharSerializer : ISerializer<long>
{
    public int Size => sizeof(long); 

    public void Serialize(long value, Span<byte> destination)
    {
        BinaryPrimitives.WriteInt64LittleEndian(destination, value);
    }

    public long Deserialize(ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadInt64LittleEndian(source);
    }
}