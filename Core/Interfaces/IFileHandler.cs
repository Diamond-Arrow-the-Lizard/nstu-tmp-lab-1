namespace VirtualMemory.Interfaces;

public interface IFileHandler : IDisposable
{
    void CreateOrOpen(string filename);
    void WritePage(long pageNumber, byte[] bitmap, byte[] data);
    (byte[] bitmap, byte[] data) ReadPage(long pageNumber);
    void DeleteFile();
}