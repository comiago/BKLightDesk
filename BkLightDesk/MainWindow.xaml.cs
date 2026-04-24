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

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // --- Hardware Constants ---
    private const int MatrixWidth = 32;  
    private const int MatrixHeight = 32; 

    // --- Core Managers & Windows ---
    private readonly BleManager _bleManager;
    private LogWindow? _logWindow = null; 
    private readonly StringBuilder _logHistory = new();
    private readonly MediaPlayer _soundPlayer = new();

    // --- State Management ---
    private bool _isConnected = false;
    private bool _isPowerOn = true; 
    private bool _isSendingFrame = false;
    private bool _isShuttingDown = false;
    private bool _isAnimating = false; 

    // --- Clock Mode State ---
    private DispatcherTimer? _clockTimer;
    private bool _isClockActive = false;
    
    // --- Pomodoro Timer State ---
    private DispatcherTimer? _pomoTimer;
    private bool _isPomoActive = false;
    private bool _isPomoOnScreen = false; 
    private bool _isPomoBreak = false;
    private TimeSpan _pomoTimeLeft;
    private int _pomoWorkMin = 25;
    private int _pomoBreakMin = 5;
    private int _pomoCurrentCycle = 1;
    private int _pomoTotalCycles = 4;

    // --- UI Brushes ---
    private readonly SolidColorBrush _redBrush = new(Color.FromRgb(239, 68, 68));
    private readonly SolidColorBrush _greenBrush = new(Color.FromRgb(16, 185, 129));
    private readonly SolidColorBrush _orangeBrush = new(Color.FromRgb(245, 158, 11));
    private readonly SolidColorBrush _mutedBrush = new(Color.FromRgb(82, 82, 91));
    private readonly SolidColorBrush _whiteBrush = new(Color.FromRgb(248, 250, 252));

    public MainWindow()
    {
        InitializeComponent();
        
        // Load user preferences
        SettingsManager.Load();
        
        // Initialize BLE Manager
        _bleManager = new BleManager();
        _bleManager.LogMessage += OnLogReceived;
        
        // Setup Lifecycles
        this.Closing += OnWindowClosing;
        Application.Current.DispatcherUnhandledException += OnGlobalException;
    }

    #region Application Lifecycle

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isShuttingDown) return;
        e.Cancel = true; // Delay closing to cleanup BLE
        _isShuttingDown = true;

        if (_isConnected)
        {
            StopAllActiveModes();
            try {
                // Restore the native matrix clock before exiting
                await _bleManager.RestoreClockModeAsync();
                await Task.Delay(500); 
                _bleManager.Disconnect();
            } catch { /* Silent fail on exit */ }
        }
        Application.Current.Shutdown();
    }

    private void OnGlobalException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Emergency cleanup if the app crashes
        if (_bleManager.IsConnected && !_isShuttingDown)
        {
            _isShuttingDown = true;
            try { _bleManager.RestoreClockModeAsync().Wait(500); } catch { }
        }
    }

    #endregion

    #region Connection Management

    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected)
        {
            string availability = await _bleManager.CheckBluetoothAvailability();
            if (availability != "OK") { MessageBox.Show(availability, "Bluetooth Error"); return; }

            TxtStatus.Text = "Scanning..."; 
            TxtStatus.Foreground = _orangeBrush; 
            StatusLed.Fill = _orangeBrush;
            BtnScan.IsEnabled = false;

            _bleManager.Connect(); 
        }
        else
        {
            StopAllActiveModes();
            _bleManager.Disconnect();
            SetDisconnectedUI();
        }
    }

    private void SetDisconnectedUI()
    {
        _isConnected = false;
        BtnScan.Content = "📡  Connect Device"; 
        BtnScan.IsEnabled = true;
        TxtStatus.Text = "Disconnected"; 
        TxtStatus.Foreground = _redBrush; 
        StatusLed.Fill = _redBrush; 
        StatusLed.Effect = null;
        
        ToggleFeatureButtons(false);
        BtnPower.IsEnabled = false; 
        BtnPower.Foreground = _mutedBrush;
    }

    private void OnLogReceived(string message)
    {
        Dispatcher.Invoke(async () => 
        {
            if (message.Contains("Connected") || message.Contains("READY") || message.Contains("Success"))
            {
                if (!_isConnected)
                {
                    _isConnected = true;
                    TxtStatus.Text = "Connected"; 
                    TxtStatus.Foreground = _whiteBrush; 
                    StatusLed.Fill = _greenBrush; 
                    StatusLed.Effect = new DropShadowEffect { Color = Color.FromRgb(16, 185, 129), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.6 };
                    
                    BtnScan.Content = "❌  Disconnect"; 
                    BtnScan.IsEnabled = true;
                    BtnPower.IsEnabled = true; 
                    BtnPower.Foreground = _greenBrush; 
                    _isPowerOn = true;

                    await Task.Delay(300); 
                    await _bleManager.SetBrightnessAsync(SettingsManager.Brightness);
                    
                    // Auto-start clock on connection
                    BtnClock_Click(null, null); 
                }
                ToggleFeatureButtons(true);
            }
            else if (message.Contains("Error"))
            {
                if (!_isConnected) 
                { 
                    BtnScan.IsEnabled = true; 
                    TxtStatus.Text = "Connect Fail"; 
                    TxtStatus.Foreground = _redBrush; 
                }
            }
            UpdateLog(message);
        });
    }

    private void ToggleFeatureButtons(bool enable)
    {
        BtnLoadImage.IsEnabled = enable;
        BtnClock.IsEnabled = enable;
        BtnRestore.IsEnabled = enable;
        BtnPomodoro.IsEnabled = enable;
    }

    #endregion

    #region Device Controls

    private async void BtnPower_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected) return;

        _isPowerOn = !_isPowerOn;
        if (!_isPowerOn)
        {
            StopAllActiveModes();
            BtnPower.Foreground = _mutedBrush; 
            await _bleManager.SetPowerAsync(false);
        }
        else
        {
            BtnPower.Foreground = _greenBrush;
            await _bleManager.SetPowerAsync(true);
            await Task.Delay(500); 
            StartClock();
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(_bleManager) { Owner = this };
        settings.ShowDialog(); 
    }

    private void StopAllActiveModes()
    {
        StopClock();
        StopPomodoro();
    }

    #endregion

    #region Smart Clock Mode

    private void BtnClock_Click(object? sender, RoutedEventArgs? e)
    {
        _isPomoOnScreen = false; 
        if (!_isClockActive) StartClock();
        else StopClock();
    }

    private void StartClock()
    {
        _isClockActive = true; 
        BtnClock.Content = "⏹ Stop Clock"; 
        BtnClock.Foreground = _redBrush;

        _clockTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick -= OnClockTick;
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start(); 
        
        UpdateClockFrame();
    }

    private void StopClock()
    {
        _isClockActive = false; 
        _clockTimer?.Stop();
        BtnClock.Content = "🕒 Smart Clock"; 
        BtnClock.Foreground = _whiteBrush; 
    }

    private void OnClockTick(object? sender, EventArgs? e) 
    { 
        if (!_isConnected || _isSendingFrame || _isPomoOnScreen) return; 
        UpdateClockFrame(); 
    }

    private async void UpdateClockFrame()
    {
        try 
        { 
            _isSendingFrame = true; 
            byte[] frame = RenderClockUI(); 
            await _bleManager.SendPngAsync(frame, AppSettings.UseTurboMode); 
        }
        catch { StopClock(); }
        finally { _isSendingFrame = false; }
    }

    private byte[] RenderClockUI()
    {
        RenderTargetBitmap bmp = new(MatrixWidth, MatrixHeight, 96, 96, PixelFormats.Pbgra32);
        DrawingVisual visual = new();
        using (DrawingContext ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(Brushes.Black, null, new Rect(0, 0, MatrixWidth, MatrixHeight));
            DateTime now = DateTime.Now;

            // Header Background (Day dependent)
            Color headerColor = Colors.DeepSkyBlue;
            if (now.DayOfWeek == DayOfWeek.Sunday) headerColor = Colors.Red;
            else if (now.DayOfWeek == DayOfWeek.Saturday) headerColor = Colors.Orange;
            
            ctx.DrawRectangle(new SolidColorBrush(headerColor), null, new Rect(0, 0, MatrixWidth, 9));

            // Date / Day Display (Toggles every 3 seconds)
            string topText = (now.Second % 6 < 3) 
                ? now.ToString("ddd", new CultureInfo("en-US")).ToUpper() 
                : now.ToString("dd/MM");

            FormattedText dayText = new(topText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, 
                new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.Black, FontStretches.Condensed), 
                9, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            
            ctx.DrawText(dayText, new Point((MatrixWidth - dayText.Width) / 2, -2));

            // Time Display (Gradient effect)
            LinearGradientBrush timeBrush = new()
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops = { new(Color.FromRgb(0, 255, 255), 0.0), new(Color.FromRgb(255, 0, 255), 1.0) }
            };

            FormattedText timeText = new(now.ToString("HH:mm"), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, 
                new Typeface("Arial"), 11, timeBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            
            ctx.DrawText(timeText, new Point((MatrixWidth - timeText.Width) / 2, 10));

            // Sun/Moon Icon logic
            bool isDaytime = now.Hour >= 6 && now.Hour < 20;
            Point iconCenter = new(MatrixWidth / 2, 27);
            if (isDaytime) 
            { 
                ctx.DrawEllipse(Brushes.Orange, null, iconCenter, 4, 4); 
                ctx.DrawEllipse(Brushes.Yellow, null, iconCenter, 2, 2); 
            }
            else 
            { 
                ctx.DrawEllipse(Brushes.LightGray, null, iconCenter, 3, 3); 
                ctx.DrawEllipse(Brushes.Black, null, new Point(iconCenter.X + 1, iconCenter.Y - 1), 3, 3); 
            }
        }
        bmp.Render(visual); 
        return EncodeToPng(bmp);
    }

    #endregion

    #region Pomodoro Timer Mode

    private void BtnPomodoro_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected) return;

        // If timer is already running but we are looking at the clock, bring timer back to screen
        if (_isPomoActive && !_isPomoOnScreen)
        {
            StopClock();
            _isPomoOnScreen = true;
            UpdatePomoFrame();
            return;
        }

        // If looking at timer, stop it
        if (_isPomoActive && _isPomoOnScreen) { StopPomodoro(); return; }

        // Start new session
        var pomWindow = new PomodoroWindow { Owner = this };
        if (pomWindow.ShowDialog() == true)
        {
            StopClock();
            _pomoWorkMin = pomWindow.WorkMinutes; 
            _pomoBreakMin = pomWindow.BreakMinutes; 
            _pomoTotalCycles = pomWindow.Cycles;
            
            _pomoCurrentCycle = 1; 
            _isPomoBreak = false; 
            _pomoTimeLeft = TimeSpan.FromMinutes(_pomoWorkMin);

            _pomoTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _pomoTimer.Tick -= OnPomoTick;
            _pomoTimer.Tick += OnPomoTick;

            _isPomoActive = true; 
            _isPomoOnScreen = true;
            BtnPomodoro.Content = "⏹ Stop Timer"; 
            BtnPomodoro.Foreground = _redBrush;
            
            _pomoTimer.Start(); 
            UpdatePomoFrame(); 
        }
    }

    private void StopPomodoro()
    {
        _isPomoActive = false; 
        _isPomoOnScreen = false;
        _pomoTimer?.Stop();
        BtnPomodoro.Content = "🍅 Pomodoro Timer"; 
        BtnPomodoro.Foreground = _whiteBrush; 
    }

    private async void OnPomoTick(object? sender, EventArgs? e) 
    { 
        if (!_isConnected || _isAnimating) return;

        _pomoTimeLeft = _pomoTimeLeft.Subtract(TimeSpan.FromSeconds(1));

        if (_pomoTimeLeft.TotalSeconds <= 0)
        {
            _pomoTimer!.Stop();
            
            // Screen hijacking: If user was on Clock mode, force Pomo on screen for the alert
            if (!_isPomoOnScreen) { StopClock(); _isPomoOnScreen = true; }

            if (!_isPomoBreak)
            {
                _isPomoBreak = true; 
                _pomoTimeLeft = TimeSpan.FromMinutes(_pomoBreakMin);
                await HandlePomoTransition(true); // Work Finished
            }
            else
            {
                if (_pomoCurrentCycle >= _pomoTotalCycles) { StopPomodoro(); return; }
                _isPomoBreak = false; 
                _pomoCurrentCycle++; 
                _pomoTimeLeft = TimeSpan.FromMinutes(_pomoWorkMin);
                await HandlePomoTransition(false); // Break Finished
            }
            _pomoTimer.Start();
        }

        if (!_isSendingFrame && !_isAnimating && _isPomoOnScreen) UpdatePomoFrame(); 
    }

    private async Task HandlePomoTransition(bool toBreak)
    {
        _isAnimating = true; 
        
        // Sound Notification
        string soundFile = toBreak ? "work_end.wav" : "work_start.wav";
        string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", soundFile);
        if (File.Exists(soundPath)) { _soundPlayer.Open(new Uri(soundPath)); _soundPlayer.Play(); }

        // Visual Flash Animation
        Color flashColor = toBreak ? Color.FromRgb(255, 60, 0) : Color.FromRgb(0, 255, 100);
        byte[] colorFrame = GenerateSolidColorFrame(flashColor); 
        byte[] blackFrame = GenerateSolidColorFrame(Colors.Black);

        for (int i = 0; i < 3; i++) 
        { 
            await _bleManager.SendPngAsync(colorFrame, true); 
            await Task.Delay(150); 
            await _bleManager.SendPngAsync(blackFrame, true); 
            await Task.Delay(150); 
        }
        
        _isAnimating = false;
    }

    private async void UpdatePomoFrame()
    {
        try 
        { 
            _isSendingFrame = true; 
            byte[] frame = RenderPomoUI(); 
            await _bleManager.SendPngAsync(frame, AppSettings.UseTurboMode); 
        }
        finally { _isSendingFrame = false; }
    }

    private byte[] RenderPomoUI()
    {
        RenderTargetBitmap bmp = new(MatrixWidth, MatrixHeight, 96, 96, PixelFormats.Pbgra32);
        DrawingVisual visual = new();
        using (DrawingContext ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(Brushes.Black, null, new Rect(0, 0, MatrixWidth, MatrixHeight));
            
            TimeSpan total = _isPomoBreak ? TimeSpan.FromMinutes(_pomoBreakMin) : TimeSpan.FromMinutes(_pomoWorkMin);
            double progress = _pomoTimeLeft.TotalSeconds / total.TotalSeconds;
            
            Color themeColor = _isPomoBreak ? Color.FromRgb(245, 158, 11) : Color.FromRgb(16, 185, 129); 
            SolidColorBrush themeBrush = new(themeColor);

            // Session Icon (Tomato vs Coffee/Box)
            if (!_isPomoBreak) 
            { 
                ctx.DrawEllipse(Brushes.Red, null, new Point(5, 5), 3, 3); 
                ctx.DrawRectangle(Brushes.LimeGreen, null, new Rect(4, 1, 2, 2)); 
            }
            else 
            { 
                ctx.DrawRectangle(Brushes.SaddleBrown, null, new Rect(2, 3, 6, 5)); 
                ctx.DrawRectangle(Brushes.White, null, new Rect(4, 1, 1, 2)); 
            }

            // Cycle Indicators (Dots)
            int dotSize = 2, spacing = 2;
            int startX = MatrixWidth - (_pomoTotalCycles * (dotSize + spacing)) - 2; 
            for(int i = 0; i < _pomoTotalCycles; i++) {
                Brush brush = (i < _pomoCurrentCycle) ? themeBrush : new SolidColorBrush(Color.FromRgb(40, 40, 40));
                ctx.DrawRectangle(brush, null, new Rect(startX + (i * (dotSize + spacing)), 4, dotSize, dotSize));
            }

            // Countdown Text
            FormattedText timeText = new(_pomoTimeLeft.ToString(@"mm\:ss"), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, 
                new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 11, themeBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            
            ctx.DrawText(timeText, new Point((MatrixWidth - timeText.Width) / 2, 12));

            // Progress Bar
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 30)), null, new Rect(2, MatrixHeight - 4, MatrixWidth - 4, 2)); 
            ctx.DrawRectangle(themeBrush, null, new Rect(2, MatrixHeight - 4, Math.Max(0, progress * (MatrixWidth - 4)), 2)); 
        }
        bmp.Render(visual); 
        return EncodeToPng(bmp);
    }

    #endregion

    #region Utility & Gallery

    private async void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected) return;
        StopAllActiveModes();
        _isPomoOnScreen = false; 
        await _bleManager.RestoreClockModeAsync();
    }

    private async void BtnLoadImage_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected) return;
        StopAllActiveModes();
        _isPomoOnScreen = false; 

        OpenFileDialog dlg = new() { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp" };
        if (dlg.ShowDialog() == true)
        {
            try {
                BitmapImage img = new(new Uri(dlg.FileName));
                RenderTargetBitmap resizer = new(MatrixWidth, MatrixHeight, 96, 96, PixelFormats.Pbgra32);
                DrawingVisual dv = new();
                using (DrawingContext dc = dv.RenderOpen()) dc.DrawImage(img, new Rect(0, 0, MatrixWidth, MatrixHeight));
                resizer.Render(dv);
                await _bleManager.SendPngAsync(EncodeToPng(resizer), AppSettings.UseTurboMode);
            } 
            catch (Exception ex) { UpdateLog($"Gallery Error: {ex.Message}"); }
        }
    }

    private byte[] EncodeToPng(BitmapSource bitmap)
    {
        using MemoryStream ms = new();
        PngBitmapEncoder encoder = new() { Interlace = PngInterlaceOption.Off };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(ms);
        return ms.ToArray();
    }

    private byte[] GenerateSolidColorFrame(Color color)
    {
        RenderTargetBitmap bmp = new(MatrixWidth, MatrixHeight, 96, 96, PixelFormats.Pbgra32);
        DrawingVisual visual = new();
        using (DrawingContext ctx = visual.RenderOpen()) ctx.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, MatrixWidth, MatrixHeight));
        bmp.Render(visual); 
        return EncodeToPng(bmp);
    }

    private void BtnLogs_Click(object sender, RoutedEventArgs e) 
    { 
        if (_logWindow == null) 
        { 
            _logWindow = new LogWindow(); 
            _logWindow.Closed += (s, a) => _logWindow = null; 
            _logWindow.SetHistory(_logHistory.ToString()); 
            _logWindow.Show(); 
        } 
        else _logWindow.Activate(); 
    }

    private void UpdateLog(string msg) 
    { 
        string entry = $"[{DateTime.Now:HH:mm:ss}] {msg}"; 
        _logHistory.AppendLine(entry); 
        _logWindow?.AddMessage(entry); 
    }

    #endregion
}