using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks; 
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
    private bool _isPowerOn = true; 
    
    // Timer Orologio
    private DispatcherTimer? _clockTimer;
    private bool _isClockRunning = false;
    
    // Timer Pomodoro
    private DispatcherTimer? _pomodoroTimer;
    private bool _isPomodoroRunning = false;
    private bool _isPomodoroBreak = false;
    private TimeSpan _pomodoroTimeLeft;
    private const int POMODORO_WORK_MINUTES = 25;
    private const int POMODORO_BREAK_MINUTES = 5;

    private bool _isSendingFrame = false;
    private bool _isCleaningUp = false;

    // Colori 2026
    private readonly SolidColorBrush _redBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));    // #EF4444
    private readonly SolidColorBrush _greenBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // #10B981
    private readonly SolidColorBrush _orangeBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // #F59E0B
    private readonly SolidColorBrush _mutedBrush = new SolidColorBrush(Color.FromRgb(82, 82, 91));   // #52525B
    private readonly SolidColorBrush _whiteBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252)); // #F8FAFC

    public MainWindow()
    {
        InitializeComponent();
        SettingsManager.Load();
        
        _bleManager = new BleManager();
        _bleManager.LogMessage += OnLogMessageReceived;

        this.Closing += MainWindow_Closing;
        Application.Current.DispatcherUnhandledException += App_CrashHandler;
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isCleaningUp) return;
        e.Cancel = true;
        _isCleaningUp = true;
        
        UpdateLog("Chiusura in corso... Ripristino Firmware.");

        if (_isUiConnected)
        {
            if (_clockTimer != null) _clockTimer.Stop();
            if (_pomodoroTimer != null) _pomodoroTimer.Stop();
            try {
                await _bleManager.RestoreClockModeAsync();
                await Task.Delay(800); 
                _bleManager.Disconnect();
            } catch { /* Ignora */ }
        }
        Application.Current.Shutdown();
    }

    private void App_CrashHandler(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_bleManager.IsConnected && !_isCleaningUp)
        {
            _isCleaningUp = true;
            try { var t = _bleManager.RestoreClockModeAsync(); t.Wait(1000); } catch {}
        }
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
            if (_isPomodoroRunning) StopPomodoro();
            _bleManager.Disconnect();
            SetDisconnectedState();
        }
    }

    private void SetDisconnectedState()
    {
        _isUiConnected = false;
        BtnScan.Content = "📡  Connetti Dispositivo";
        BtnScan.IsEnabled = true;
        
        TxtStatus.Text = "Disconnesso"; 
        TxtStatus.Foreground = _redBrush; 
        StatusLed.Fill = _redBrush; 
        StatusLed.Effect = new DropShadowEffect { Color = Color.FromRgb(239, 68, 68), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.6 };
        
        if(BtnLoadImage != null) BtnLoadImage.IsEnabled = false;
        if(BtnClock != null) BtnClock.IsEnabled = false;
        if(BtnRestore != null) BtnRestore.IsEnabled = false;
        if(BtnPomodoro != null) BtnPomodoro.IsEnabled = false;
        
        if(BtnPower != null) {
            BtnPower.IsEnabled = false;
            BtnPower.Foreground = _mutedBrush;
        }
    }

    private void OnLogMessageReceived(string message)
    {
        Dispatcher.Invoke(async () => 
        {
            if (message.Contains("Connesso") || message.Contains("PRONTO") || message.Contains("Successo"))
            {
                if (!_isUiConnected)
                {
                    _isUiConnected = true;
                    TxtStatus.Text = "Connesso"; 
                    TxtStatus.Foreground = _whiteBrush;
                    StatusLed.Fill = _greenBrush; 
                    StatusLed.Effect = new DropShadowEffect { Color = Color.FromRgb(16, 185, 129), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.6 };
                    
                    BtnScan.Content = "❌  Disconnetti"; 
                    BtnScan.IsEnabled = true;
                    
                    BtnPower.IsEnabled = true;
                    BtnPower.Foreground = _greenBrush;
                    _isPowerOn = true;

                    int savedBrightness = SettingsManager.Brightness;
                    UpdateLog($"Applico luminosità salvata: {savedBrightness}%");
                    await Task.Delay(300); 
                    await _bleManager.SetBrightnessAsync(savedBrightness);

                    UpdateLog("Avvio automatico Home...");
                    BtnClock_Click(null, null); 
                }
                if (!_isClockRunning && !_isPomodoroRunning) EnableButtons(true);
            }
            else if (message.Contains("Errore") || message.Contains("fallito") || message.Contains("terminata"))
            {
                if (!_isUiConnected) { 
                    BtnScan.IsEnabled = true; 
                    TxtStatus.Text = "Errore Connessione"; 
                    TxtStatus.Foreground = _redBrush; 
                    StatusLed.Fill = _redBrush; 
                }
                UpdateLog(message);
            }
        });
        UpdateLog(message);
    }

    private void EnableButtons(bool enable)
    {
        if(BtnLoadImage != null) BtnLoadImage.IsEnabled = enable;
        if(BtnClock != null) BtnClock.IsEnabled = enable;
        if(BtnRestore != null) BtnRestore.IsEnabled = enable;
        if(BtnPomodoro != null) BtnPomodoro.IsEnabled = enable;
    }

    private async void BtnPower_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUiConnected) return;

        if (_isPowerOn)
        {
            _isPowerOn = false;
            if (_isClockRunning) StopClock();
            if (_isPomodoroRunning) StopPomodoro();
            
            BtnPower.Foreground = _mutedBrush; 

            await _bleManager.SetPowerAsync(false);
            UpdateLog("Matrice spenta (Standby).");
        }
        else
        {
            _isPowerOn = true;
            BtnPower.Foreground = _greenBrush;

            await _bleManager.SetPowerAsync(true);
            UpdateLog("Matrice accesa.");

            await Task.Delay(500); 
            if (!_isClockRunning && !_isPomodoroRunning)
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

    // --- GESTIONE IMMAGINI ---
    private async void BtnLoadImage_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUiConnected) return;
        if (_isClockRunning) StopClock();
        if (_isPomodoroRunning) StopPomodoro();

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

    // --- GESTIONE OROLOGIO APP ---
    private void BtnClock_Click(object? sender, RoutedEventArgs? e)
    {
        if (_isPomodoroRunning) StopPomodoro();

        if (_clockTimer == null) { _clockTimer = new DispatcherTimer(); _clockTimer.Interval = TimeSpan.FromSeconds(1); _clockTimer.Tick += ClockTimer_Tick; }
        if (!_isClockRunning) {
            _isClockRunning = true; 
            BtnClock.Content = "⏹ Ferma Orologio"; 
            BtnClock.Foreground = _redBrush;
            EnableButtons(false); 
            BtnClock.IsEnabled = true; 
            _clockTimer.Start(); 
            UpdateClockDisplay(); 
        } else StopClock();
    }

    private void StopClock()
    {
        _isClockRunning = false; 
        if(_clockTimer != null) _clockTimer.Stop();
        BtnClock.Content = "🕒 Orologio"; 
        BtnClock.Foreground = _whiteBrush; 
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
            FormattedText dayText = new FormattedText(topTextStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.Black, FontStretches.Condensed), 9, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
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

    // --- GESTIONE POMODORO TIMER ---
    private void BtnPomodoro_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUiConnected) return;
        if (_isClockRunning) StopClock();

        if (_pomodoroTimer == null) { 
            _pomodoroTimer = new DispatcherTimer(); 
            _pomodoroTimer.Interval = TimeSpan.FromSeconds(1); 
            _pomodoroTimer.Tick += PomodoroTimer_Tick; 
        }

        if (!_isPomodoroRunning) {
            _isPomodoroRunning = true; 
            _isPomodoroBreak = false;
            _pomodoroTimeLeft = TimeSpan.FromMinutes(POMODORO_WORK_MINUTES);

            BtnPomodoro.Content = "⏹ Ferma Pomodoro"; 
            BtnPomodoro.Foreground = _redBrush;
            EnableButtons(false); 
            BtnPomodoro.IsEnabled = true; 
            
            UpdateLog("🍅 Avviato Pomodoro Timer (Focus: 25m)!");
            _pomodoroTimer.Start(); 
            UpdatePomodoroDisplay(); 
        } else {
            StopPomodoro();
        }
    }

    private void StopPomodoro()
    {
        _isPomodoroRunning = false; 
        if(_pomodoroTimer != null) _pomodoroTimer.Stop();
        BtnPomodoro.Content = "🍅 Pomodoro"; 
        BtnPomodoro.Foreground = _whiteBrush; 
        if (_isUiConnected) EnableButtons(true);
        UpdateLog("⏹ Pomodoro interrotto.");
    }

    private void PomodoroTimer_Tick(object? sender, EventArgs? e) 
    { 
        if (!_isUiConnected) return;

        // Decrescita del timer
        _pomodoroTimeLeft = _pomodoroTimeLeft.Subtract(TimeSpan.FromSeconds(1));

        if (_pomodoroTimeLeft.TotalSeconds <= 0)
        {
            // Switch tra Lavoro e Pausa
            _isPomodoroBreak = !_isPomodoroBreak;
            _pomodoroTimeLeft = _isPomodoroBreak ? TimeSpan.FromMinutes(POMODORO_BREAK_MINUTES) : TimeSpan.FromMinutes(POMODORO_WORK_MINUTES);
            UpdateLog(_isPomodoroBreak ? "☕ Pausa iniziata (5m)" : "🍅 Focus iniziato (25m)!");
        }

        if (!_isSendingFrame) UpdatePomodoroDisplay(); 
    }

    private async void UpdatePomodoroDisplay()
    {
        try { _isSendingFrame = true; byte[] frame = DrawPomodoroFrame(); await _bleManager.SendPngAsync(frame, AppSettings.UseTurboMode); }
        catch (Exception ex) { UpdateLog($"Errore Pomodoro: {ex.Message}"); StopPomodoro(); }
        finally { _isSendingFrame = false; }
    }

    private byte[] DrawPomodoroFrame()
    {
        int w = MATRIX_WIDTH; int h = MATRIX_HEIGHT;
        RenderTargetBitmap bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        DrawingVisual visual = new DrawingVisual();
        using (DrawingContext ctx = visual.RenderOpen())
        {
            // Sfondo Nero
            ctx.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));

            // Logica Colori e Progresso
            TimeSpan totalTime = _isPomodoroBreak ? TimeSpan.FromMinutes(POMODORO_BREAK_MINUTES) : TimeSpan.FromMinutes(POMODORO_WORK_MINUTES);
            double progress = _pomodoroTimeLeft.TotalSeconds / totalTime.TotalSeconds;

            Color mainColor = _isPomodoroBreak ? Color.FromRgb(245, 158, 11) : Color.FromRgb(16, 185, 129); // Arancio Pausa / Verde Focus
            SolidColorBrush mainBrush = new SolidColorBrush(mainColor);

            // Icona in alto (Pomodorino o Tazzina)
            if (!_isPomodoroBreak) {
                ctx.DrawEllipse(Brushes.Red, null, new Point(w/2, 5), 3, 3); // Corpo pomodoro
                ctx.DrawRectangle(Brushes.LimeGreen, null, new Rect((w/2)-1, 1, 2, 2)); // Fogliolina
            } else {
                ctx.DrawRectangle(Brushes.SaddleBrown, null, new Rect((w/2)-3, 3, 6, 5)); // Corpo tazza
                ctx.DrawRectangle(Brushes.SaddleBrown, null, new Rect((w/2)+3, 4, 2, 3)); // Manico
                ctx.DrawRectangle(Brushes.White, null, new Rect((w/2)-1, 1, 1, 2)); // Vapore
                ctx.DrawRectangle(Brushes.White, null, new Rect((w/2)+1, 0, 1, 2));
            }

            // Testo Minuti
            string timeStr = _pomodoroTimeLeft.ToString(@"mm\:ss");
            FormattedText timeText = new FormattedText(timeStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Condensed), 10, mainBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            
            // Centra il testo
            double x = (w - timeText.Width) / 2;
            ctx.DrawText(timeText, new Point(x, 11));

            // Barra di Avanzamento Cyberpunk (Sotto)
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 30)), null, new Rect(2, h - 5, w - 4, 3)); // Sfondo barra
            double barWidth = progress * (w - 4);
            ctx.DrawRectangle(mainBrush, null, new Rect(2, h - 5, barWidth, 3)); // Progresso
        }
        bmp.Render(visual); return ConvertBitmapToPng(bmp);
    }

    // --- GESTIONE OROLOGIO FIRMWARE E UTILITY ---
    private async void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUiConnected) return;
        if (_isClockRunning) StopClock();
        if (_isPomodoroRunning) StopPomodoro();

        await _bleManager.RestoreClockModeAsync();
        UpdateLog("Matrice ripristinata all'orologio originale.");
    }

    private byte[] ConvertBitmapToPng(BitmapSource bitmap)
    {
        using (MemoryStream stream = new MemoryStream()) { PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Interlace = PngInterlaceOption.Off; encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream); return stream.ToArray(); }
    }

    private void BtnLogs_Click(object sender, RoutedEventArgs e) { if (_logWindow == null) { _logWindow = new LogWindow(); _logWindow.Closed += (s, args) => _logWindow = null; _logWindow.SetHistory(_logHistory.ToString()); _logWindow.Show(); } else _logWindow.Activate(); }
    private void UpdateLog(string msg) { string fullMsg = $"[{DateTime.Now:HH:mm:ss}] {msg}"; _logHistory.AppendLine(fullMsg); if (_logWindow != null) _logWindow.AddMessage(fullMsg); }
}