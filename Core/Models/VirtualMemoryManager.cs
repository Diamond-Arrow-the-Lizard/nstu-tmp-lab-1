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
    private readonly int elementSize; // Размер одного элемента в байтах (для строковых типов)

    // Для строковых типов elementSize должен быть > 0, для остальных можно не передавать
    public VirtualMemoryManager(string fileName, long totalElements, int elementsPerPage, int elementSize = 0)
    {
        this.fileName = fileName;
        this.elementsPerPage = elementsPerPage;
        this.bufferPages = new List<IPage<T>>(bufferSize);

        // Если T равен string, то требуется передать элементный размер (например, для char фиксированная длина * 2, для varchar – maxLength*2+4)
        if (typeof(T) == typeof(string))
        {
            if (elementSize <= 0)
                throw new ArgumentException("Для строкового типа необходимо указать размер элемента (elementSize > 0).");
            this.elementSize = elementSize;
        }
        else
        {
            // Для остальных типов можно вычислить размер через Marshal.SizeOf
            this.elementSize = Marshal.SizeOf(typeof(T));
        }

        // Если файла не существует, создаём его, иначе открываем
        if (!File.Exists(fileName))
        {
            CreateSwapFile(totalElements);
        }
        fs = new FileStream(fileName, FileMode.Open, FileAccess.ReadWrite);

        // Загрузка начальных страниц в буфер (минимум 3)
        LoadInitialPages();
    }

    // Метод создания файла подкачки
    private void CreateSwapFile(long totalElements)
    {
        // Вычисляем общий объём в байтах:
        long totalBytes = totalElements * elementSize;
        int pagesCount = (int)((totalBytes + pageSize - 1) / pageSize);

        using FileStream fCreate = new(fileName, FileMode.CreateNew, FileAccess.ReadWrite);
        // Записываем сигнатуру "ВМ" (например, 'V', 'M')
        byte[] signature = new byte[] { (byte)'V', (byte)'M' };
        fCreate.Write(signature, 0, signature.Length);

        // Заполняем файл пустыми страницами (нулями)
        byte[] emptyPage = new byte[pageSize];
        for (int i = 0; i < pagesCount; i++)
        {
            fCreate.Write(emptyPage, 0, pageSize);
        }
    }

    // Метод загрузки начальных страниц в буфер
    private void LoadInitialPages()
    {
        for (int i = 0; i < bufferSize; i++)
        {
            bufferPages.Add(new Page<T>(elementsPerPage) { AbsolutePageNumber = i });
        }
    }

    // Метод получения номера страницы в буфере, содержащей элемент с индексом 'index'
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

    // Метод замещения страницы (выбирается самая старая)
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

    // Метод чтения страницы из файла с десериализацией
    private IPage<T> ReadPageFromFile(int pageNumber)
    {
        IPage<T> page = new Page<T>(elementsPerPage)
        {
            AbsolutePageNumber = pageNumber
        };

        // Смещение с учётом 2 байт сигнатуры
        long offset = 2 + pageNumber * pageSize;
        fs.Seek(offset, SeekOrigin.Begin);

        using (BinaryReader br = new BinaryReader(fs, System.Text.Encoding.Default, leaveOpen: true))
        {
            int bitMapLength = (elementsPerPage + 7) / 8;
            byte[] bitMapBytes = br.ReadBytes(bitMapLength);
            page.BitMap = new BitArray(bitMapBytes);

            // Пример десериализации для int. Для string необходимо реализовать соответствующую логику.
            for (int i = 0; i < elementsPerPage; i++)
            {
                if (typeof(T) == typeof(int))
                {
                    int value = br.ReadInt32();
                    page.Data[i] = (T)(object)value;
                }
                else if (typeof(T) == typeof(string))
                {
                    // Для строк предполагаем, что записано сначала 4 байта длины, затем сами символы в формате Unicode
                    int len = br.ReadInt32();
                    // Ограничиваем длину, чтобы не выйти за пределы elementSize
                    int maxBytes = elementSize - 4;
                    int bytesToRead = Math.Min(len * 2, maxBytes);
                    byte[] stringBytes = br.ReadBytes(bytesToRead);
                    string strValue = System.Text.Encoding.Unicode.GetString(stringBytes);
                    page.Data[i] = (T)(object)strValue;
                }
                else
                {
                    throw new NotSupportedException($"Десериализация для типа {typeof(T)} не реализована.");
                }
            }
        }
        return page;
    }

    // Метод записи страницы в файл
    private void WritePageToFile(IPage<T> page)
    {
        long offset = 2 + page.AbsolutePageNumber * pageSize;
        fs.Seek(offset, SeekOrigin.Begin);
        byte[] pageData = new byte[pageSize];

        // Пример сериализации для int. Для string необходимо реализовать свою логику.
        if (typeof(T) == typeof(int))
        {
            // Запишем битовую карту
            int bitMapLength = (elementsPerPage + 7) / 8;
            byte[] bitMapBytes = new byte[bitMapLength];
            page.BitMap.CopyTo(bitMapBytes, 0);
            Array.Copy(bitMapBytes, 0, pageData, 0, bitMapLength);

            // Запишем данные
            for (int i = 0; i < elementsPerPage; i++)
            {
                byte[] intBytes = BitConverter.GetBytes((int)(object)page.Data[i]!);
                Array.Copy(intBytes, 0, pageData, bitMapLength + i * 4, 4);
            }
        }
        else if (typeof(T) == typeof(string))
        {
            int bitMapLength = (elementsPerPage + 7) / 8;
            byte[] bitMapBytes = new byte[bitMapLength];
            page.BitMap.CopyTo(bitMapBytes, 0);
            Array.Copy(bitMapBytes, 0, pageData, 0, bitMapLength);

            for (int i = 0; i < elementsPerPage; i++)
            {
                string str = (string)(object)page.Data[i]!; 
                byte[] strBytes = System.Text.Encoding.Unicode.GetBytes(str);
                int len = strBytes.Length / 2;
                // Запишем 4 байта длины строки
                byte[] lenBytes = BitConverter.GetBytes(len);
                Array.Copy(lenBytes, 0, pageData, bitMapLength + i * elementSize, 4);
                // Запишем строку (ограничиваем количеством байтов)
                int maxBytes = elementSize - 4;
                int bytesToWrite = Math.Min(strBytes.Length, maxBytes);
                Array.Copy(strBytes, 0, pageData, bitMapLength + i * elementSize + 4, bytesToWrite);
            }
        }
        else
        {
            throw new NotSupportedException($"Сериализация для типа {typeof(T)} не реализована.");
        }

        fs.Write(pageData, 0, pageSize);
        fs.Flush();
    }

    // Чтение элемента по индексу
    public T ReadElement(long index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("Индекс меньше 0");
        int pageIndex = GetBufferPageIndex(index);
        int offsetInPage = (int)(index % elementsPerPage);
        return bufferPages[pageIndex].Data[offsetInPage];
    }

    // Запись элемента по индексу
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
    }

    // Закрытие файлового потока
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
