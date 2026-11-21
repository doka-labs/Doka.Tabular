// WHY: Shared sources compile in test applications with different global usings, so they import what they use; in a
// project with matching global usings these directives look redundant, which the cleanup must not act on.
// ReSharper disable RedundantUsingDirective

#pragma warning disable IDE0005

// WHY: Every test application links these helpers; each application uses only some of them.
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Doka.Tabular.Testing;

/// <summary>A readable or writable stream that rejects Seek, Position, and Length, like a network or SFTP stream.</summary>
internal sealed class ForwardOnlyStream : Stream
{
    private readonly Stream _inner;

    public ForwardOnlyStream(
        Stream inner
    )
    {
        _inner = inner;
    }

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => _inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public bool Disposed { get; private set; }

    public override void Flush() => _inner.Flush();

    public override int Read(
        byte[] buffer,
        int offset,
        int count
    ) => _inner.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    ) => _inner.ReadAsync(buffer, cancellationToken);

    public override long Seek(
        long offset,
        SeekOrigin origin
    ) => throw new NotSupportedException();

    public override void SetLength(
        long value
    ) => throw new NotSupportedException();

    public override void Write(
        byte[] buffer,
        int offset,
        int count
    ) => _inner.Write(buffer, offset, count);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    ) => _inner.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(
        bool disposing
    )
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>Byte and text helpers for test input.</summary>
internal static class TestInput
{
    public static MemoryStream Utf8(
        string text
    ) => new(Encoding.UTF8.GetBytes(text));

    public static MemoryStream Bytes(
        params byte[] bytes
    ) => new(bytes);

    public static string Utf8Text(
        Stream stream
    )
    {
        stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        return Encoding.UTF8.GetString(copy.ToArray());
    }

    public static byte[] AllBytes(
        Stream stream
    )
    {
        stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        return copy.ToArray();
    }
}

/// <summary>
/// A write-only target that rejects synchronous writes and flushes, like an ASP.NET Core response body, and counts
/// the bytes it received.
/// </summary>
internal sealed class AsyncOnlyStream : Stream
{
    public long Written { get; private set; }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => throw new InvalidOperationException("Synchronous operations are disallowed.");

    public override Task FlushAsync(
        CancellationToken cancellationToken
    ) => Task.CompletedTask;

    public override int Read(
        byte[] buffer,
        int offset,
        int count
    ) => throw new NotSupportedException();

    public override long Seek(
        long offset,
        SeekOrigin origin
    ) => throw new NotSupportedException();

    public override void SetLength(
        long value
    ) => throw new NotSupportedException();

    public override void Write(
        byte[] buffer,
        int offset,
        int count
    ) => throw new InvalidOperationException("Synchronous operations are disallowed.");

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        Written += buffer.Length;

        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    ) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken)
        .AsTask();
}
