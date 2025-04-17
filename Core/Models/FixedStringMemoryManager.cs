namespace VirtualMemory.Models;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VirtualMemory.Interfaces;

public class FixedStringMemoryManager : IVirtualMemoryManager<string>
{
    private const int DefaultPageSize = 512;
    private readonly int _pageSize;
    private readonly int _bufferSize;
    private readonly int _elementsPerPage;
    private readonly ISerializer<string> _serializer;
    private readonly IFileHandler _fileHandler;
    private readonly Dictionary<long, IPage<string>> _pagesInMemory = new();

    public FixedStringMemoryManager(int bufferSize, string filename, int stringLength, int pageSize = DefaultPageSize)
    {
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be positive");
        if (bufferSize < 3)
            throw new ArgumentException("Buffer size must be at least 3 pages", nameof(bufferSize));
        if (pageSize != DefaultPageSize)
            throw new ArgumentException($"Page size must be {DefaultPageSize} bytes", nameof(pageSize));

        _pageSize = pageSize;
        _bufferSize = bufferSize;
        _serializer = new FixedStringSerializer(stringLength, Encoding.ASCII);
        _elementsPerPage = CalculateElementsPerPage();

        _fileHandler = new PageFileHandler(pageSize, _elementsPerPage);
        _fileHandler.CreateOrOpen(filename);
    }

    public int BufferSize => _bufferSize;

private int CalculateElementsPerPage()
{
    int elementSize = _serializer.Size;

    // Ищем максимальное количество элементов, чтобы bitmap + data влезли в страницу
    for (int count = 1; count < 10000; count++)
    {
        int bitmapSize = (count + 7) / 8;
        int dataSize = count * elementSize;

        if (bitmapSize + dataSize > _pageSize)
            return count - 1;
    }

    throw new InvalidOperationException("Cannot fit any elements on page.");
}


    public string ReadElement(long index)
    {
        var (pageNumber, offset) = CalculateIndices(index);
        var page = GetOrLoadPage(pageNumber);

        if (!page.IsElementInitialized(offset))
            throw new InvalidOperationException("Element not initialized");

        return page.Data[offset];
    }

    public void WriteElement(long index, string value)
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

    private IPage<string> GetOrLoadPage(long pageNumber)
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

    private IPage<string> LoadPage(long pageNumber)
    {
        try
        {
            var (bitmapBytes, dataBytes) = _fileHandler.ReadPage(pageNumber);
            var page = new Page<string>(_elementsPerPage)
            {
                AbsolutePageNumber = pageNumber,
                BitMap = new BitArray(bitmapBytes),
                Data = DeserializeData(dataBytes),
                LastAccessTime = DateTime.UtcNow
            };
            _pagesInMemory[pageNumber] = page;
            return page;
        }
        catch (ArgumentOutOfRangeException)
        {
            var page = new Page<string>(_elementsPerPage)
            {
                AbsolutePageNumber = pageNumber,
                BitMap = new BitArray(_elementsPerPage, false),
                Data = new string[_elementsPerPage],
                LastAccessTime = DateTime.UtcNow
            };
            _pagesInMemory[pageNumber] = page;
            return page;
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

    private byte[] SerializeData(string[] data)
    {
        // The 'data' array passed in should ideally already have size _elementsPerPage
        if (data.Length != _elementsPerPage)
        {
            throw new ArgumentOutOfRangeException("Data size and the amount of elements per page don't match");
        }

        var buffer = new byte[_elementsPerPage * _serializer.Size];
        for (int i = 0; i < _elementsPerPage; i++) // Loop up to _elementsPerPage
        {
            // Check if the input array has this element, handle nulls
            string valueToSerialize = (i < data.Length && data[i] != null) ? data[i] : string.Empty;

            var span = new Span<byte>(buffer, i * _serializer.Size, _serializer.Size);
            _serializer.Serialize(valueToSerialize, span);
        }
        return buffer;
    }

    private string[] DeserializeData(byte[] bytes)
    {
        int expectedDataSize = _elementsPerPage * _serializer.Size;
        if (bytes.Length < expectedDataSize)
            throw new InvalidOperationException($"Insufficient data for deserialization. Expected {expectedDataSize}, got {bytes.Length}.");

        // Create the result array based on elements per page
        string[] result = new string[_elementsPerPage];
        for (int i = 0; i < result.Length; i++) 
        {
            // Ensure you only read the necessary part of the 'bytes' array
            var span = new ReadOnlySpan<byte>(bytes, i * _serializer.Size, _serializer.Size);
            result[i] = _serializer.Deserialize(span);
        }
        return result;
    }
}