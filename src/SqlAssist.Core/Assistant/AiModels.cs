using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SqlAssist.Core.Assistant
{
    // Keep persisted numeric IDs stable; 0 belonged to the removed provider.
    public enum AiProviderKind { OpenRouter = 1, Ollama = 2 }
    public enum AiMessageState { Complete, Responding, Stopped, Failed }

    public sealed class AiMessage
    {
        public string Role { get; }
        public string Text { get; internal set; }
        public AiMessageState State { get; internal set; }
        public AiMessage(string role, string text) { Role = role; Text = text ?? ""; }
        internal AiMessage Copy() => new AiMessage(Role, Text) { State = State };
    }

    public sealed class AiModel
    {
        public string Id { get; }
        public AiModel(string id) { Id = id; }
        public override string ToString() => Id;
    }

    public sealed class AiProviderSettings
    {
        public string ModelId { get; set; } = "";
        public string BaseUrl { get; set; } = "http://localhost:11434";
        public bool UseStreaming { get; set; } = true;
        public AiProviderSettings Copy() => (AiProviderSettings)MemberwiseClone();
    }

    /// <summary>Only non-secret configuration belongs in the XML settings file.</summary>
    public sealed class AssistantOptions
    {
        private readonly Dictionary<AiProviderKind, AiProviderSettings> _providers = new Dictionary<AiProviderKind, AiProviderSettings>
        { { AiProviderKind.OpenRouter, new AiProviderSettings() }, { AiProviderKind.Ollama, new AiProviderSettings() } };
        public AiProviderKind ActiveProvider { get; set; } = AiProviderKind.OpenRouter;
        public AiProviderSettings For(AiProviderKind kind) => _providers.TryGetValue(kind, out var settings) ? settings : throw new ArgumentOutOfRangeException(nameof(kind));
        public XElement ToXml()
        {
            var root = new XElement("Assistant", new XAttribute("Provider", ActiveProvider));
            foreach (AiProviderKind kind in Enum.GetValues(typeof(AiProviderKind)))
            {
                var value = For(kind);
                root.Add(new XElement(kind.ToString(), new XAttribute("Model", value.ModelId ?? ""),
                    new XAttribute("Streaming", value.UseStreaming),
                    kind == AiProviderKind.Ollama ? new XAttribute("BaseUrl", value.BaseUrl ?? "") : null));
            }
            return root;
        }
        public void ReadXml(XElement root)
        {
            if (root == null) return;
            ActiveProvider = AiProviderKind.OpenRouter;
            if (Enum.TryParse((string)root.Attribute("Provider"), out AiProviderKind active) && Enum.IsDefined(typeof(AiProviderKind), active)) ActiveProvider = active;
            foreach (AiProviderKind kind in Enum.GetValues(typeof(AiProviderKind)))
            {
                var element = root.Element(kind.ToString());
                if (element == null) continue;
                var value = For(kind);
                value.ModelId = ((string)element.Attribute("Model") ?? "").Trim();
                if (bool.TryParse((string)element.Attribute("Streaming"), out bool streaming)) value.UseStreaming = streaming;
                if (kind == AiProviderKind.Ollama) value.BaseUrl = (string)element.Attribute("BaseUrl") ?? value.BaseUrl;
            }
        }
    }

    public interface IAiChatProvider
    {
        Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken);
        Task TestConnectionAsync(CancellationToken cancellationToken);
        Task GenerateAsync(IReadOnlyList<AiMessage> messages, Action<string> onText, CancellationToken cancellationToken);
    }

    /// <summary>Safe, fixed user-facing errors; never carry raw provider bodies, URLs or secret-bearing inner exceptions.</summary>
    public sealed class AiException : Exception
    {
        public string Code { get; }
        public AiException(string code) : base(Describe(code)) { Code = code; }
        private static string Describe(string code)
        {
            switch (code)
            {
                case "auth": return "API Key ไม่ถูกต้อง หรือไม่มีสิทธิ์ใช้โมเดลนี้";
                case "quota": return "โควตาหรือเครดิตไม่เพียงพอ ตรวจบัญชีของ provider";
                case "rate": return "เกินอัตราการเรียกใช้งาน กรุณารอก่อนลองใหม่";
                case "model": return "ไม่พบโมเดลที่เลือก โหลดรายชื่อโมเดลและตรวจ Model ID";
                case "timeout": return "หมดเวลารอคำตอบ ลองใหม่หรือเลือกโมเดลอื่น";
                case "network": return "เชื่อมต่อไม่ได้ ตรวจเครือข่าย หรือเปิด Ollama และตรวจ Base URL";
                case "key": return "ยังไม่มี API Key เปิด Settings เพื่อตั้งค่า";
                case "url": return "Base URL ต้องเป็น HTTP/HTTPS และไม่มี credentials, query หรือ fragment";
                case "limit": return "ข้อความหรือคำตอบใหญ่เกินขีดจำกัด กรุณาลดขนาดข้อความ";
                case "incomplete": return "การเชื่อมต่อจบก่อนคำตอบสมบูรณ์ ข้อความที่ได้รับยังคงอยู่";
                case "blocked": return "โมเดลไม่ส่งคำตอบข้อความ อาจถูกบล็อกหรือจบด้วยเหตุอื่น";
                default: return "คำขอ AI ไม่สำเร็จ ตรวจการตั้งค่าและลองใหม่";
            }
        }
        internal static AiException FromStatus(int status) => new AiException(
            status == 401 || status == 403 ? "auth" : status == 402 ? "quota" : status == 429 ? "rate" : status == 404 ? "model" : status == 408 || status == 504 ? "timeout" : "provider");
    }
}
