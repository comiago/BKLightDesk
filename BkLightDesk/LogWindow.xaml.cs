using System.Windows;

namespace BkLightDesk;

/// <summary>
/// Interaction logic for LogWindow.xaml.
/// Provides a dedicated terminal view for system events and BLE communication logs.
/// </summary>
public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Appends a new log message to the terminal.
    /// Ensures thread safety by using the UI Dispatcher.
    /// </summary>
    /// <param name="message">The text message to display.</param>
    public void AddMessage(string message)
    {
        // Use the Dispatcher to ensure we are writing to the UI from the correct thread
        Dispatcher.Invoke(() =>
        {
            TxtOutput.AppendText(message + "\n");
            TxtOutput.ScrollToEnd();
        });
    }

    /// <summary>
    /// Populates the terminal with the entire existing log history upon opening.
    /// </summary>
    /// <param name="history">The concatenated string of all past logs.</param>
    public void SetHistory(string history)
    {
        TxtOutput.Text = history;
        TxtOutput.ScrollToEnd();
    }
}