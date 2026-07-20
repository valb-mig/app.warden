using Avalonia.Controls;

namespace Warden.Admin.Views;

public partial class OutputDialog : Window
{
    public OutputDialog()
    {
        InitializeComponent();
    }

    public OutputDialog(string title, string output) : this()
    {
        Title = title;
        TitleText.Text = title;
        OutputText.Text = string.IsNullOrEmpty(output) ? "(sem saída)" : output;
    }
}
