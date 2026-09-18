using Avalonia.Controls;

namespace EndfieldCharge.Views;

/// <summary>消息框按钮组合。</summary>
public enum MessageBoxButton
{
    Ok,
    OkCancel,
}

/// <summary>消息框返回值。</summary>
public enum MessageBoxResult
{
    Ok,
    Cancel,
}

/// <summary>
/// 极简消息框（只有「检查更新」在用）。
///
/// 原来这个类是塞在 SettingsWindow.axaml.cs 末尾的，窗口内容也是用代码一行行搭的。
/// 现在拆成独立视图，配色走 Styles/HudTheme.axaml。
/// </summary>
public partial class MessageBoxWindow : Window
{
    private MessageBoxResult _result = MessageBoxResult.Ok;

    public MessageBoxWindow()
    {
        InitializeComponent();
    }

    private MessageBoxWindow(string title, string message, MessageBoxButton button) : this()
    {
        Title = title;
        MessageText.Text = message;

        OkButton.Content = Localization.Current.BtnDownload;
        OkButton.Click += (_, _) =>
        {
            _result = MessageBoxResult.Ok;
            Close();
        };

        if (button == MessageBoxButton.OkCancel)
        {
            CancelButton.Content = Localization.Current.BtnCancel;
            CancelButton.IsVisible = true;
            CancelButton.Click += (_, _) =>
            {
                _result = MessageBoxResult.Cancel;
                Close();
            };
        }
    }

    /// <summary>以 owner 为中心弹一个消息框，返回用户点的是哪个按钮。</summary>
    public static async Task<MessageBoxResult> ShowAsync(
        Window owner,
        string message,
        string title,
        MessageBoxButton button = MessageBoxButton.Ok)
    {
        var dialog = new MessageBoxWindow(title, message, button);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }
}
