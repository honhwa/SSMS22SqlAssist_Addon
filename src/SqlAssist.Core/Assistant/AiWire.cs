using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SqlAssist.Core.Assistant
{
    [DataContract]
    internal sealed class WireMessage
    {
        [DataMember(Name = "role")] public string Role;
        [DataMember(Name = "content", EmitDefaultValue = false)] public string Content;
    }
    [DataContract]
    internal sealed class WireChatRequest
    {
        [DataMember(Name = "model")] public string Model;
        [DataMember(Name = "messages")] public WireMessage[] Messages;
        [DataMember(Name = "stream")] public bool Stream;
        [DataMember(Name = "max_tokens", EmitDefaultValue = false)] public int MaxTokens;
        [DataMember(Name = "options", EmitDefaultValue = false)] public OllamaOptions Options;
    }
    [DataContract]
    internal sealed class OllamaOptions { [DataMember(Name = "num_predict")] public int NumPredict; }

    internal static class AiWire
    {
        public static string Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        // Framework JSON-to-XML reader avoids an additional parser assembly in the SSMS host.
        public static XElement Parse(string json)
        {
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json), new XmlDictionaryReaderQuotas
            { MaxDepth = 64, MaxStringContentLength = 8 * 1024 * 1024, MaxArrayLength = 100000 }))
                return XElement.Load(reader);
        }
        public static string Value(XElement node, string name) => (string)node?.Element(name);
        public static void CheckError(XElement root)
        {
            var error = root.Element("error");
            if (error == null || (string)error.Attribute("type") == "null") return;
            string status = Value(error, "status");
            if (status == "RESOURCE_EXHAUSTED") throw new AiException("quota");
            if (int.TryParse(Value(error, "code"), out int code)) throw AiException.FromStatus(code);
            throw new AiException("provider");
        }
    }
}
