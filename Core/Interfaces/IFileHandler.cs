using VirtualMemory.Interfaces;

public interface IFileHandler
{
    void CreateOrOpen(string filename, int pageSize);
    void WritePage(long pageNumber, byte[] bitmap, byte[] data);
    (byte[] bitmap, byte[] data) ReadPage(long pageNumber);
    void DeleteFile();
}