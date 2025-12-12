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
    
    // CORREZIONE FONDAMENTALE: 32x32
    private const int MATRIX_WIDTH = 32;  
    private const int MATRIX_HEIGHT = 32; 

    private LogWindow? _logWindow = null; 
    private StringBuilder _logHistory = new StringBuilder();
    private bool _isUiConnected = false;
    
    private readonly SolidColorBrush _redBrush = new SolidColorBrush(Color.FromRgb(255, 68, 68)); 
    private readonly SolidColorBrush _greenBrush = new SolidColorBrush(Color.FromRgb(0, 255, 128));
    private readonly SolidColorBrush _orangeBrush = Brushes.Orange;
    private readonly SolidColorBrush _darkGrayBrush = new SolidColorBrush(Color.FromRgb(45, 45, 48));

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
            TxtStatus.Text = "Ricerca in corso...";
            TxtStatus.Foreground = _orangeBrush;
            StatusLed.Fill = _orangeBrush;
            BtnScan.IsEnabled = false;
            _bleManager.Connect(); 
        }
        else
        {
            _bleManager.Disconnect();
            SetDisconnectedState();
        }
    }

    private void SetDisconnectedState()
    {
        _isUiConnected = false;
        BtnScan.Content = "📡  RICERCA DISPOSITIVO";
        BtnScan.Background = _darkGrayBrush;
        BtnScan.IsEnabled = true;
        TxtStatus.Text = "Disconnesso";
        TxtStatus.Foreground = _redBrush;
        StatusLed.Fill = _redBrush;
        StatusLed.Effect = null;
        if(BtnRedTest != null) BtnRedTest.IsEnabled = false;
    }

    private void OnLogMessageReceived(string message)
    {
        Dispatcher.Invoke(() => 
        {
            if (message.Contains("Connesso") || message.Contains("PRONTO"))
            {
                _isUiConnected = true;
                TxtStatus.Text = "Dispositivo Connesso";
                TxtStatus.Foreground = Brushes.White;
                StatusLed.Fill = _greenBrush; 
                StatusLed.Effect = new DropShadowEffect { Color = Colors.LimeGreen, BlurRadius = 15, ShadowDepth = 0 };
                BtnScan.Content = "❌  DISCONNETTI";
                BtnScan.Background = new SolidColorBrush(Color.FromRgb(180, 40, 40));
                BtnScan.IsEnabled = true;
                if (BtnRedTest != null) BtnRedTest.IsEnabled = true;
            }
            else if (message.Contains("Errore"))
            {
                UpdateLog(message);
                BtnScan.IsEnabled = true;
            }
        });
        UpdateLog(message);
    }

    private async void BtnRedTest_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUiConnected) return;

        try
        {
            UpdateLog("Generazione Immagine Rossa (32x32)...");

            int width = MATRIX_WIDTH;
            int height = MATRIX_HEIGHT;
            WriteableBitmap bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);

            int stride = width * 3; 
            byte[] pixels = new byte[height * stride];

            // Riempi tutto di Rosso
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i]     = 0;   // Blue
                pixels[i + 1] = 0;   // Green
                pixels[i + 2] = 255; // Red
            }

            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

            byte[] pngBytes;
            using (MemoryStream stream = new MemoryStream())
            {
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Interlace = PngInterlaceOption.Off; 
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
                pngBytes = stream.ToArray();
            }

            UpdateLog($"PNG Generato ({pngBytes.Length} bytes). Invio...");
            await _bleManager.SendPngAsync(pngBytes);
        }
        catch (Exception ex)
        {
            UpdateLog($"Errore: {ex.Message}");
        }
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
}