using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SqlAssist.Core.Assistant
{
    public static class AiProviderFactory
    {
        private static readonly HttpClient Shared = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        public static IAiChatProvider Create(AiProviderKind kind, AiProviderSettings settings, string key, HttpClient client = null)
        {
            switch (kind)
            {
                case AiProviderKind.OpenRouter: return new OpenRouterChatProvider(settings, key, client ?? Shared);
                case AiProviderKind.Ollama: return new OllamaChatProvider(settings, client ?? Shared);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    /// <summary>Owns each response/stream, but borrows a long-lived HttpClient. Cancellation closes reads even on net48.</summary>
    public abstract class HttpAiProvider : IAiChatProvider
    {
        protected readonly AiProviderSettings Settings;
        private readonly HttpClient _client;
        private readonly string _key;
        protected virtual string KeyHeader => null;
        protected HttpAiProvider(AiProviderSettings settings, string key, HttpClient client) { Settings = settings.Copy(); _key = key; _client = client; }
        protected abstract string DiscoveryPath(string page);
        protected abstract string ChatPath(bool stream);
        protected abstract string RequestBody(IReadOnlyList<AiMessage> messages, bool stream, bool test);
        protected abstract IEnumerable<string> ReadModels(XElement root);
        protected abstract bool ReadChunk(XElement root, bool streaming, Action<string> append);
        protected virtual string NextPage(XElement root) => null;
        protected virtual bool IsNdjson => false;
        protected virtual Task ValidateModelAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct) => GuardAsync(30, ct, ListCoreAsync);
        private async Task<IReadOnlyList<AiModel>> ListCoreAsync(CancellationToken ct)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var pages = new HashSet<string>(StringComparer.Ordinal);
            string page = null;
            do
            {
                using (var request = Request(HttpMethod.Get, DiscoveryPath(page)))
                using (var response = await SendAsync(request, ct).ConfigureAwait(false))
                {
                    var root = AiWire.Parse(await ReadBoundedAsync(response, ct).ConfigureAwait(false));
                    AiWire.CheckError(root);
                    foreach (string id in ReadModels(root)) if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
                    page = NextPage(root);
                }
                if (!string.IsNullOrEmpty(page) && (!pages.Add(page) || pages.Count > 100)) throw new AiException("provider");
            } while (!string.IsNullOrEmpty(page));
            return ids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => new AiModel(x)).ToList().AsReadOnly();
        }
        public async Task TestConnectionAsync(CancellationToken ct)
        {
            await GuardAsync(60, ct, async token => { await GenerateCoreAsync(new[] { new AiMessage("user", "Reply only OK.") }, s => { }, token, true).ConfigureAwait(false); return true; }).ConfigureAwait(false);
        }
        public async Task GenerateAsync(IReadOnlyList<AiMessage> messages, Action<string> onText, CancellationToken ct)
        {
            await GuardAsync(180, ct, async token => { await GenerateCoreAsync(messages, onText, token, false).ConfigureAwait(false); return true; }).ConfigureAwait(false);
        }
        private async Task GenerateCoreAsync(IReadOnlyList<AiMessage> messages, Action<string> onText, CancellationToken ct, bool test)
        {
            if (string.IsNullOrWhiteSpace(Settings.ModelId)) throw new AiException("model");
            await ValidateModelAsync(ct).ConfigureAwait(false);
            bool streaming = Settings.UseStreaming && !test;
            int received = 0;
            Action<string> append = text =>
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(text)) return;
                if (received + text.Length > 128000) throw new AiException("limit");
                received += text.Length;
                onText(text);
            };
            using (var request = Request(HttpMethod.Post, ChatPath(streaming)))
            {
                request.Content = new StringContent(RequestBody(messages, streaming, test), Encoding.UTF8, "application/json");
                using (var response = await SendAsync(request, ct).ConfigureAwait(false))
                {
                    bool complete;
                    if (!streaming)
                    {
                        var root = AiWire.Parse(await ReadBoundedAsync(response, ct).ConfigureAwait(false));
                        AiWire.CheckError(root);
                        complete = ReadChunk(root, false, append);
                    }
                    else complete = await ReadStreamAsync(response, append, ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (!complete) throw new AiException("incomplete");
                    if (received == 0) throw new AiException("blocked");
                }
            }
        }
        private HttpRequestMessage Request(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            if (KeyHeader != null)
            {
                if (string.IsNullOrWhiteSpace(_key)) { request.Dispose(); throw new AiException("key"); }
                if (KeyHeader == "Authorization") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key.Trim());
                else request.Headers.Add(KeyHeader, _key.Trim());
            }
            return request;
        }
        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                using (response)
                {
                    int status = (int)response.StatusCode;
                    if (status == 429)
                    {
                        try
                        {
                            var root = AiWire.Parse(await ReadBoundedAsync(response, ct).ConfigureAwait(false));
                            if (AiWire.Value(root.Element("error"), "status") == "RESOURCE_EXHAUSTED") status = 402;
                        }
                        catch (Exception) when (!ct.IsCancellationRequested) { /* Fall back to safe HTTP classification; never retain the body. */ }
                    }
                    throw AiException.FromStatus(status);
                }
            }
            return response;
        }
        private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken ct)
        {
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (ct.Register(() => stream.Dispose()))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var text = new StringBuilder();
                var buffer = new char[4096];
                int read;
                while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
                {
                    ct.ThrowIfCancellationRequested();
                    if (text.Length + read > 8 * 1024 * 1024) throw new AiException("limit");
                    text.Append(buffer, 0, read);
                }
                ct.ThrowIfCancellationRequested();
                return text.ToString();
            }
        }
        private async Task<bool> ReadStreamAsync(HttpResponseMessage response, Action<string> append, CancellationToken ct)
        {
            bool complete = false, end = false;
            var data = new StringBuilder();
            Action<string> consume = json =>
            {
                ct.ThrowIfCancellationRequested();
                if (json == "[DONE]") { complete = true; end = true; return; }
                var root = AiWire.Parse(json);
                AiWire.CheckError(root);
                if (ReadChunk(root, true, append)) { complete = true; end = true; }
            };
            Action<string> line = value =>
            {
                if (IsNdjson) { if (!string.IsNullOrWhiteSpace(value)) consume(value); return; }
                if (value.Length == 0)
                {
                    if (data.Length > 0) { string json = data.ToString(); data.Clear(); consume(json); }
                }
                else if (value.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (data.Length > 0) data.Append('\n');
                    string field = value.Substring(5);
                    data.Append(field.StartsWith(" ", StringComparison.Ordinal) ? field.Substring(1) : field);
                    if (data.Length > 1024 * 1024) throw new AiException("limit");
                }
            };
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (ct.Register(() => stream.Dispose()))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var pending = new StringBuilder();
                var chars = new char[4096];
                int read;
                while (!end && (read = await reader.ReadAsync(chars, 0, chars.Length).ConfigureAwait(false)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    for (int i = 0; i < read && !end; i++)
                    {
                        if (chars[i] == '\n') { line(pending.ToString().TrimEnd('\r')); pending.Clear(); }
                        else { pending.Append(chars[i]); if (pending.Length > 1024 * 1024) throw new AiException("limit"); }
                    }
                }
                if (!end && pending.Length > 0) line(pending.ToString().TrimEnd('\r'));
                if (!end && data.Length > 0) consume(data.ToString());
            }
            return complete;
        }
        private static async Task<T> GuardAsync<T>(int seconds, CancellationToken ct, Func<CancellationToken, Task<T>> work)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
                try { return await work(timeout.Token).ConfigureAwait(false); }
                catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
                catch (Exception) when (timeout.IsCancellationRequested) { throw new AiException("timeout"); }
                catch (AiException) { throw; }
                catch (OperationCanceledException) { throw new AiException("timeout"); }
                catch (HttpRequestException) { throw new AiException("network"); }
                catch (IOException) { throw new AiException("network"); }
                catch (Exception) { throw new AiException("provider"); }
            }
        }
    }
}
