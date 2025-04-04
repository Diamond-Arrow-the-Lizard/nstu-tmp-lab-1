namespace VirtualMemory.Models;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VirtualMemory.Interfaces;

public class VirtualMemoryManager<T> : IVirtualMemoryManager<T>
{
    private const int DefaultPageSize = 512;
    private readonly int _pageSize;
    private readonly int _bufferSize;
    private readonly int _elementsPerPage;
    private readonly ISerializer<T> _serializer;
    private readonly IFileHandler _fileHandler;
    private readonly Dictionary<long, IPage<T>> _pagesInMemory = new();

    public VirtualMemoryManager(int bufferSize, ISerializer<T> serializer, IFileHandler fileHandler, int pageSize = DefaultPageSize)
    {
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be positive");
        if (bufferSize < 3)
            throw new ArgumentException("Buffer size must be at least 3 pages", nameof(bufferSize));
        if (pageSize != DefaultPageSize)
            throw new ArgumentException($"Page size must be {DefaultPageSize} bytes", nameof(pageSize));
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(fileHandler);

        _pageSize = pageSize;
        _bufferSize = bufferSize;
        _serializer = serializer;
        _fileHandler = fileHandler;
        _elementsPerPage = CalculateElementsPerPage();
    }

    public int BufferSize => _bufferSize;

    private int CalculateElementsPerPage()
    {
        int elementSizeBits = _serializer.Size * 8 + 1; // +1 бит для битовой карты
        return (_pageSize * 8) / elementSizeBits;
    }

    public T ReadElement(long index)
    {
        var (pageNumber, offset) = CalculateIndices(index);
        var page = GetOrLoadPage(pageNumber);
        
        if (!page.IsElementInitialized(offset))
            throw new InvalidOperationException("Element not initialized");
        
        return page.Data[offset];
    }

    public void WriteElement(long index, T value)
    {
        var (pageNumber, offset) = CalculateIndices(index);
        var page = GetOrLoadPage(pageNumber);
        
        page.Data[offset] = value;
        page.MarkAsModified(offset);
    }

    public void FlushModifiedPages()
    {
        foreach (var page in _pagesInMemory.Values.Where(p => p.Modified))
        {
            var bitmapBytes = ConvertBitArray(page.BitMap);
            var dataBytes = SerializeData(page.Data);
            _fileHandler.WritePage(page.AbsolutePageNumber, bitmapBytes, dataBytes);
            page.Modified = false;
        }
    }

    public long CalculateAbsolutePageNumber(long index) => index / _elementsPerPage;

    public void Dispose()
    {
        FlushModifiedPages();
        _fileHandler.Dispose();
        GC.SuppressFinalize(this);
    }

    private (long pageNumber, int offset) CalculateIndices(long index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("Index cannot be negative");
        
        long pageNumber = index / _elementsPerPage;
        int offset = (int)(index % _elementsPerPage);
        return (pageNumber, offset);
    }

    private IPage<T> GetOrLoadPage(long pageNumber)
    {
        if (_pagesInMemory.TryGetValue(pageNumber, out var page))
        {
            page.UpdateAccessTime();
            return page;
        }

        if (_pagesInMemory.Count >= _bufferSize)
            EvictPage();

        return LoadPage(pageNumber);
    }

    private IPage<T> LoadPage(long pageNumber)
    {
        try
        {
            var (bitmapBytes, dataBytes) = _fileHandler.ReadPage(pageNumber);

            if (bitmapBytes.All(b => b == 0))
            {
                return new Page<T>(_elementsPerPage)
                {
                    AbsolutePageNumber = pageNumber,
                    BitMap = new BitArray(_elementsPerPage, false),
                    Data = new T[_elementsPerPage],
                    LastAccessTime = DateTime.UtcNow
                };
            }

            var page = new Page<T>(_elementsPerPage)
            {
                AbsolutePageNumber = pageNumber,
                BitMap = new BitArray(bitmapBytes),
                Data = DeserializeData(dataBytes),
                LastAccessTime = DateTime.UtcNow
            };
            _pagesInMemory[pageNumber] = page;
            return page;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load page {pageNumber}", ex);
        }
    }

    private void EvictPage()
    {
        var pageToEvict = _pagesInMemory.Values
            .OrderBy(p => p.LastAccessTime)
            .First();

        if (pageToEvict.Modified)
        {
            var bitmapBytes = ConvertBitArray(pageToEvict.BitMap);
            var dataBytes = SerializeData(pageToEvict.Data);
            _fileHandler.WritePage(pageToEvict.AbsolutePageNumber, bitmapBytes, dataBytes);
        }

        _pagesInMemory.Remove(pageToEvict.AbsolutePageNumber);
    }

    private static byte[] ConvertBitArray(BitArray bits)
    {
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        bits.CopyTo(bytes, 0);
        return bytes;
    }

    private byte[] SerializeData(T[] data)
    {
        var buffer = new byte[data.Length * _serializer.Size];
        for (int i = 0; i < data.Length; i++)
        {
            var span = new Span<byte>(buffer, i * _serializer.Size, _serializer.Size);
            _serializer.Serialize(data[i], span);
        }
        return buffer;
    }

    private T[] DeserializeData(byte[] bytes)
    {
        T[] result = new T[bytes.Length / _serializer.Size];
        for (int i = 0; i < result.Length; i++)
        {
            var span = new ReadOnlySpan<byte>(bytes, i * _serializer.Size, _serializer.Size);
            result[i] = _serializer.Deserialize(span);
        }
        return result;
    }
}