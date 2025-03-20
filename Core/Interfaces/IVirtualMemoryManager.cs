namespace VirtualMemory.Interfaces;

public interface IVirtualMemoryManager<T>
{
    T ReadElement(long index);
    void WriteElement(long index, T value);
    void Close();
}