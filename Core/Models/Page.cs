namespace VirtualMemory.Models;

using System;
using System.Collections;
using VirtualMemory.Interfaces;

public class Page<T> : IPage<T>
{
    public long AbsolutePageNumber { get; set; }
    public bool Modified { get; set; }
    public DateTime LastAccessTime { get; set; }
    public BitArray BitMap { get; set; }
    public T[] Data { get; set; }

    public Page(int elementsPerPage)
    {
        BitMap = new BitArray(elementsPerPage);
        Data = new T[elementsPerPage];
        LastAccessTime = DateTime.Now;
        Modified = false;
    }

    public void UpdateAccessTime() => LastAccessTime = DateTime.Now;

    public bool IsElementInitialized(int index)
    {
        if (index < 0 || index >= BitMap.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        return BitMap[index];
    }

    public void MarkAsModified(int index)
    {
        if (index < 0 || index >= BitMap.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        BitMap[index] = true;
        Modified = true;
        UpdateAccessTime();
    }
}