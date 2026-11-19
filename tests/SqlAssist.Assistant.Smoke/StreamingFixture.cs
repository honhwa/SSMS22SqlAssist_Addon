using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Test-only remote boundary: emits a partial SSE response, then blocks until cancellation disposes it.</summary>
internal sealed class StreamingFixture : HttpMessageHandler
{
    public int Requests;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Requests);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new PendingStream()) });
    }
    private sealed class PendingStream : Stream
    {
        private readonly byte[] _prefix = Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n");
        private int _position;
        private readonly TaskCompletionSource<int> _pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
#pragma warning disable VSTHRD003 // Test stream deliberately remains pending until the production cancellation disposes it.
            if (_position == _prefix.Length) return _pending.Task;
#pragma warning restore VSTHRD003
            int length = Math.Min(count, _prefix.Length - _position);
            Array.Copy(_prefix, _position, buffer, offset, length); _position += length;
            return Task.FromResult(length);
        }
        protected override void Dispose(bool disposing) { _pending.TrySetException(new ObjectDisposedException("test stream")); base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
