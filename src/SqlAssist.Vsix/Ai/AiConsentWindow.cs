using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SqlAssist.Core.Ai;

namespace SqlAssist.Ai
{
    /// <summary>Shows what an Explain SQL request sends and where to: first the SQL and schema as plain text, then the JSON body exactly
    /// as it goes out (the readable text is the user message inside that same body). Asked before the first send; also opened read-only
    /// from the result window's "ดูสิ่งที่ส่ง". A plain Window so it can be rendered outside SSMS to check its layout.</summary>
    internal sealed class AiConsentWindow : Window
    {
        private readonly CheckBox _dontAskAgain = new CheckBox { Content = "ไม่ต้องถามอีก (ดูสิ่งที่ส่งได้ทุกครั้งจากหน้าต่างผลลัพธ์)", VerticalAlignment = VerticalAlignment.Center };

        public AiConsentWindow(ExplainRequest request, string endpoint, bool ask)
        {
            Title = ask ? "SqlAssist: ยืนยันก่อนส่ง SQL ไปยังบริการ AI" : "SqlAssist: ข้อมูลที่ส่งไปยังบริการ AI";
            Width = 820;
            Height = 600;
            MinWidth = 520;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            var root = new DockPanel { Margin = new Thickness(14) };

            var header = new StackPanel();
            header.Children.Add(new TextBlock
            {
                Text = (ask ? "ข้อมูลนี้จะถูกส่งไปที่ " : "ข้อมูลนี้ถูกส่งไปที่ ") + endpoint,
                FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            });
            header.Children.Add(new TextBlock
            {
                Text = "ไม่มีข้อมูลในตาราง ผลลัพธ์ query ชื่อ server ชื่อ database หรือ login (API key อยู่ใน header ไม่อยู่ในข้อความนี้) " +
                       "comment ใน SQL ถูกส่งไปตามที่เขียน ถ้ามีข้อมูลที่ไม่ควรออกนอกองค์กร ให้ยกเลิกแล้วลบออกก่อน",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 4, 0, 8),
            });
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 10, 0, 0) };
            if (ask)
            {
                var cancel = new Button { Content = "ยกเลิก", Width = 90, Height = 28, IsCancel = true, IsDefault = true };
                cancel.Click += (s, e) => DialogResult = false;
                DockPanel.SetDock(cancel, Dock.Right);
                buttons.Children.Add(cancel);
                var send = new Button { Content = "ส่ง", Width = 90, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
                send.Click += (s, e) => DialogResult = true;
                DockPanel.SetDock(send, Dock.Right);
                buttons.Children.Add(send);
                DockPanel.SetDock(_dontAskAgain, Dock.Left);
                buttons.Children.Add(_dontAskAgain);
            }
            else
            {
                var close = new Button { Content = "ปิด", Width = 90, Height = 28, IsCancel = true, IsDefault = true };
                close.Click += (s, e) => Close();
                DockPanel.SetDock(close, Dock.Right);
                buttons.Children.Add(close);
            }
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = "SQL และ schema ที่ส่ง", Content = Text(request.UserContent, TextWrapping.NoWrap) });
            tabs.Items.Add(new TabItem { Header = "ข้อความที่ส่งจริง (JSON)", Content = Text(request.Body, TextWrapping.Wrap) });
            root.Children.Add(tabs);
            Content = root;
        }

        public bool DontAskAgain => _dontAskAgain.IsChecked == true;

        private static TextBox Text(string text, TextWrapping wrapping) => new TextBox
        {
            Text = text, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = wrapping,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        /// <summary>True when the user pressed "ส่ง". Esc, Enter and closing the window all mean no.</summary>
        public static bool Ask(ExplainRequest request, string endpoint, out bool dontAskAgain)
        {
            var window = new AiConsentWindow(request, endpoint, ask: true);
            bool send = DialogHost.ShowDialog(window) == true;
            dontAskAgain = send && window.DontAskAgain;
            return send;
        }

        public static void ShowSent(ExplainRequest request, string endpoint) => DialogHost.ShowDialog(new AiConsentWindow(request, endpoint, ask: false));
    }
}
