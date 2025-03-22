namespace VirtualMemory.Interfaces;

public interface IVirtualMemoryManager<T> : IDisposable
{
    T ReadElement(long index);
    void WriteElement(long index, T value);

}