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

    public FixedStringMemoryManager(int bufferSize, string filename, int stringLength)
    {
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be positive");
        if (bufferSize < 3)
            throw new ArgumentException("Buffer size must be at least 3 pages", nameof(bufferSize));

        _bufferSize = bufferSize;
        _pageSize = DefaultPageSize;
        _serializer = new FixedStringSerializer(stringLength, Encoding.ASCII);
        _elementsPerPage = CalculateElementsPerPage();

        _fileHandler = new PageFileHandler(_elementsPerPage, _serializer.Size);
        _fileHandler.CreateOrOpen(filename);
    }

    public int BufferSize => _bufferSize;

    private int CalculateElementsPerPage()
    {
        int elementSize = _serializer.Size;

        for (int count = 1; count < 10000; count++)
        {
            int bitmapSize = (count + 7) / 8;
            int dataSize = count * elementSize;

            if (bitmapSize + dataSize > _pageSize)
                return count - 1;
        }

        throw new InvalidOperationException($"Cannot fit any elements of size {elementSize} on a page of size {_pageSize}.");
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
        // The page will be written to file when it's evicted or FlushModifiedPages is called.
    }

    public void FlushModifiedPages()
    {
        foreach (var page in _pagesInMemory.Values.Where(p => p.Modified).ToList())
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
        if (offset < 0 || offset >= _elementsPerPage)
             throw new IndexOutOfRangeException($"Calculated offset {offset} is out of page bounds (0-{_elementsPerPage - 1}).");
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
            if (page.Data.Length != _elementsPerPage)
            {
                 throw new InvalidOperationException($"Loaded page {pageNumber} has incorrect data size. Expected {_elementsPerPage}, got {page.Data.Length}.");
            }
            _pagesInMemory[pageNumber] = page;
            return page;
        }
        catch (ArgumentOutOfRangeException)
        {
            // Page does not exist in file, create a new zeroed one
            var page = new Page<string>(_elementsPerPage)
            {
                AbsolutePageNumber = pageNumber,
                BitMap = new BitArray(_elementsPerPage, false),
                Data = new string[_elementsPerPage],
                LastAccessTime = DateTime.UtcNow
            };
            for(int i = 0; i < _elementsPerPage; i++)
            {
                page.Data[i] = string.Empty; 
            }
            _pagesInMemory[pageNumber] = page;
            return page;
        }
         catch (IOException ex)
        {
             throw new IOException($"Failed to read page {pageNumber} from file.", ex);
        }
         catch (InvalidOperationException ex) when (ex.Message.Contains("Insufficient data for deserialization"))
        {
             throw new InvalidOperationException($"File may be corrupted. Insufficient data to deserialize page {pageNumber}.", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load page {pageNumber}", ex);
        }
    }

    private void EvictPage()
    {
        // Find the least recently used page
        var pageToEvict = _pagesInMemory.Values
            .OrderBy(p => p.LastAccessTime)
            .First(); 

        // Only write back if modified
        if (pageToEvict.Modified)
        {
            var bitmapBytes = ConvertBitArray(pageToEvict.BitMap);
            var dataBytes = SerializeData(pageToEvict.Data);
            _fileHandler.WritePage(pageToEvict.AbsolutePageNumber, bitmapBytes, dataBytes);
            pageToEvict.Modified = false; // Reset modified flag after flushing
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
        if (data.Length != _elementsPerPage)
        {
            throw new ArgumentException($"Data array size mismatch during serialization. Expected {_elementsPerPage}, got {data.Length}.");
        }

        var buffer = new byte[_elementsPerPage * _serializer.Size];
        for (int i = 0; i < _elementsPerPage; i++)
        {
            // FixedStringSerializer handles padding, so pass the string directly.
            // Ensure no nulls are passed if the serializer doesn't handle them.
             string valueToSerialize = data[i] ?? string.Empty; 


            var span = new Span<byte>(buffer, i * _serializer.Size, _serializer.Size);
            _serializer.Serialize(valueToSerialize, span);
        }
        return buffer;
    }

    private string[] DeserializeData(byte[] bytes)
    {
        int expectedDataSize = _elementsPerPage * _serializer.Size;
        if (bytes.Length != expectedDataSize)
            throw new InvalidOperationException($"Insufficient data for deserialization. Expected {expectedDataSize}, got {bytes.Length}.");

        string[] result = new string[_elementsPerPage];
        for (int i = 0; i < _elementsPerPage; i++)
        {
            var span = new ReadOnlySpan<byte>(bytes, i * _serializer.Size, _serializer.Size);
            result[i] = _serializer.Deserialize(span);
        }
        return result;
    }
}