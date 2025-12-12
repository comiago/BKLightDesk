using System.Windows;
using System.Windows.Controls;

namespace BkLightDesk;

public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
    }

    // Metodo per aggiungere testo dal MainWindow
    public void AddMessage(string message)
    {
        // Usa Dispatcher per essere sicuro di scrivere dal thread della UI
        Dispatcher.Invoke(() =>
        {
            TxtOutput.AppendText(message + "\n");
            TxtOutput.ScrollToEnd();
        });
    }

    // Metodo per caricare tutto lo storico quando apri la finestra
    public void SetHistory(string history)
    {
        TxtOutput.Text = history;
        TxtOutput.ScrollToEnd();
    }
}