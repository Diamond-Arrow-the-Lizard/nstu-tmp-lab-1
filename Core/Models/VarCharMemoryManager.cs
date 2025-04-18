using System.Collections;
using VirtualMemory.Interfaces;
using System.Linq; 

namespace VirtualMemory.Models;

public class VarCharMemoryManager : IVirtualMemoryManager<string>
{
    private const int AddressesPerPage = 128;
    private readonly int _bufferSize;
    private readonly ISerializer<long> _addressSerializer = new VarCharSerializer();
    private readonly VarCharFileHandler _stringFileHandler = new();
    private readonly IFileHandler _pageFileHandler;
    private readonly Dictionary<long, IPage<long>> _pagesInMemory = new();
    private readonly string _pageFileName;
    private readonly string _stringFileName;
    private readonly long _maxStringLength;

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

        try
        {
            _pageFileHandler = new PageFileHandler(AddressesPerPage, _addressSerializer.Size);
            _pageFileHandler.CreateOrOpen(_pageFileName);


            _stringFileHandler.CreateOrOpen(_stringFileName);
        }
        catch (IOException ex)
        {
            throw new IOException("Error during file operations", ex);
        }
        catch (OutOfMemoryException ex)
        {
            throw new OutOfMemoryException("Insufficient memory to create object", ex);
        }
    }

    public int BufferSize => _bufferSize;

    public string ReadElement(long index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("Index cannot be negative");

        try
        {
            var (pageNumber, offset) = CalculateIndices(index);
            var page = GetOrLoadPage(pageNumber);

            if (!page.IsElementInitialized(offset))
                throw new InvalidOperationException("Element not initialized");

            long stringOffset = page.Data[offset];
            return _stringFileHandler.ReadString(stringOffset).value;
        }
        catch (IOException ex)
        {
            throw new IOException("Error reading element", ex);
        }
         catch (IndexOutOfRangeException ex) 
        {
            throw new IndexOutOfRangeException($"Index {index} is out of bounds.", ex);
        }
         catch (Exception ex)
        {
             throw new InvalidOperationException($"An unexpected error occurred while reading element {index}.", ex);
        }
    }

    public void WriteElement(long index, string value)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("Index cannot be negative");
        if (value.Length > _maxStringLength)
            throw new ArgumentException($"String length exceeds maximum allowed length ({_maxStringLength})");

        try
        {
            var (pageNumber, offset) = CalculateIndices(index);
            var page = GetOrLoadPage(pageNumber);

            long stringOffset = _stringFileHandler.AppendString(value);
            page.Data[offset] = stringOffset;
            page.MarkAsModified(offset);

        }
        catch (IOException ex)
        {
            throw new IOException("Error writing element", ex);
        }
        catch (OutOfMemoryException ex)
        {
            throw new OutOfMemoryException("Insufficient memory to write element", ex);
        }
         catch (IndexOutOfRangeException ex) 
        {
            throw new IndexOutOfRangeException($"Index {index} is out of bounds.", ex);
        }
         catch (Exception ex)
        {
             throw new InvalidOperationException($"An unexpected error occurred while writing element {index}.", ex);
        }
    }

    public void FlushModifiedPages()
    {
        try
        {
            foreach (var page in _pagesInMemory.Values.Where(p => p.Modified).ToList()) 
            {
                var bitmapBytes = ConvertBitArray(page.BitMap);
                var dataBytes = SerializeData(page.Data);
                _pageFileHandler.WritePage(page.AbsolutePageNumber, bitmapBytes, dataBytes);
                page.Modified = false;
            }
        }
        catch (IOException ex)
        {
            throw new IOException("Error flushing modified pages", ex);
        }
    }

    public long CalculateAbsolutePageNumber(long index) => index / AddressesPerPage;

    public void Dispose()
    {
        try
        {
            FlushModifiedPages();
            _pageFileHandler.Dispose();
            _stringFileHandler.Dispose();
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Error during Dispose: {ex.Message}");
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    private (long pageNumber, int offset) CalculateIndices(long index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("Index cannot be negative");

        long pageNumber = index / AddressesPerPage;
        int offset = (int)(index % AddressesPerPage);

        if (offset < 0 || offset >= AddressesPerPage)
             throw new IndexOutOfRangeException($"Calculated offset {offset} is out of page bounds (0-{AddressesPerPage - 1}).");


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

            if (page.BitMap.Length != AddressesPerPage || page.Data.Length != AddressesPerPage)
            {
                 throw new InvalidOperationException($"Loaded page {pageNumber} has incorrect dimensions.");
            }

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
        catch (IOException ex)
        {
            throw new IOException($"Failed to load page {pageNumber}", ex);
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

        try
        {
            // Only write back if modified
            if (pageToEvict.Modified)
            {
                var bitmapBytes = ConvertBitArray(pageToEvict.BitMap);
                var dataBytes = SerializeData(pageToEvict.Data);
                _pageFileHandler.WritePage(pageToEvict.AbsolutePageNumber, bitmapBytes, dataBytes);
                pageToEvict.Modified = false; // Reset modified flag after flushing
            }

            _pagesInMemory.Remove(pageToEvict.AbsolutePageNumber);
        }
        catch (IOException ex)
        {
            throw new IOException("Error evicting page", ex);
        }
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
        int expectedSize = AddressesPerPage * _addressSerializer.Size;
        if (bytes.Length != expectedSize)
             throw new InvalidOperationException($"Invalid data length for deserialization. Expected {expectedSize}, got {bytes.Length}.");


        long[] result = new long[AddressesPerPage]; 
        for (int i = 0; i < result.Length; i++)
        {
            var span = new ReadOnlySpan<byte>(bytes, i * _addressSerializer.Size, _addressSerializer.Size);
            result[i] = _addressSerializer.Deserialize(span);
        }
        return result;
    }
}