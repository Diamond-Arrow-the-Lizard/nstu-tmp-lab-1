namespace VirtualMemory.Interfaces;

using System.Collections;

public interface IPage<T>
{
    long AbsolutePageNumber { get; set; }
    bool Modified { get; set; }
    DateTime LastAccessTime { get; set; }
    BitArray BitMap { get; set; }
    T[] Data { get; set; }

    // Добавленные методы
    void UpdateAccessTime();
    bool IsElementInitialized(int index);
    void MarkAsModified(int index);
}