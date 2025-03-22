using System.Collections;
using VirtualMemory.Interfaces;

namespace VirtualMemory.Models;

public class Page<T> : IPage<T>
{
    public long AbsolutePageNumber { get; set; }
    public bool Modified { get; set; }
    public DateTime LastAccessTime { get; set; }
    public BitArray BitMap { get; set; }
    public T[] Data { get; set; }

    // Размер страницы в элементах (например, 128 элементов для int)
    private readonly int _elementsPerPage;

    public Page(long absolutePageNumber, int elementsPerPage)
    {
        AbsolutePageNumber = absolutePageNumber;
        _elementsPerPage = elementsPerPage;
        
        // Инициализация битовой карты (все биты 0)
        BitMap = new BitArray(elementsPerPage, false);
        
        // Инициализация массива данных
        Data = new T[elementsPerPage];
        
        LastAccessTime = DateTime.Now;
        Modified = false;
    }

    /// <summary>
    /// Обновляет время последнего доступа к странице.
    /// </summary>
    public void UpdateAccessTime()
    {
        LastAccessTime = DateTime.Now;
    }

    /// <summary>
    /// Проверяет, инициализирован ли элемент по индексу.
    /// </summary>
    public bool IsElementInitialized(int index)
    {
        if (index < 0 || index >= _elementsPerPage)
            throw new ArgumentOutOfRangeException(nameof(index));
        
        return BitMap[index];
    }

    /// <summary>
    /// Устанавливает флаг модификации и обновляет битовую карту.
    /// </summary>
    public void MarkAsModified(int index)
    {
        if (index < 0 || index >= _elementsPerPage)
            throw new ArgumentOutOfRangeException(nameof(index));
        
        BitMap[index] = true;
        Modified = true;
        UpdateAccessTime();
    }
}