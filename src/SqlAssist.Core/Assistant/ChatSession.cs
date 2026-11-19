using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlAssist.Core.Assistant
{
    public sealed class ChatTurn
    {
        public long Id { get; internal set; }
        public IReadOnlyList<AiMessage> Context { get; internal set; }
        public int OmittedTurns { get; internal set; }
    }

    /// <summary>UI-thread-owned conversation. Identity invalidation also rejects already-queued progress after Stop/New Chat.</summary>
    public sealed class ChatSession
    {
        public const int DefaultContextLimit = 32000;
        private readonly int _limit;
        private readonly List<AiMessage> _messages = new List<AiMessage>();
        private long _identity;
        public IReadOnlyList<AiMessage> Messages => _messages.AsReadOnly();
        public bool IsBusy { get; private set; }
        public ChatSession(int contextLimit = DefaultContextLimit) { _limit = contextLimit; }

        public int PreviewOmittedTurns(string text) { SelectContext(text, out int omitted); return omitted; }
        public ChatTurn Begin(string text)
        {
            if (IsBusy) throw new InvalidOperationException("มีคำขอที่กำลังทำงานอยู่");
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("กรุณาพิมพ์ข้อความก่อนส่ง");
            var context = SelectContext(text, out int omitted);
            _messages.Add(new AiMessage("user", text));
            _messages.Add(new AiMessage("assistant", "") { State = AiMessageState.Responding });
            IsBusy = true;
            return new ChatTurn { Id = ++_identity, Context = context.AsReadOnly(), OmittedTurns = omitted };
        }
        private List<AiMessage> SelectContext(string text, out int omitted)
        {
            text = text ?? "";
            if (text.Length > _limit) throw new ArgumentException("ข้อความและ SQL แนบเกิน " + _limit + " ตัวอักษร กรุณาลดขนาดก่อนส่ง");
            var turns = new List<AiMessage[]>();
            for (int i = 0; i + 1 < _messages.Count; i += 2)
            {
                var reply = _messages[i + 1];
                if (reply.State != AiMessageState.Responding && reply.Text.Length > 0)
                {
                    var contextReply = reply.Copy();
                    if (reply.State != AiMessageState.Complete) contextReply.Text += "\n[คำตอบก่อนหน้านี้หยุดกลางทางหรือไม่สมบูรณ์]";
                    turns.Add(new[] { _messages[i].Copy(), contextReply });
                }
            }
            int size = text.Length + turns.Sum(t => t.Sum(m => m.Text.Length));
            omitted = 0;
            while (size > _limit && omitted < turns.Count)
                size -= turns[omitted++].Sum(m => m.Text.Length);
            var result = turns.Skip(omitted).SelectMany(t => t).ToList();
            result.Add(new AiMessage("user", text));
            return result;
        }
        public bool TryAppend(long id, string text)
        {
            if (!IsBusy || id != _identity) return false;
            _messages[_messages.Count - 1].Text += text;
            return true;
        }
        public bool Complete(long id) => End(id, AiMessageState.Complete);
        public bool Fail(long id) => End(id, AiMessageState.Failed);
        private bool End(long id, AiMessageState state)
        {
            if (!IsBusy || id != _identity) return false;
            _messages[_messages.Count - 1].State = state;
            IsBusy = false;
            return true;
        }
        public void Stop()
        {
            if (IsBusy) End(_identity, AiMessageState.Stopped);
            ++_identity;
        }
        public void NewChat() { Stop(); _messages.Clear(); }
    }
}
