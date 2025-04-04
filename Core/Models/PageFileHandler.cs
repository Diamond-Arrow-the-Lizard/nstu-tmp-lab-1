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
    private bool _disposed;

    public PageFileHandler(int pageSize, int elementsPerPage)
    {
        if (pageSize <= 0)
            throw new ArgumentException("Page size must be positive", nameof(pageSize));
        if (elementsPerPage <= 0)
            throw new ArgumentException("Elements per page must be positive", nameof(elementsPerPage));
        
        _pageSize = pageSize;
        _elementsPerPage = elementsPerPage;
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
                _fileStream.SetLength(_pageSize);

                var zeroBuffer = new byte[_pageSize];
                _fileStream.Write(zeroBuffer, 0, zeroBuffer.Length);
            }
            else
            {
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
        if (offset + _pageSize > _fileStream.Length)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        _fileStream.Seek(offset, SeekOrigin.Begin);
        
        int bitmapSize = (_elementsPerPage + 7) / 8;
        var bitmap = new byte[bitmapSize];
        if (_fileStream.Read(bitmap, 0, bitmapSize) != bitmapSize)
            throw new IOException("Failed to read bitmap");

        var data = new byte[_pageSize - bitmapSize];
        if (_fileStream.Read(data, 0, data.Length) != data.Length)
            throw new IOException("Failed to read page data");
        
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

        int expectedBitmapSize = (_elementsPerPage + 7) / 8;
        if (bitmap.Length != expectedBitmapSize || data.Length != _pageSize - expectedBitmapSize)
            throw new ArgumentException("Invalid page data size");

        var offset = 2 + pageNumber * _pageSize; 
        if (_fileStream.Length < offset + _pageSize)
        {
            _fileStream.SetLength(offset + _pageSize);
        }
        _fileStream.Seek(offset, SeekOrigin.Begin);
        _fileStream.Write(bitmap, 0, bitmap.Length);
        _fileStream.Write(data, 0, data.Length);
        _fileStream.Flush();
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