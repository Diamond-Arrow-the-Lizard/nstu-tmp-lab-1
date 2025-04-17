using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VirtualMemory.Interfaces;

namespace VirtualMemory.Models;

public class VarCharMemoryManager : IVirtualMemoryManager<string>
{
    private const int DefaultPageSize = 512;
    private const int AddressesPerPage = 128; 
    private readonly int _pageSize = DefaultPageSize;
    private readonly int _bufferSize;
    private readonly ISerializer<long> _addressSerializer = new VarCharSerializer();
    private readonly VarCharFileHandler _stringFileHandler = new();
    private readonly IFileHandler _pageFileHandler;
    private readonly Dictionary<long, IPage<long>> _pagesInMemory = new();
    private readonly string _pageFileName;
    private readonly string _stringFileName;
    private long _maxStringLength;

    public VarCharMemoryManager(int bufferSize, string pageFileName, string stringFileName, long maxStringLength)
    {
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be positive");
        if (bufferSize < 3)
            throw new ArgumentException("Buffer size must be at least 3 pages", nameof(bufferSize));

        _bufferSize = bufferSize;
        _pageFileName = pageFileName;
        _stringFileName = stringFileName;
        _maxStringLength = maxStringLength;

        _pageFileHandler = new PageFileHandler(_pageSize, AddressesPerPage);
        _pageFileHandler.CreateOrOpen(_pageFileName);
        _stringFileHandler.CreateOrOpen(_stringFileName);
    }

    public int BufferSize => _bufferSize;

    public string ReadElement(long index)
    {
        var (pageNumber, offset) = CalculateIndices(index);
        var page = GetOrLoadPage(pageNumber);

        if (!page.IsElementInitialized(offset))
            throw new InvalidOperationException("Element not initialized");

        long stringOffset = page.Data[offset];
        return _stringFileHandler.ReadString(stringOffset).value;
    }

    public void WriteElement(long index, string value)
    {
        if (value.Length > _maxStringLength)
            throw new ArgumentException($"String length exceeds maximum allowed length ({_maxStringLength})");

        var (pageNumber, offset) = CalculateIndices(index);
        var page = GetOrLoadPage(pageNumber);

        long stringOffset = _stringFileHandler.AppendString(value);
        page.Data[offset] = stringOffset;
        page.MarkAsModified(offset);

        var bitmapBytes = ConvertBitArray(page.BitMap);
        var dataBytes = SerializeData(page.Data);
        _pageFileHandler.WritePage(page.AbsolutePageNumber, bitmapBytes, dataBytes);
        page.Modified = false;
    }

    public void FlushModifiedPages()
    {
        foreach (var page in _pagesInMemory.Values.Where(p => p.Modified))
        {
            var bitmapBytes = ConvertBitArray(page.BitMap);
            var dataBytes = SerializeData(page.Data);
            _pageFileHandler.WritePage(page.AbsolutePageNumber, bitmapBytes, dataBytes);
            page.Modified = false;
        }
    }

    public long CalculateAbsolutePageNumber(long index) => index / AddressesPerPage;

    public void Dispose()
    {
        FlushModifiedPages();
        _pageFileHandler.Dispose();
        _stringFileHandler.Dispose();
        GC.SuppressFinalize(this);
    }

    private (long pageNumber, int offset) CalculateIndices(long index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("Index cannot be negative");

        long pageNumber = index / AddressesPerPage;
        int offset = (int)(index % AddressesPerPage);
        return (pageNumber, offset);
    }

    private IPage<long> GetOrLoadPage(long pageNumber)
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

    private IPage<long> LoadPage(long pageNumber)
    {
        try
        {
            var (bitmapBytes, dataBytes) = _pageFileHandler.ReadPage(pageNumber);
            var page = new Page<long>(AddressesPerPage)
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
            var page = new Page<long>(AddressesPerPage)
            {
                AbsolutePageNumber = pageNumber,
                BitMap = new BitArray(AddressesPerPage, false),
                Data = new long[AddressesPerPage],
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
            _pageFileHandler.WritePage(pageToEvict.AbsolutePageNumber, bitmapBytes, dataBytes);
        }

        _pagesInMemory.Remove(pageToEvict.AbsolutePageNumber);
    }

    private static byte[] ConvertBitArray(BitArray bits)
    {
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        bits.CopyTo(bytes, 0);
        return bytes;
    }

    private byte[] SerializeData(long[] data)
    {
        var buffer = new byte[data.Length * _addressSerializer.Size];
        for (int i = 0; i < data.Length; i++)
        {
            var span = new Span<byte>(buffer, i * _addressSerializer.Size, _addressSerializer.Size);
            _addressSerializer.Serialize(data[i], span);
        }
        return buffer;
    }

    private long[] DeserializeData(byte[] bytes)
    {
        if (bytes.Length % _addressSerializer.Size != 0)
            throw new InvalidOperationException("Invalid data length for deserialization.");

        long[] result = new long[bytes.Length / _addressSerializer.Size];
        for (int i = 0; i < result.Length; i++)
        {
            var span = new ReadOnlySpan<byte>(bytes, i * _addressSerializer.Size, _addressSerializer.Size);
            result[i] = _addressSerializer.Deserialize(span);
        }
        return result;
    }
}