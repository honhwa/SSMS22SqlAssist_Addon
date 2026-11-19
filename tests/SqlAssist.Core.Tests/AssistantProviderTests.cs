using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SqlAssist.Core.Assistant;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class AssistantProviderTests
    {
        // Test-only HTTP fixture. Production request building/parsing is always exercised.
        private sealed class Fixture : HttpMessageHandler
        {
            public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply;
            public bool SupplyInstalledModel;
            public readonly List<string> Bodies = new List<string>();
            public readonly List<string> Urls = new List<string>();
            public readonly List<string> Auth = new List<string>();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (SupplyInstalledModel && request.RequestUri.AbsolutePath.EndsWith("/api/tags"))
                    return Response("{\"models\":[{\"name\":\"example-model\"}]}");
                Urls.Add(request.RequestUri.AbsoluteUri);
                Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct));
                Auth.Add(request.Headers.Authorization?.ToString() ?? "");
                return await Reply(request, ct);
            }
        }
        private static HttpResponseMessage Response(string body, string type = "application/json", int status = 200) =>
            new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, type) };
        private static IAiChatProvider Provider(AiProviderKind kind, HttpClient client, bool streaming = true) =>
            AiProviderFactory.Create(kind, new AiProviderSettings { ModelId = "example-model", BaseUrl = "http://localhost:11434/prefix/", UseStreaming = streaming }, "test-secret", client);

        [Theory]
        [InlineData(AiProviderKind.OpenRouter)]
        [InlineData(AiProviderKind.Ollama)]
        public async Task MapsConversationAndAuthWithoutSendingSecretsInBody(AiProviderKind kind)
        {
            var fixture = new Fixture { SupplyInstalledModel = true, Reply = (r, ct) => Task.FromResult(Response(kind == AiProviderKind.OpenRouter ? "{\"choices\":[{\"message\":{\"content\":\"answer\"},\"finish_reason\":\"stop\"}]}"
                : "{\"message\":{\"content\":\"answer\"},\"done\":true}")) };
            using var client = new HttpClient(fixture);
            string result = "";
            await Provider(kind, client, false).GenerateAsync(new[] { new AiMessage("user", "SQL ไทย"), new AiMessage("assistant", "previous"), new AiMessage("user", "next") }, s => result += s, CancellationToken.None);
            Assert.Equal("answer", result);
            Assert.DoesNotContain("test-secret", fixture.Bodies.Single());
            Assert.DoesNotContain("test-secret", fixture.Urls.Single());
            Assert.Equal(kind == AiProviderKind.Ollama ? "" : "Bearer test-secret", fixture.Auth.Single());
            using var json = JsonDocument.Parse(fixture.Bodies.Single());
            var messages = json.RootElement.GetProperty("messages");
            Assert.Equal(3, messages.GetArrayLength());
            Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
            if (kind == AiProviderKind.Ollama) Assert.EndsWith("/prefix/api/chat", fixture.Urls.Single());
        }

        [Theory]
        [InlineData(AiProviderKind.OpenRouter, ": keep alive\r\ndata: {\"choices\":[{\"delta\":{\"content\":\"ไทย\"}}]}\r\n\r\ndata: [DONE]\r\n\r\n")]
        [InlineData(AiProviderKind.Ollama, "{\"message\":{\"content\":\"ไทย\"},\"done\":false}\n{\"done\":true}\n")]
        public async Task StreamsFragmentedUtf8AndChecksCompletion(AiProviderKind kind, string body)
        {
            var fixture = new Fixture { SupplyInstalledModel = true, Reply = (r, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new OneByteStream(Encoding.UTF8.GetBytes(body))) }) };
            using var client = new HttpClient(fixture);
            string output = "";
            await Provider(kind, client).GenerateAsync(new[] { new AiMessage("user", "hi") }, s => output += s, CancellationToken.None);
            Assert.Equal("ไทย", output);
        }

        [Fact]
        public async Task OpenRouterMultilineSseIsOneEvent()
        {
            var fixture = new Fixture { Reply = (r, ct) => Task.FromResult(Response("data: {\"choices\":\n" + "data: [{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n", "text/event-stream")) };
            using var client = new HttpClient(fixture);
            string output = "";
            await Provider(AiProviderKind.OpenRouter, client).GenerateAsync(new[] { new AiMessage("user", "hi") }, s => output += s, default);
            Assert.Equal("ok", output);
        }

        [Theory]
        [InlineData(401, "auth")]
        [InlineData(402, "quota")]
        [InlineData(429, "rate")]
        [InlineData(404, "model")]
        public async Task HttpErrorsNeverExposeRawBodiesOrRetry(int status, string code)
        {
            var fixture = new Fixture { Reply = (r, ct) => Task.FromResult(Response("test-secret sensitive data", status: status)) };
            using var client = new HttpClient(fixture);
            var error = await Assert.ThrowsAsync<AiException>(() => Provider(AiProviderKind.OpenRouter, client).GenerateAsync(new[] { new AiMessage("user", "hi") }, s => { }, default));
            Assert.Equal(code, error.Code);
            Assert.DoesNotContain("test-secret", error.ToString());
            Assert.Null(error.InnerException);
            Assert.Single(fixture.Urls);
        }

        [Theory]
        [InlineData(AiProviderKind.OpenRouter, "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\ndata: {\"error\":{\"code\":429,\"message\":\"test-secret\"}}\n\n", "rate")]
        [InlineData(AiProviderKind.Ollama, "{\"message\":{\"content\":\"partial\"}}\n{\"error\":\"test-secret\"}\n", "provider")]
        [InlineData(AiProviderKind.OpenRouter, "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n", "incomplete")]
        public async Task PartialErrorsPreserveTextWithoutRetry(AiProviderKind kind, string body, string code)
        {
            var fixture = new Fixture { SupplyInstalledModel = true, Reply = (r, ct) => Task.FromResult(Response(body)) };
            using var client = new HttpClient(fixture);
            string output = "";
            var error = await Assert.ThrowsAsync<AiException>(() => Provider(kind, client).GenerateAsync(new[] { new AiMessage("user", "hi") }, s => output += s, default));
            Assert.Equal("partial", output);
            Assert.Equal(code, error.Code);
            Assert.DoesNotContain("test-secret", error.ToString());
            Assert.Single(fixture.Urls);
        }

        [Theory]
        [InlineData(AiProviderKind.OpenRouter, "{\"data\":[{\"id\":\"vendor/model\"}]}", "vendor/model")]
        [InlineData(AiProviderKind.Ollama, "{\"models\":[{\"name\":\"local:latest\"}]}", "local:latest")]
        public async Task DiscoversActualModelIds(AiProviderKind kind, string body, string id)
        {
            var fixture = new Fixture { Reply = (r, ct) => Task.FromResult(Response(body)) };
            using var client = new HttpClient(fixture);
            Assert.Equal(id, (await Provider(kind, client).ListModelsAsync(default)).Single().Id);
        }

        [Fact]
        public async Task OllamaRefusesUnknownModelBeforeGeneration()
        {
            var fixture = new Fixture { Reply = (r, ct) => Task.FromResult(Response("{\"models\":[{\"name\":\"installed\"}]}")) };
            using var client = new HttpClient(fixture);
            var error = await Assert.ThrowsAsync<AiException>(() => Provider(AiProviderKind.Ollama, client).GenerateAsync(new[] { new AiMessage("user", "hi") }, s => { }, default));
            Assert.Equal("model", error.Code);
            Assert.EndsWith("/api/tags", fixture.Urls.Single());
        }

        [Theory]
        [InlineData("http://user:password@host")]
        [InlineData("http://host?key=secret")]
        [InlineData("file:///C:/data")]
        public void RejectsUnsafeOllamaUrls(string url)
        {
            Assert.Throws<AiException>(() => AiProviderFactory.Create(AiProviderKind.Ollama, new AiProviderSettings { BaseUrl = url }, ""));
        }

        [Fact]
        public async Task CancellationBeforeHeadersStopsRequest()
        {
            var fixture = new Fixture { Reply = async (r, ct) => { await Task.Delay(Timeout.Infinite, ct); return Response(""); } };
            using var client = new HttpClient(fixture);
            using var cts = new CancellationTokenSource();
            var task = Provider(AiProviderKind.OpenRouter, client).GenerateAsync(new[] { new AiMessage("user", "hi") }, s => { }, cts.Token);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }

        [Fact]
        public async Task CancellationDuringBlockedReadClosesStreamAndDoesNotAppendAgain()
        {
            var stream = new BlockingStream();
            var fixture = new Fixture { Reply = (r, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }) };
            using var client = new HttpClient(fixture);
            using var cts = new CancellationTokenSource();
            string output = "";
            var task = Provider(AiProviderKind.OpenRouter, client).GenerateAsync(new[] { new AiMessage("user", "hi") }, s => output += s, cts.Token);
            await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(stream.WasDisposed);
            Assert.Equal("", output);
        }

        [Fact]
        public async Task TestConnectionRequiresActualTextAndUsesSelectedModel()
        {
            var fixture = new Fixture { Reply = (r, ct) => Task.FromResult(Response("{\"choices\":[{\"message\":{\"content\":\"\"},\"finish_reason\":\"stop\"}]}")) };
            using var client = new HttpClient(fixture);
            var error = await Assert.ThrowsAsync<AiException>(() => Provider(AiProviderKind.OpenRouter, client).TestConnectionAsync(default));
            Assert.Equal("blocked", error.Code);
            using var json = JsonDocument.Parse(fixture.Bodies.Single());
            Assert.Equal("example-model", json.RootElement.GetProperty("model").GetString());
            Assert.False(json.RootElement.GetProperty("stream").GetBoolean());
            Assert.Equal(64, json.RootElement.GetProperty("max_tokens").GetInt32());
        }

        [Fact]
        public async Task MissingKeyFailsBeforeSendingAnything()
        {
            var fixture = new Fixture { Reply = (r, ct) => throw new Exception("Network should not be reached") };
            using var client = new HttpClient(fixture);
            var provider = AiProviderFactory.Create(AiProviderKind.OpenRouter, new AiProviderSettings { ModelId = "vendor/example" }, null, client);
            var error = await Assert.ThrowsAsync<AiException>(() => provider.TestConnectionAsync(default));
            Assert.Equal("key", error.Code);
            Assert.Empty(fixture.Urls);
        }

        [Fact]
        public async Task QuotaResponseIsClassifiedWithoutLeakingItsMessage()
        {
            var fixture = new Fixture { Reply = (r, ct) => Task.FromResult(Response("{\"error\":{\"code\":429,\"status\":\"RESOURCE_EXHAUSTED\",\"message\":\"test-secret quota detail\"}}", status: 429)) };
            using var client = new HttpClient(fixture);
            var error = await Assert.ThrowsAsync<AiException>(() => Provider(AiProviderKind.OpenRouter, client).TestConnectionAsync(default));
            Assert.Equal("quota", error.Code);
            Assert.DoesNotContain("test-secret", error.ToString());
        }

        [Fact]
        public async Task TransportTimeoutIsReportedAsTimeout()
        {
            var fixture = new Fixture { Reply = async (r, ct) => { await Task.Delay(Timeout.Infinite, ct); return Response(""); } };
            using var client = new HttpClient(fixture) { Timeout = TimeSpan.FromMilliseconds(25) };
            var error = await Assert.ThrowsAsync<AiException>(() => Provider(AiProviderKind.OpenRouter, client).TestConnectionAsync(default));
            Assert.Equal("timeout", error.Code);
        }

        private sealed class BlockingStream : Stream
        {
            public readonly TaskCompletionSource<bool> Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<int> _read = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool WasDisposed;
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) { Started.TrySetResult(true); return _read.Task; }
            protected override void Dispose(bool disposing) { WasDisposed = true; _read.TrySetException(new ObjectDisposedException("test stream")); base.Dispose(disposing); }
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

        private sealed class OneByteStream : MemoryStream
        {
            public OneByteStream(byte[] bytes) : base(bytes) { }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => base.ReadAsync(buffer, offset, Math.Min(1, count), ct);
        }
    }
}
