using System.Buffers.Binary;
using VirtualMemory.Interfaces;

public class IntSerializer : ISerializer<int>
{
    public int Size => 4;
    public void Serialize(int value, Span<byte> destination)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, value);
    }
    public int Deserialize(ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(source);
    }
}
