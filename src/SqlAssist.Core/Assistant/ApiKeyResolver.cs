using System;

namespace SqlAssist.Core.Assistant
{
    public interface IApiKeyStore
    {
        string Read(AiProviderKind provider);
        void Save(AiProviderKind provider, string value);
        void Delete(AiProviderKind provider);
    }
    public sealed class ApiKeyResolver
    {
        private readonly IApiKeyStore _store;
        private readonly Func<string, string> _environment;
        public ApiKeyResolver(IApiKeyStore store, Func<string, string> environment = null) { _store = store; _environment = environment ?? Environment.GetEnvironmentVariable; }
        private const string KeyVariable = "OPENROUTER_API_KEY";
        public string Get(AiProviderKind kind)
        {
            if (kind == AiProviderKind.Ollama) return null;
            string value = _environment(KeyVariable);
            return !string.IsNullOrWhiteSpace(value) ? value.Trim() : _store.Read(kind);
        }
        public string Source(AiProviderKind kind)
        {
            if (kind == AiProviderKind.Ollama) return "Local/remote Ollama: ไม่ใช้ API Key";
            if (!string.IsNullOrWhiteSpace(_environment(KeyVariable))) return "Environment: " + KeyVariable;
            return !string.IsNullOrEmpty(_store.Read(kind)) ? "Windows Credential Manager" : "ยังไม่มี API Key";
        }
    }
}
