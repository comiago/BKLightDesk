using System.Windows;

namespace BkLightDesk;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        
        // Carica lo stato salvato all'apertura
        ChkTurbo.IsChecked = AppSettings.UseTurboMode;
    }

    private void ChkTurbo_Changed(object sender, RoutedEventArgs e)
    {
        // Aggiorna la variabile globale immediatamente
        AppSettings.UseTurboMode = ChkTurbo.IsChecked == true;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}