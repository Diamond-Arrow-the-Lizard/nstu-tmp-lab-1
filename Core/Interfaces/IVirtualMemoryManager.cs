namespace VirtualMemory.Interfaces;

public interface IVirtualMemoryManager<T> : IDisposable
{
    int BufferSize { get; }
    T ReadElement(long index);
    void WriteElement(long index, T value);
    void FlushModifiedPages(); 
    long CalculateAbsolutePageNumber(long index); 
}
