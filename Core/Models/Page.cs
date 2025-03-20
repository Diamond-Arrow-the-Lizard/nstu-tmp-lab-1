namespace VirtualMemory.Models;

using VirtualMemory.Interfaces;
using System.Collections;

    // Структура, описывающая страницу в памяти
    public class Page<T> : IPage<T>
    {
        public long AbsolutePageNumber { get; set; }
        public bool Modified { get; set; }
        public DateTime LastAccessTime { get; set; }
        public BitArray BitMap { get; set; }
        public T[] Data { get; set; }

        public Page(int elementsPerPage)
        {
            Data = new T[elementsPerPage];
            BitMap = new BitArray(elementsPerPage);
            Modified = false;
            LastAccessTime = DateTime.Now;
        }
    }
