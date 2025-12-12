using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32; // Fondamentale per aprire i file

namespace BkLightDesk;

public partial class MainWindow : Window
{
    private BleManager _bleManager;
    
    // Configurazione fissa per la tua matrice
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

    // --- GESTIONE CONNESSIONE ---

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
        if(BtnLoadImage != null) BtnLoadImage.IsEnabled = false;
    }

    private void OnLogMessageReceived(string message)
    {
        Dispatcher.Invoke(() => 
        {
            if (message.Contains("Connesso") || message.Contains("PRONTO") || message.Contains("Successo"))
            {
                if (!_isUiConnected)
                {
                    _isUiConnected = true;
                    TxtStatus.Text = "Dispositivo Connesso";
                    TxtStatus.Foreground = Brushes.White;
                    StatusLed.Fill = _greenBrush; 
                    StatusLed.Effect = new DropShadowEffect { Color = Colors.LimeGreen, BlurRadius = 15, ShadowDepth = 0 };
                    
                    BtnScan.Content = "❌  DISCONNETTI";
                    BtnScan.Background = new SolidColorBrush(Color.FromRgb(180, 40, 40));
                    BtnScan.IsEnabled = true;
                }

                if (BtnRedTest != null) BtnRedTest.IsEnabled = true;
                if (BtnLoadImage != null) BtnLoadImage.IsEnabled = true;
            }
            else if (message.Contains("Errore") || message.Contains("fallito"))
            {
                UpdateLog(message);
                BtnScan.IsEnabled = true;
            }
        });
        UpdateLog(message);
    }

    // --- LOGICA TEST ROSSO ---

    private async void BtnRedTest_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUiConnected) return;

        try
        {
            UpdateLog("Generazione Pattern Rosso (32x32)...");

            int width = MATRIX_WIDTH;
            int height = MATRIX_HEIGHT;
            WriteableBitmap bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);

            int stride = width * 3; 
            byte[] pixels = new byte[height * stride];

            // Riempi di Rosso
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i]     = 0;   // Blue
                pixels[i + 1] = 0;   // Green
                pixels[i + 2] = 255; // Red
            }

            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

            // Converti in PNG
            byte[] pngBytes = ConvertBitmapToPng(bitmap);

            bool turbo = ChkTurbo.IsChecked == true;
            UpdateLog($"Invio Test...");
            await _bleManager.SendPngAsync(pngBytes, turbo);
        }
        catch (Exception ex)
        {
            UpdateLog($"Errore Test: {ex.Message}");
        }
    }

    // --- LOGICA CARICAMENTO IMMAGINE (32x32) ---

    private async void BtnLoadImage_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUiConnected) return;

        OpenFileDialog openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "Immagini|*.jpg;*.jpeg;*.png;*.bmp;*.gif";
        openFileDialog.Title = "Seleziona immagine";

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                string filename = openFileDialog.FileName;
                UpdateLog($"Elaborazione: {Path.GetFileName(filename)}...");

                // 1. Carica l'originale
                BitmapImage original = new BitmapImage();
                original.BeginInit();
                original.UriSource = new Uri(filename);
                original.CacheOption = BitmapCacheOption.OnLoad;
                original.EndInit();

                // 2. Ridimensiona a 32x32
                RenderTargetBitmap resizedBitmap = new RenderTargetBitmap(
                    MATRIX_WIDTH, MATRIX_HEIGHT, 
                    96, 96, 
                    PixelFormats.Pbgra32);

                DrawingVisual drawingVisual = new DrawingVisual();
                using (DrawingContext context = drawingVisual.RenderOpen())
                {
                    // Disegna l'immagine scalata per riempire il quadrato 32x32
                    context.DrawImage(original, new Rect(0, 0, MATRIX_WIDTH, MATRIX_HEIGHT));
                }
                resizedBitmap.Render(drawingVisual);

                // 3. Converti in PNG
                byte[] pngBytes = ConvertBitmapToPng(resizedBitmap);

                bool turbo = ChkTurbo.IsChecked == true;
                UpdateLog($"Invio immagine ({pngBytes.Length} bytes)...");
                
                await _bleManager.SendPngAsync(pngBytes, turbo);
            }
            catch (Exception ex)
            {
                UpdateLog($"Errore Immagine: {ex.Message}");
                MessageBox.Show(ex.Message);
            }
        }
    }

    // Helper per creare il PNG
    private byte[] ConvertBitmapToPng(BitmapSource bitmap)
    {
        using (MemoryStream stream = new MemoryStream())
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Interlace = PngInterlaceOption.Off; // Importante
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            return stream.ToArray();
        }
    }

    // --- LOGGING ---

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