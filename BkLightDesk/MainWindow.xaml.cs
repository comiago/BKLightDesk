using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks; // Necessario per Task.Delay
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BkLightDesk;

public partial class MainWindow : Window
{
    private BleManager _bleManager;
    private const int MATRIX_WIDTH = 32;  
    private const int MATRIX_HEIGHT = 32; 

    private LogWindow? _logWindow = null; 
    private StringBuilder _logHistory = new StringBuilder();
    private bool _isUiConnected = false;
    private bool _isPowerOn = true; // Stato presunto accensione
    
    private DispatcherTimer? _clockTimer;
    private bool _isClockRunning = false;
    private bool _isSendingFrame = false;

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

    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUiConnected)
        {
            string btStatus = await _bleManager.CheckBluetoothAvailability();
            if (btStatus != "OK") { MessageBox.Show(btStatus); UpdateLog(btStatus); return; }

            TxtStatus.Text = "Ricerca in corso...";
            TxtStatus.Foreground = _orangeBrush;
            StatusLed.Fill = _orangeBrush;
            BtnScan.IsEnabled = false;
            _bleManager.Connect(); 
        }
        else
        {
            if (_isClockRunning) StopClock();
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
        
        TxtStatus.Text = "Disconnesso"; TxtStatus.Foreground = _redBrush; StatusLed.Fill = _redBrush; StatusLed.Effect = null;
        
        if(BtnRedTest != null) BtnRedTest.IsEnabled = false;
        if(BtnLoadImage != null) BtnLoadImage.IsEnabled = false;
        if(BtnClock != null) BtnClock.IsEnabled = false;
        
        // Reset tasto Power
        if(BtnPower != null) {
            BtnPower.IsEnabled = false;
            BtnPower.Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85));
            BtnPower.Background = new SolidColorBrush(Color.FromRgb(51, 51, 51));
        }
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
                    TxtStatus.Text = "Dispositivo Connesso"; TxtStatus.Foreground = Brushes.White;
                    StatusLed.Fill = _greenBrush; StatusLed.Effect = new DropShadowEffect { Color = Colors.LimeGreen, BlurRadius = 15, ShadowDepth = 0 };
                    
                    BtnScan.Content = "❌  DISCONNETTI"; BtnScan.Background = new SolidColorBrush(Color.FromRgb(180, 40, 40)); BtnScan.IsEnabled = true;
                    
                    // Attiva Power Button
                    BtnPower.IsEnabled = true;
                    BtnPower.Foreground = Brushes.LimeGreen;
                    _isPowerOn = true;

                    UpdateLog("Avvio automatico Home...");
                    // Avvia orologio
                    BtnClock_Click(null, null); 
                }
                if (!_isClockRunning) EnableButtons(true);
            }
            else if (message.Contains("Errore") || message.Contains("fallito") || message.Contains("terminata"))
            {
                if (!_isUiConnected) { BtnScan.IsEnabled = true; TxtStatus.Text = "Errore / Non Trovato"; TxtStatus.Foreground = _redBrush; StatusLed.Fill = _redBrush; }
                UpdateLog(message);
            }
        });
        UpdateLog(message);
    }

    private void EnableButtons(bool enable)
    {
        if(BtnRedTest != null) BtnRedTest.IsEnabled = enable;
        if(BtnLoadImage != null) BtnLoadImage.IsEnabled = enable;
        if(BtnClock != null) BtnClock.IsEnabled = enable;
    }

    // --- NUOVO: PULSANTE POWER ---
    private async void BtnPower_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUiConnected) return;

        if (_isPowerOn)
        {
            // SPEGNI
            _isPowerOn = false;
            if (_isClockRunning) StopClock();
            
            BtnPower.Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)); 
            BtnPower.Background = new SolidColorBrush(Color.FromRgb(40, 20, 20)); 

            await _bleManager.SetPowerAsync(false);
            UpdateLog("Matrice spenta (Standby).");
        }
        else
        {
            // ACCENDI
            _isPowerOn = true;
            BtnPower.Foreground = Brushes.LimeGreen;
            BtnPower.Background = new SolidColorBrush(Color.FromRgb(51, 51, 51));

            await _bleManager.SetPowerAsync(true);
            UpdateLog("Matrice accesa.");

            // Ritorna alla Home (Orologio) dopo breve pausa
            await Task.Delay(500); 
            if (!_isClockRunning)
            {
                UpdateLog("Ritorno alla Home...");
                BtnClock_Click(null, null);
            }
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsWindow settings = new SettingsWindow(_bleManager);
        settings.Owner = this; 
        settings.ShowDialog(); 
    }

    private async void BtnRedTest_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUiConnected) return;
        try {
            UpdateLog("Generazione Rosso...");
            WriteableBitmap bitmap = new WriteableBitmap(MATRIX_WIDTH, MATRIX_HEIGHT, 96, 96, PixelFormats.Bgr24, null);
            byte[] pixels = new byte[MATRIX_HEIGHT * MATRIX_WIDTH * 3];
            for (int i = 0; i < pixels.Length; i += 3) pixels[i + 2] = 255; 
            bitmap.WritePixels(new Int32Rect(0, 0, MATRIX_WIDTH, MATRIX_HEIGHT), pixels, MATRIX_WIDTH * 3, 0);
            await _bleManager.SendPngAsync(ConvertBitmapToPng(bitmap), AppSettings.UseTurboMode);
        } catch (Exception ex) { UpdateLog($"Errore Test: {ex.Message}"); }
    }

    private async void BtnLoadImage_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUiConnected) return;
        OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "Immagini|*.jpg;*.jpeg;*.png;*.bmp;*.gif" };
        if (openFileDialog.ShowDialog() == true)
        {
            try {
                UpdateLog($"Elaborazione: {Path.GetFileName(openFileDialog.FileName)}...");
                BitmapImage original = new BitmapImage(); original.BeginInit(); original.UriSource = new Uri(openFileDialog.FileName); original.CacheOption = BitmapCacheOption.OnLoad; original.EndInit();
                RenderTargetBitmap resizedBitmap = new RenderTargetBitmap(MATRIX_WIDTH, MATRIX_HEIGHT, 96, 96, PixelFormats.Pbgra32);
                DrawingVisual drawingVisual = new DrawingVisual();
                using (DrawingContext context = drawingVisual.RenderOpen()) { context.DrawImage(original, new Rect(0, 0, MATRIX_WIDTH, MATRIX_HEIGHT)); }
                resizedBitmap.Render(drawingVisual);
                await _bleManager.SendPngAsync(ConvertBitmapToPng(resizedBitmap), AppSettings.UseTurboMode);
            } catch (Exception ex) { UpdateLog($"Errore Immagine: {ex.Message}"); }
        }
    }

    private void BtnClock_Click(object? sender, RoutedEventArgs? e)
    {
        if (_clockTimer == null) { _clockTimer = new DispatcherTimer(); _clockTimer.Interval = TimeSpan.FromSeconds(1); _clockTimer.Tick += ClockTimer_Tick; }
        if (!_isClockRunning) {
            _isClockRunning = true; BtnClock.Content = "⏹ FERMA OROLOGIO"; BtnClock.Background = new SolidColorBrush(Color.FromRgb(200, 50, 50)); 
            EnableButtons(false); BtnClock.IsEnabled = true; _clockTimer.Start(); UpdateClockDisplay(); 
        } else StopClock();
    }

    private void StopClock()
    {
        _isClockRunning = false; if(_clockTimer != null) _clockTimer.Stop();
        BtnClock.Content = "🕒 OROLOGIO STYLE"; BtnClock.Background = new SolidColorBrush(Color.FromRgb(102, 0, 204));
        if (_isUiConnected) EnableButtons(true);
    }

    private void ClockTimer_Tick(object? sender, EventArgs? e) { if (!_isUiConnected || _isSendingFrame) return; UpdateClockDisplay(); }

    private async void UpdateClockDisplay()
    {
        try { _isSendingFrame = true; byte[] frame = DrawStylishClock(); await _bleManager.SendPngAsync(frame, AppSettings.UseTurboMode); }
        catch (Exception ex) { UpdateLog($"Errore Clock: {ex.Message}"); StopClock(); }
        finally { _isSendingFrame = false; }
    }

    private byte[] DrawStylishClock()
    {
        int w = MATRIX_WIDTH; int h = MATRIX_HEIGHT;
        RenderTargetBitmap bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        DrawingVisual visual = new DrawingVisual();
        using (DrawingContext ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
            DateTime now = DateTime.Now; string timeStr = now.ToString("HH:mm");
            Color dayBgColor = Colors.DeepSkyBlue; if (now.DayOfWeek == DayOfWeek.Sunday) dayBgColor = Colors.Red; if (now.DayOfWeek == DayOfWeek.Saturday) dayBgColor = Colors.Orange;
            ctx.DrawRectangle(new SolidColorBrush(dayBgColor), null, new Rect(0, 0, w, 9));
            string topTextStr; if (now.Second % 6 < 3) topTextStr = now.ToString("ddd", new CultureInfo("it-IT")).ToUpper().Replace(".", ""); else topTextStr = now.ToString("dd/MM");
            FormattedText dayText = new FormattedText(topTextStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Condensed), 9, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            ctx.DrawText(dayText, new Point((w - dayText.Width) / 2, -2));
            LinearGradientBrush timeBrush = new LinearGradientBrush(); timeBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 255, 255), 0.0)); timeBrush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 0, 255), 1.0)); timeBrush.StartPoint = new Point(0, 0); timeBrush.EndPoint = new Point(1, 1);
            FormattedText timeText = new FormattedText(timeStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 11, timeBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            ctx.DrawText(timeText, new Point((w - timeText.Width) / 2, 10));
            bool isDay = now.Hour >= 6 && now.Hour < 20;
            if (isDay) { ctx.DrawEllipse(Brushes.Orange, null, new Point(w/2, 27), 4, 4); ctx.DrawEllipse(Brushes.Yellow, null, new Point(w/2, 27), 2, 2); }
            else { ctx.DrawEllipse(Brushes.LightGray, null, new Point(w/2, 27), 3, 3); ctx.DrawEllipse(Brushes.Black, null, new Point((w/2) + 1, 26), 3, 3); }
            ctx.DrawRectangle(Brushes.DimGray, null, new Rect(2, 27, 4, 1)); ctx.DrawRectangle(Brushes.DimGray, null, new Rect(26, 27, 4, 1));
        }
        bmp.Render(visual); return ConvertBitmapToPng(bmp);
    }

    private byte[] ConvertBitmapToPng(BitmapSource bitmap)
    {
        using (MemoryStream stream = new MemoryStream()) { PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Interlace = PngInterlaceOption.Off; encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream); return stream.ToArray(); }
    }

    private void BtnLogs_Click(object sender, RoutedEventArgs e) { if (_logWindow == null) { _logWindow = new LogWindow(); _logWindow.Closed += (s, args) => _logWindow = null; _logWindow.SetHistory(_logHistory.ToString()); _logWindow.Show(); } else _logWindow.Activate(); }
    private void UpdateLog(string msg) { string fullMsg = $"[{DateTime.Now:HH:mm:ss}] {msg}"; _logHistory.AppendLine(fullMsg); if (_logWindow != null) _logWindow.AddMessage(fullMsg); }
}