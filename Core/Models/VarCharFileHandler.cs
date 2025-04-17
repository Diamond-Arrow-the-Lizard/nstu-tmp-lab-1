using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace VirtualMemory.Models;

public class VarCharFileHandler : IDisposable
{
    private FileStream? _fileStream;
    private bool _disposed;

    public VarCharFileHandler() { }

    public void CreateOrOpen(string filename)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VarCharFileHandler));

        _fileStream = new FileStream(
            filename,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.RandomAccess);
    }

    public void WriteString(long offset, string value)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VarCharFileHandler));
        if (_fileStream == null)
            throw new InvalidOperationException("File not opened");

        byte[] stringBytes = Encoding.UTF8.GetBytes(value);
        int length = stringBytes.Length;
        byte[] lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, length);

        _fileStream.Seek(offset, SeekOrigin.Begin);
        _fileStream.Write(lengthBytes, 0, 4);
        _fileStream.Write(stringBytes, 0, length);
    }

    public (string value, long nextOffset) ReadString(long offset)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VarCharFileHandler));
        if (_fileStream == null)
            throw new InvalidOperationException("File not opened");

        _fileStream.Seek(offset, SeekOrigin.Begin);

        byte[] lengthBytes = new byte[4];
        if (_fileStream.Read(lengthBytes, 0, 4) != 4)
            throw new EndOfStreamException("Could not read string length");

        int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        byte[] stringBytes = new byte[length];

        if (_fileStream.Read(stringBytes, 0, length) != length)
            throw new EndOfStreamException("Could not read string data");

        string value = Encoding.UTF8.GetString(stringBytes);
        long nextOffset = offset + 4 + length; // Offset для следующей строки
        return (value, nextOffset);
    }

    public long AppendString(string value)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VarCharFileHandler));
        if (_fileStream == null)
            throw new InvalidOperationException("File not opened");

        long offset = _fileStream.Length;
        WriteString(offset, value);
        return offset;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _fileStream?.Dispose();
        _fileStream = null;
        _disposed = true;
    }
}