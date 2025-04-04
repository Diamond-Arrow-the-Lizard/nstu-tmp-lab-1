namespace VirtualMemory.Models;

using System.Collections;
using VirtualMemory.Interfaces;
using System.Linq;

public class VirtualMemoryManager<T> : IVirtualMemoryManager<T>
{
    private readonly int _pageSize;
    private readonly int _elementsPerPage;
    private readonly int _bufferSize;
    private readonly ISerializer<T> _serializer;
    private readonly IFileHandler _fileHandler;

    private readonly Dictionary<long, IPage<T>> _pagesInMemory = new();

    public VirtualMemoryManager(int pageSize, int bufferSize, ISerializer<T> serializer, IFileHandler fileHandler)
    {
        _pageSize = pageSize;
        _bufferSize = bufferSize;
        _serializer = serializer;
        _fileHandler = fileHandler;

        if (pageSize % serializer.Size != 0)
            throw new ArgumentException("Page size must be divisible by element size.");

        _elementsPerPage = pageSize / serializer.Size;
    }

    public int BufferSize => _bufferSize;

    public T ReadElement(long index)
    {
        if (index < 0)
        {
            throw new IndexOutOfRangeException("Index cannot be negative.");
        }

        if (index >= _elementsPerPage * _bufferSize)
        {
            throw new IndexOutOfRangeException("Index is out of range.");
        }

        long pageNumber = CalculateAbsolutePageNumber(index);
        int offset = (int)(index % _elementsPerPage);
        var page = GetOrLoadPage(pageNumber);
        return page.Data[offset];
    }

    public void WriteElement(long index, T value)
    {
        if (index < 0)
        {
            throw new IndexOutOfRangeException("Index cannot be negative.");
        }

        if (index >= _elementsPerPage * _bufferSize)
        {
            throw new IndexOutOfRangeException("Index is out of range.");
        }

        long pageNumber = CalculateAbsolutePageNumber(index);
        int offset = (int)(index % _elementsPerPage);
        var page = GetOrLoadPage(pageNumber);
        
        page.Data[offset] = value;
        page.MarkAsModified(offset);
        page.Modified = true;
    }

    public void FlushModifiedPages()
    {
        foreach (var page in _pagesInMemory.Values.Where(p => p.Modified))
        {
            var bitmapBytes = ToByteArray(page.BitMap);
            var dataBytes = SerializeData(page.Data);
            _fileHandler.WritePage(page.AbsolutePageNumber, bitmapBytes, dataBytes);
            page.Modified = false;
        }
    }

    public long CalculateAbsolutePageNumber(long index)
    {
        if (index < 0)
        {
            throw new IndexOutOfRangeException("Index cannot be negative.");
        }

        return index / _elementsPerPage;
    }

    public void Dispose()
    {
        FlushModifiedPages();
        _pagesInMemory.Clear();
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
            if (bitmapBytes == null || dataBytes == null || bitmapBytes.Length == 0 || dataBytes.Length == 0)
            {
                throw new Exception("Corrupted or empty page data.");
            }

            var bitmap = new BitArray(bitmapBytes);
            var data = DeserializeData(dataBytes);

            var page = new Page<T>(_pageSize)
            {
                AbsolutePageNumber = pageNumber,
                BitMap = bitmap,
                Data = data,
                LastAccessTime = DateTime.Now
            };

            _pagesInMemory[pageNumber] = page;
            return page;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to load page {pageNumber}, initializing empty page. Error: {ex.Message}");

            var emptyPage = new Page<T>(_pageSize)
            {
                AbsolutePageNumber = pageNumber,
                BitMap = new BitArray(_elementsPerPage),
                Data = new T[_elementsPerPage],
                LastAccessTime = DateTime.Now
            };

            _pagesInMemory[pageNumber] = emptyPage;
            return emptyPage;
        }
    }

    private void EvictPage()
    {
        var pageToEvict = _pagesInMemory.Values
            .OrderBy(p => p.LastAccessTime)
            .First();

        if (pageToEvict.Modified)
        {
            var bitmapBytes = ToByteArray(pageToEvict.BitMap);
            var dataBytes = SerializeData(pageToEvict.Data);
            _fileHandler.WritePage(pageToEvict.AbsolutePageNumber, bitmapBytes, dataBytes);
        }

        if (pageToEvict is IDisposable disposablePage)
        {
            disposablePage.Dispose();
        }

        _pagesInMemory.Remove(pageToEvict.AbsolutePageNumber);
    }

    private byte[] ToByteArray(BitArray bits)
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
        int count = bytes.Length / _serializer.Size;
        T[] result = new T[count];
        for (int i = 0; i < count; i++)
        {
            var span = new ReadOnlySpan<byte>(bytes, i * _serializer.Size, _serializer.Size);
            result[i] = _serializer.Deserialize(span);
        }
        return result;
    }
}