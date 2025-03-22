using VirtualMemory.Interfaces;

public interface IFileHandler
{
    void CreateOrOpen(string filename, int pageSize);
    void WritePage(long pageNumber, byte[] data);
    byte[] ReadPage(long pageNumber);
    void DeleteFile();
}