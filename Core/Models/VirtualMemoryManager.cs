namespace VirtualMemory.Models;

using System.Collections;
using VirtualMemory.Interfaces;
using System.Runtime.InteropServices;

public class VirtualMemoryManager<T>
{
    private readonly string fileName;
    private readonly FileStream fs;
    private readonly int pageSize = 512;
    private readonly int elementsPerPage;
    private readonly List<IPage<T>> bufferPages;
    private readonly int bufferSize = 3; // минимум 3 страницы в памяти
    private readonly int elementSize; // размер одного элемента в байтах (берётся из сериализатора)
    private readonly ISerializer<T> serializer;

    // Конструктор принимает экземпляр сериализатора, который инкапсулирует всю логику преобразования T в байты и обратно.
    public VirtualMemoryManager(string fileName, long totalElements, int elementsPerPage, ISerializer<T> serializer)
    {
        this.fileName = fileName;
        this.elementsPerPage = elementsPerPage;
        this.serializer = serializer;
        this.elementSize = serializer.Size;
        bufferPages = new List<IPage<T>>(bufferSize);

        if (!File.Exists(fileName))
        {
            CreateSwapFile(totalElements);
        }
        fs = new FileStream(fileName, FileMode.Open, FileAccess.ReadWrite);
        LoadInitialPages();
    }

    // Создание файла подкачки с вычисленным общим объёмом (с учётом размера элемента)
    private void CreateSwapFile(long totalElements)
    {
        long totalBytes = totalElements * elementSize;
        int pagesCount = (int)((totalBytes + pageSize - 1) / pageSize);

        using FileStream fCreate = new(fileName, FileMode.CreateNew, FileAccess.ReadWrite);
        byte[] signature = new byte[] { (byte)'V', (byte)'M' };
        fCreate.Write(signature, 0, signature.Length);

        byte[] emptyPage = new byte[pageSize];
        for (int i = 0; i < pagesCount; i++)
        {
            fCreate.Write(emptyPage, 0, pageSize);
        }
    }

    // Инициализация буфера страниц
    private void LoadInitialPages()
    {
        for (int i = 0; i < bufferSize; i++)
        {
            bufferPages.Add(new Page<T>(elementsPerPage) { AbsolutePageNumber = i });
        }
    }

    // Определение страницы в буфере для данного индекса
    private int GetBufferPageIndex(long index)
    {
        int pageNumber = (int)(index / elementsPerPage);
        for (int i = 0; i < bufferPages.Count; i++)
        {
            if (bufferPages[i].AbsolutePageNumber == pageNumber)
            {
                bufferPages[i].LastAccessTime = DateTime.Now;
                return i;
            }
        }
        return SwapPage(pageNumber);
    }

    // Замещение страницы – выбирается самая старая
    private int SwapPage(int requiredPageNumber)
    {
        int oldestIndex = 0;
        DateTime oldest = bufferPages[0].LastAccessTime;
        for (int i = 1; i < bufferPages.Count; i++)
        {
            if (bufferPages[i].LastAccessTime < oldest)
            {
                oldest = bufferPages[i].LastAccessTime;
                oldestIndex = i;
            }
        }
        if (bufferPages[oldestIndex].Modified)
        {
            WritePageToFile(bufferPages[oldestIndex]);
        }
        bufferPages[oldestIndex] = ReadPageFromFile(requiredPageNumber);
        bufferPages[oldestIndex].LastAccessTime = DateTime.Now;
        bufferPages[oldestIndex].Modified = false;
        return oldestIndex;
    }

    // Унифицированная десериализация страницы с использованием ISerializer<T>
    private IPage<T> ReadPageFromFile(int pageNumber)
    {
        IPage<T> page = new Page<T>(elementsPerPage)
        {
            AbsolutePageNumber = pageNumber
        };

        long offset = 2 + pageNumber * pageSize;
        fs.Seek(offset, SeekOrigin.Begin);

        using (BinaryReader br = new BinaryReader(fs, System.Text.Encoding.Default, leaveOpen: true))
        {
            int bitMapLength = (elementsPerPage + 7) / 8;
            byte[] bitMapBytes = br.ReadBytes(bitMapLength);
            page.BitMap = new BitArray(bitMapBytes);

            for (int i = 0; i < elementsPerPage; i++)
            {
                byte[] buffer = br.ReadBytes(elementSize);
                page.Data[i] = serializer.Deserialize(buffer);
            }
        }
        return page;
    }

    // Унифицированная сериализация страницы с использованием ISerializer<T>
    private void WritePageToFile(IPage<T> page)
    {
        long offset = 2 + page.AbsolutePageNumber * pageSize;
        fs.Seek(offset, SeekOrigin.Begin);
        byte[] pageData = new byte[pageSize];

        int bitMapLength = (elementsPerPage + 7) / 8;
        byte[] bitMapBytes = new byte[bitMapLength];
        page.BitMap.CopyTo(bitMapBytes, 0);
        Array.Copy(bitMapBytes, 0, pageData, 0, bitMapLength);

        for (int i = 0; i < elementsPerPage; i++)
        {
            int pos = bitMapLength + i * elementSize;
            byte[] buffer = new byte[elementSize];
            // Если значение не инициализировано, использовать пустую строку
            var value = page.Data[i] ?? (T)(object)string.Empty;
            serializer.Serialize(value, buffer);
            Array.Copy(buffer, 0, pageData, pos, elementSize);
        }

        fs.Write(pageData, 0, pageSize);
        fs.Flush();
    }

    public T ReadElement(long index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("Индекс меньше 0");
        int pageIndex = GetBufferPageIndex(index);
        int offsetInPage = (int)(index % elementsPerPage);
        return bufferPages[pageIndex].Data[offsetInPage];
    }

    public void WriteElement(long index, T value)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("Индекс меньше 0");
        int pageIndex = GetBufferPageIndex(index);
        int offsetInPage = (int)(index % elementsPerPage);
        bufferPages[pageIndex].Data[offsetInPage] = value;
        bufferPages[pageIndex].Modified = true;
        bufferPages[pageIndex].LastAccessTime = DateTime.Now;
        bufferPages[pageIndex].BitMap.Set(offsetInPage, true);

        WritePageToFile(bufferPages[pageIndex]);
    }

    public void Close()
    {
        foreach (var page in bufferPages)
        {
            if (page.Modified)
            {
                WritePageToFile(page);
            }
        }
        fs.Close();
    }
}
