using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SqlAssist.Core.Assistant
{
    public sealed class OllamaChatProvider : HttpAiProvider
    {
        private readonly string _root;
        public OllamaChatProvider(AiProviderSettings settings, HttpClient client) : base(settings, null, client)
        {
            if (!Uri.TryCreate(settings.BaseUrl?.Trim(), UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) throw new AiException("url");
            _root = uri.AbsoluteUri.TrimEnd('/') + "/";
        }
        protected override bool IsNdjson => true;
        protected override string DiscoveryPath(string page) => _root + "api/tags";
        protected override string ChatPath(bool stream) => _root + "api/chat";
        protected override string RequestBody(IReadOnlyList<AiMessage> messages, bool stream, bool test) => AiWire.Serialize(new WireChatRequest
        {
            Model = Settings.ModelId.Trim(), Stream = stream, Options = test ? new OllamaOptions { NumPredict = 64 } : null,
            Messages = messages.Select(m => new WireMessage { Role = m.Role, Content = m.Text }).ToArray()
        });
        protected override IEnumerable<string> ReadModels(XElement root) => (root.Element("models")?.Elements() ?? Enumerable.Empty<XElement>()).Select(m => AiWire.Value(m, "name"));
        protected override async Task ValidateModelAsync(CancellationToken ct)
        {
            var models = await ListModelsAsync(ct).ConfigureAwait(false);
            if (!models.Any(m => string.Equals(m.Id, Settings.ModelId.Trim(), StringComparison.Ordinal))) throw new AiException("model");
        }
        protected override bool ReadChunk(XElement root, bool streaming, Action<string> append)
        {
            append(AiWire.Value(root.Element("message"), "content"));
            if (AiWire.Value(root, "done") != "true") return false;
            string reason = AiWire.Value(root, "done_reason");
            if (!string.IsNullOrEmpty(reason) && reason != "stop") throw new AiException("blocked");
            return true;
        }
    }
}
