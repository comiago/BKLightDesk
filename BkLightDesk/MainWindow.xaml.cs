using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace BkLightDesk;

public partial class MainWindow : Window
{
    private BleManager _bleManager;
    private const int MATRIX_WIDTH = 64;  
    private const int MATRIX_HEIGHT = 64; 

    private LogWindow? _logWindow = null; 
    private StringBuilder _logHistory = new StringBuilder();
    private bool _isUiConnected = false;
    
    // Definiamo il colore Rosso Moderno anche qui per coerenza
    private readonly SolidColorBrush _redBrush = new SolidColorBrush(Color.FromRgb(255, 68, 68)); // #FF4444

    public MainWindow()
    {
        InitializeComponent();
        _bleManager = new BleManager();
        _bleManager.LogMessage += OnLogMessageReceived;
    }

    private void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUiConnected)
        {
            // Connetti
            TxtStatus.Text = "Ricerca in corso...";
            TxtStatus.Foreground = Brushes.Orange;
            StatusLed.Fill = Brushes.Orange;
            BtnScan.IsEnabled = false;
            _bleManager.Connect(); 
        }
        else
        {
            // Disconnetti
            _bleManager.Disconnect();
            SetDisconnectedState();
        }
    }

    private async void BtnSendTest_Click(object sender, RoutedEventArgs e)
    {
        string path = @"C:\Users\tecno\OneDrive\Desktop\download.jpg"; 

        if (File.Exists(path))
        {
            try 
            {
                UpdateLog("Elaborazione immagine...");
                byte[] processedPng = ProcessImageForMatrix(path, MATRIX_WIDTH, MATRIX_HEIGHT);
                
                UpdateLog($"Invio {processedPng.Length} bytes...");
                await _bleManager.InviaImmagineAsync(processedPng);
            }
            catch (Exception ex)
            {
                UpdateLog($"Errore: {ex.Message}");
            }
        }
        else
        {
            UpdateLog($"ERRORE FILE: {path}");
            MessageBox.Show("File non trovato!", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetDisconnectedState()
    {
        _isUiConnected = false;
        BtnScan.Content = "📡  RICERCA DISPOSITIVO";
        BtnScan.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
        BtnScan.IsEnabled = true;
        BtnSend.IsEnabled = false;

        // MODIFICA: Ora torna Rosso invece che Grigio
        TxtStatus.Text = "Disconnesso";
        TxtStatus.Foreground = _redBrush;
        StatusLed.Fill = _redBrush;
        StatusLed.Effect = null;
    }

    private void OnLogMessageReceived(string message)
    {
        Dispatcher.Invoke(() => 
        {
            if (message.Contains("PRONTO") || message.Contains("Connesso"))
            {
                _isUiConnected = true;
                TxtStatus.Text = "Dispositivo Connesso";
                TxtStatus.Foreground = Brushes.White;
                
                StatusLed.Fill = new SolidColorBrush(Color.FromRgb(0, 255, 128)); 
                StatusLed.Effect = new DropShadowEffect { Color = Colors.LimeGreen, BlurRadius = 15, ShadowDepth = 0 };

                BtnSend.IsEnabled = true; 
                BtnScan.Content = "❌  DISCONNETTI";
                BtnScan.Background = new SolidColorBrush(Color.FromRgb(180, 40, 40));
                BtnScan.IsEnabled = true;
            }
            else if (message.Contains("Errore") || message.Contains("fallito"))
            {
                // Anche in caso di errore, usiamo il rosso
                TxtStatus.Text = "Errore / Disconnesso";
                TxtStatus.Foreground = _redBrush;
                StatusLed.Fill = _redBrush;
                BtnScan.IsEnabled = true;
                if (_isUiConnected) SetDisconnectedState();
            }
        });
        UpdateLog(message);
    }

    private void BtnLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_logWindow == null)
        {
            _logWindow = new LogWindow();
            _logWindow.Closed += (s, args) => _logWindow = null;
            _logWindow.SetHistory(_logHistory.ToString());
            _logWindow.Show();
        }
        else _logWindow.Activate();
    }

    private void UpdateLog(string msg)
    {
        string fullMsg = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        _logHistory.AppendLine(fullMsg);
        if (_logWindow != null) _logWindow.AddMessage(fullMsg);
    }

    private byte[] ProcessImageForMatrix(string filePath, int targetWidth, int targetHeight)
    {
        var uri = new Uri(filePath);
        var original = new BitmapImage();
        original.BeginInit();
        original.UriSource = uri;
        original.CacheOption = BitmapCacheOption.OnLoad;
        original.EndInit();

        var scaleX = (double)targetWidth / original.PixelWidth;
        var scaleY = (double)targetHeight / original.PixelHeight;
        var resized = new TransformedBitmap(original, new ScaleTransform(scaleX, scaleY));
        var converted = new FormatConvertedBitmap(resized, PixelFormats.Rgb24, null, 0);
        var encoder = new PngBitmapEncoder();
        encoder.Interlace = PngInterlaceOption.Off;
        encoder.Frames.Add(BitmapFrame.Create(converted));

        using (var ms = new MemoryStream()) { encoder.Save(ms); return ms.ToArray(); }
    }
}