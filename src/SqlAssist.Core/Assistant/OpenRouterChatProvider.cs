using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Xml.Linq;

namespace SqlAssist.Core.Assistant
{
    public sealed class OpenRouterChatProvider : HttpAiProvider
    {
        private const string Root = "https://openrouter.ai/api/v1/";
        public OpenRouterChatProvider(AiProviderSettings settings, string key, HttpClient client) : base(settings, key, client) { }
        protected override string KeyHeader => "Authorization";
        protected override string DiscoveryPath(string page) => Root + "models";
        protected override string ChatPath(bool stream) => Root + "chat/completions";
        protected override string RequestBody(IReadOnlyList<AiMessage> messages, bool stream, bool test) => AiWire.Serialize(new WireChatRequest
        {
            Model = Settings.ModelId.Trim(), Stream = stream, MaxTokens = test ? 64 : 0,
            Messages = messages.Select(m => new WireMessage { Role = m.Role, Content = m.Text }).ToArray()
        });
        protected override IEnumerable<string> ReadModels(XElement root) => (root.Element("data")?.Elements() ?? Enumerable.Empty<XElement>()).Select(m => AiWire.Value(m, "id"));
        protected override bool ReadChunk(XElement root, bool streaming, Action<string> append)
        {
            var choice = root.Element("choices")?.Elements().FirstOrDefault();
            append(AiWire.Value(choice?.Element(streaming ? "delta" : "message"), "content"));
            string finish = AiWire.Value(choice, "finish_reason");
            if (!string.IsNullOrEmpty(finish) && finish != "stop") throw new AiException("blocked");
            // The SSE [DONE] sentinel closes a streamed OpenRouter response, not a usage-only chunk.
            return !streaming && finish == "stop";
        }
    }
}
