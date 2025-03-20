namespace VirtualMemory.Models;

using System.Collections;
using VirtualMemory.Interfaces;

// Основной класс управления виртуальной памятью
public class VirtualMemoryManager<T>
{
    private readonly string fileName;
    private readonly FileStream fs;
    private readonly int pageSize = 512;
    private readonly int elementsPerPage;
    private readonly List<IPage<T>> bufferPages;
    private readonly int bufferSize = 3; // минимум 3 страницы в памяти

    // Параметры конструктора:
    // fileName – имя файла,
    // totalElements – размерность виртуального массива,
    // elementsPerPage – число элементов, помещающихся на одной странице (вычисляется исходя из типа и задания)
    public VirtualMemoryManager(string fileName, long totalElements, int elementsPerPage)
    {
        this.fileName = fileName;
        this.elementsPerPage = elementsPerPage;
        bufferPages = new List<IPage<T>>(bufferSize);

        // Если файл не существует, создать его, иначе открыть
        if (!File.Exists(fileName))
        {
            CreateSwapFile(totalElements);
        }
        fs = new FileStream(fileName, FileMode.Open, FileAccess.ReadWrite);

        // Загрузка первых страниц в буфер (минимум 3)
        LoadInitialPages();
    }

    // Метод создания файла подкачки
    private void CreateSwapFile(long totalElements)
    {
        // Вычисляем необходимое количество страниц:
        long totalBytes = totalElements * System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
        int pagesCount = (int)((totalBytes + pageSize - 1) / pageSize);

        using FileStream fCreate = new(fileName, FileMode.CreateNew, FileAccess.ReadWrite);
        // Записываем сигнатуру "ВМ" (например, в ASCII: 'V', 'M')
        byte[] signature = [(byte)'V', (byte)'M'];
        fCreate.Write(signature, 0, signature.Length);

        // Заполняем файл нулями
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
            // Инициализация страниц буфера с нулевым номером.
            bufferPages.Add(new Page<T>(elementsPerPage) { AbsolutePageNumber = i });
            // При необходимости можно добавить чтение страницы из файла.
        }
    }

    // Метод получения номера страницы в буфере, содержащей элемент с индексом 'index'
    private int GetBufferPageIndex(long index)
    {
        // Вычисление абсолютного номера страницы
        int pageNumber = (int)(index / elementsPerPage);

        // Проверяем, есть ли нужная страница в буфере
        for (int i = 0; i < bufferPages.Count; i++)
        {
            if (bufferPages[i].AbsolutePageNumber == pageNumber)
            {
                // Обновляем время доступа
                bufferPages[i].LastAccessTime = DateTime.Now;
                return i;
            }
        }
        // Если страницы нет в буфере, выполняем замещение
        return SwapPage(pageNumber);
    }

    // Метод замещения страницы (выбирается самая старая)
    private int SwapPage(int requiredPageNumber)
    {
        // Находим индекс страницы с минимальным временем доступа
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
        // Если страница модифицирована, выгружаем её
        if (bufferPages[oldestIndex].Modified)
        {
            WritePageToFile(bufferPages[oldestIndex]);
        }
        // Загружаем новую страницу с номером requiredPageNumber
        bufferPages[oldestIndex] = ReadPageFromFile(requiredPageNumber);
        bufferPages[oldestIndex].LastAccessTime = DateTime.Now;
        bufferPages[oldestIndex].Modified = false;
        return oldestIndex;
    }
    // Метод чтения страницы из файла
    private IPage<T> ReadPageFromFile(int pageNumber)
    {
        IPage<T> page = new Page<T>(elementsPerPage)
        {
            AbsolutePageNumber = pageNumber
        };

        // Вычисляем смещение страницы в файле (учитывая 2 байта для сигнатуры)
        long offset = 2 + pageNumber * pageSize;
        fs.Seek(offset, SeekOrigin.Begin);

        using (BinaryReader br = new(fs, System.Text.Encoding.Default, leaveOpen: true))
        {
            // Определяем, сколько байтов требуется для битовой карты.
            // Для elementsPerPage элементов потребуется (elementsPerPage + 7) / 8 байт.
            int bitMapLength = (elementsPerPage + 7) / 8;
            byte[] bitMapBytes = br.ReadBytes(bitMapLength);
            page.BitMap = new BitArray(bitMapBytes);

            // Далее считываем данные страницы.
            // Пример реализован для типа int. Для других типов необходимо добавить соответствующую логику.
            for (int i = 0; i < elementsPerPage; i++)
            {
                if (typeof(T) == typeof(int))
                {
                    int value = br.ReadInt32();
                    // Приведение через object позволяет присвоить значение обобщённому типу
                    page.Data[i] = (T)(object)value;
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

        // Сериализуем битовую карту и данные страницы в массив байт.
        // Пример: для int, преобразуем каждый элемент в 4 байта.
        byte[] pageData = new byte[pageSize];
        // Реализуйте заполнение pageData согласно внутренней структуре страницы

        fs.Write(pageData, 0, pageSize);
        fs.Flush();
    }

    // Метод чтения значения элемента по индексу
    public T ReadElement(long index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("Индекс меньше 0");
        int pageIndex = GetBufferPageIndex(index);
        int offsetInPage = (int)(index % elementsPerPage);

        // Можно добавить проверку битовой карты: если элемент не инициализирован – генерировать ошибку
        return bufferPages[pageIndex].Data[offsetInPage];
    }

    // Метод записи значения по индексу
    public void WriteElement(long index, T value)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("Индекс меньше 0");
        int pageIndex = GetBufferPageIndex(index);
        int offsetInPage = (int)(index % elementsPerPage);

        bufferPages[pageIndex].Data[offsetInPage] = value;
        bufferPages[pageIndex].Modified = true;
        bufferPages[pageIndex].LastAccessTime = DateTime.Now;
        // Обновляем битовую карту: отмечаем, что в данной ячейке записано значение
        bufferPages[pageIndex].BitMap.Set(offsetInPage, true);
    }

    // Метод закрытия файлов
    public void Close()
    {
        // Перед закрытием выгружаем все модифицированные страницы
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


