using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlAssist.Core.Ai
{
    public enum FindingSeverity { Info, Warning }

    public sealed class ExplainFinding
    {
        /// <summary>1-based line in the SQL that was sent; 0 when the finding is about the whole script or the line was unusable.</summary>
        public int Line { get; set; }
        public FindingSeverity Severity { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
    }

    public sealed class ExplainResult
    {
        public string Summary { get; set; }
        public IReadOnlyList<ExplainFinding> Findings { get; set; } = new ExplainFinding[0];
        /// <summary>Not null when there is nothing to show: why, in Thai, for the user.</summary>
        public string Problem { get; set; }

        internal static ExplainResult Failed(string problem) => new ExplainResult { Problem = problem };
    }

    /// <summary>Turns the HTTP status and body of an Explain SQL call into what the result window shows. Never throws: anything
    /// unexpected becomes a <see cref="ExplainResult.Problem"/>.</summary>
    public static class ExplainResponse
    {
        private const int MaxErrorText = 300;

        public static ExplainResult Parse(int httpStatus, string body, int sentLineCount)
        {
            if (httpStatus == 0) return ExplainResult.Failed("ติดต่อบริการ AI ไม่ได้ (เครือข่ายหรือหมดเวลา)");
            if (httpStatus == 401 || httpStatus == 403)
                return ExplainResult.Failed("API key ไม่ถูกต้องหรือไม่มีสิทธิ์ (HTTP " + httpStatus.ToString(CultureInfo.InvariantCulture) + ")");
            if (httpStatus == 429) return ExplainResult.Failed("ถูกจำกัดจำนวนการเรียก ลองใหม่อีกครั้งภายหลัง (HTTP 429)");
            if (httpStatus != 200)
            {
                string message = TryRead<ErrorBody>(body)?.Error?.Message;
                string text = "บริการ AI ตอบกลับผิดพลาด (HTTP " + httpStatus.ToString(CultureInfo.InvariantCulture) + ")";
                if (!string.IsNullOrWhiteSpace(message))
                    text += ": " + (message.Length > MaxErrorText ? message.Substring(0, MaxErrorText) + "…" : message);
                return ExplainResult.Failed(text);
            }

            const string Unreadable = "อ่านคำตอบจาก AI ไม่ได้";
            var message200 = TryRead<MessageBody>(body);
            if (message200 == null) return ExplainResult.Failed(Unreadable);
            if (message200.StopReason == "refusal") return ExplainResult.Failed("model ปฏิเสธคำขอนี้");
            if (message200.StopReason == "max_tokens") return ExplainResult.Failed("คำตอบยาวเกินกำหนด ให้เลือก SQL ให้สั้นลง");

            string json = message200.Content?.FirstOrDefault(b => b?.Type == "text")?.Text;
            var answer = TryRead<Answer>(json);
            if (answer?.Summary == null) return ExplainResult.Failed(Unreadable);

            var findings = (answer.Findings ?? new List<AnswerFinding>())
                .Where(f => f != null)
                .Select(f => new ExplainFinding
                {
                    Line = f.Line >= 1 && f.Line <= sentLineCount ? f.Line : 0,
                    Severity = string.Equals(f.Severity, "warning", StringComparison.OrdinalIgnoreCase) ? FindingSeverity.Warning : FindingSeverity.Info,
                    Title = f.Title ?? "",
                    Detail = f.Detail ?? "",
                })
                .ToList();
            return new ExplainResult { Summary = answer.Summary, Findings = findings };
        }

        private static T TryRead<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return new DataContractJsonSerializer(typeof(T)).ReadObject(stream) as T;
            }
            catch (Exception)
            {
                // Not JSON, or not the expected shape (a line that is not a number, an HTML error page...).
                return null;
            }
        }

        [DataContract]
        private sealed class ErrorBody
        {
            [DataMember(Name = "error")] public ErrorDetail Error { get; set; }
        }

        [DataContract]
        private sealed class ErrorDetail
        {
            [DataMember(Name = "message")] public string Message { get; set; }
        }

        [DataContract]
        private sealed class MessageBody
        {
            [DataMember(Name = "stop_reason")] public string StopReason { get; set; }
            [DataMember(Name = "content")] public List<ContentBlock> Content { get; set; }
        }

        [DataContract]
        private sealed class ContentBlock
        {
            [DataMember(Name = "type")] public string Type { get; set; }
            [DataMember(Name = "text")] public string Text { get; set; }
        }

        [DataContract]
        private sealed class Answer
        {
            [DataMember(Name = "summary")] public string Summary { get; set; }
            [DataMember(Name = "findings")] public List<AnswerFinding> Findings { get; set; }
        }

        [DataContract]
        private sealed class AnswerFinding
        {
            [DataMember(Name = "line")] public int Line { get; set; }
            [DataMember(Name = "severity")] public string Severity { get; set; }
            [DataMember(Name = "title")] public string Title { get; set; }
            [DataMember(Name = "detail")] public string Detail { get; set; }
        }
    }
}
