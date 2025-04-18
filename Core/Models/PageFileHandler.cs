namespace VirtualMemory.Models;

using System;
using System.IO;
using System.Text;
using VirtualMemory.Interfaces;

public class PageFileHandler : IFileHandler
{
    private const string Signature = "VM";
    private FileStream? _fileStream;
    private readonly int _pageSize; 
    private readonly int _elementsPerPage;
    private readonly int _elementSize; 
    private readonly int _calculatedBitmapSize;
    private readonly int _calculatedPageDataSize;
    private bool _disposed;

    public PageFileHandler(int elementsPerPage, int elementSize)
    {
        if (elementsPerPage <= 0)
            throw new ArgumentException("Elements per page must be positive", nameof(elementsPerPage));
        if (elementSize <= 0)
            throw new ArgumentException("Element size must be positive", nameof(elementSize));

        _elementsPerPage = elementsPerPage;
        _elementSize = elementSize;

        _calculatedBitmapSize = (_elementsPerPage + 7) / 8;
        _calculatedPageDataSize = _elementsPerPage * _elementSize;

        _pageSize = _calculatedBitmapSize + _calculatedPageDataSize;

        if (_pageSize <= 0)
             throw new InvalidOperationException("Calculated page size must be positive.");
    }

    public void CreateOrOpen(string filename)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PageFileHandler));

        bool isNewFile = !File.Exists(filename);

        try
        {
            _fileStream = new FileStream(
                filename,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.RandomAccess);

            if (isNewFile)
            {
                var signature = Encoding.ASCII.GetBytes(Signature);
                _fileStream.Write(signature, 0, signature.Length);

                // Initialize the first page with zeros based on the calculated page size
                _fileStream.SetLength(2 + _pageSize); // Signature + one page
                var zeroBuffer = new byte[_pageSize];
                _fileStream.Write(zeroBuffer, 0, zeroBuffer.Length);
            }
            else
            {
                // Validate signature for existing files
                var buffer = new byte[2];
                if (_fileStream.Read(buffer, 0, 2) != 2 ||
                    Encoding.ASCII.GetString(buffer) != Signature)
                {
                    throw new InvalidDataException("Invalid file signature");
                }
            }
        }
        catch
        {
            _fileStream?.Dispose();
            throw;
        }
    }

    public (byte[] bitmap, byte[] data) ReadPage(long pageNumber)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PageFileHandler));
        if (_fileStream == null)
            throw new InvalidOperationException("File not opened");

        var offset = 2 + pageNumber * _pageSize; 
        long requiredLength = offset + _pageSize;         
        if (_fileStream.Length < requiredLength)
        {
            // Ensure the returned data array matches the expected size
             return (new byte[_calculatedBitmapSize], new byte[_calculatedPageDataSize]);
        }

        _fileStream.Seek(offset, SeekOrigin.Begin);

        var bitmap = new byte[_calculatedBitmapSize];
        int bytesReadBitmap = _fileStream.Read(bitmap, 0, _calculatedBitmapSize);
        if (bytesReadBitmap != _calculatedBitmapSize)
             throw new IOException($"Failed to read full bitmap for page {pageNumber}. Expected {_calculatedBitmapSize}, got {bytesReadBitmap}.");


        var data = new byte[_calculatedPageDataSize];
        int bytesReadData = _fileStream.Read(data, 0, _calculatedPageDataSize);
         if (bytesReadData != _calculatedPageDataSize)
            throw new IOException($"Failed to read full page data for page {pageNumber}. Expected {_calculatedPageDataSize}, got {bytesReadData}.");


        return (bitmap, data);
    }

    public void WritePage(long pageNumber, byte[] bitmap, byte[] data)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PageFileHandler));
        if (_fileStream == null)
            throw new InvalidOperationException("File not opened");
        if (bitmap == null || data == null)
            throw new ArgumentNullException();

        if (bitmap.Length != _calculatedBitmapSize)
             throw new ArgumentException($"Invalid bitmap size. Expected {_calculatedBitmapSize}, got {bitmap.Length}.");
        if (data.Length != _calculatedPageDataSize)
             throw new ArgumentException($"Invalid data size. Expected {_calculatedPageDataSize}, got {data.Length}.");


        var offset = 2 + pageNumber * _pageSize; 
        long requiredLength = offset + _pageSize;         

        if (_fileStream.Length < requiredLength)
        {
            _fileStream.SetLength(requiredLength);
        }

        _fileStream.Seek(offset, SeekOrigin.Begin);
        _fileStream.Write(bitmap, 0, bitmap.Length);
        _fileStream.Write(data, 0, data.Length);

        _fileStream.Flush(); // Ensure data is written to disk
    }

    public void DeleteFile()
    {
        if (_disposed || _fileStream == null)
            return;

        string? path = null;
        try
        {
            path = _fileStream.Name;
        }
        finally
        {
            Dispose();
            if (path != null && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _fileStream?.Flush();
            _fileStream?.Dispose();
        }
        finally
        {
            _fileStream = null;
            _disposed = true;
        }
    }
}