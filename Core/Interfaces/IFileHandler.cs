using System;
using VirtualMemory.Interfaces;

public interface IFileHandler : IDisposable
{
    void CreateOrOpen(string filename, int pageSize);
    void WritePage(long pageNumber, byte[] bitmap, byte[] data);
    (byte[] bitmap, byte[] data) ReadPage(long pageNumber);
    void DeleteFile();
}