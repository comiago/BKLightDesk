using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace BkLightDesk;

/// <summary>
/// Logic for the Settings window, handling device configuration and persistence.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly BleManager _bleManager; 
    private bool _isUpdatingInternally = false; 

    public SettingsWindow(BleManager bleManager)
    {
        InitializeComponent();
        _bleManager = bleManager;

        LoadCurrentSettings();
    }

    /// <summary>
    /// Initializes UI components with saved values from SettingsManager.
    /// </summary>
    private void LoadCurrentSettings()
    {
        SettingsManager.Load(); 
        
        // Setup Turbo Mode checkbox
        ChkTurbo.IsChecked = AppSettings.UseTurboMode;

        // Setup Brightness controls
        _isUpdatingInternally = true;
        
        int savedBrightness = SettingsManager.Brightness;
        SliderBrightness.Value = savedBrightness;
        
        if (TxtBrightnessInput != null) 
            TxtBrightnessInput.Text = savedBrightness.ToString();
            
        _isUpdatingInternally = false;
    }

    #region Turbo Mode Logic

    private void ChkTurbo_Checked(object sender, RoutedEventArgs e) => AppSettings.UseTurboMode = true;
    private void ChkTurbo_Unchecked(object sender, RoutedEventArgs e) => AppSettings.UseTurboMode = false;

    #endregion

    #region Brightness Slider Management

    private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtBrightnessInput == null || _isUpdatingInternally) return;

        int value = (int)e.NewValue;

        // Synchronize TextBox text without triggering a save/send yet
        _isUpdatingInternally = true;
        TxtBrightnessInput.Text = value.ToString();
        _isUpdatingInternally = false;
    }

    /// <summary>
    /// Triggered when the user releases the mouse button from the slider.
    /// This prevents flooding the BLE characteristic with too many updates during a drag.
    /// </summary>
    private void SliderBrightness_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        int value = (int)SliderBrightness.Value;
        CommitBrightnessUpdate(value);
    }

    #endregion

    #region Text Input Management

    /// <summary>
    /// Restricts TextBox input to numeric characters only.
    /// </summary>
    private void TxtBrightnessInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        Regex regex = new Regex("[^0-9]+"); 
        e.Handled = regex.IsMatch(e.Text);
    }

    private void TxtBrightnessInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingInternally || TxtBrightnessInput == null) return;
        
        if (string.IsNullOrEmpty(TxtBrightnessInput.Text)) return;

        if (int.TryParse(TxtBrightnessInput.Text, out int newValue))
        {
            // Cap value at 100%
            if (newValue > 100)
            {
                newValue = 100;
                _isUpdatingInternally = true;
                TxtBrightnessInput.Text = "100";
                TxtBrightnessInput.SelectionStart = 3; 
                _isUpdatingInternally = false;
            }
            
            // Sync Slider position
            _isUpdatingInternally = true;
            SliderBrightness.Value = newValue;
            _isUpdatingInternally = false;

            // Commit changes to disk and device
            CommitBrightnessUpdate(newValue);
        }
    }

    #endregion

    /// <summary>
    /// Saves the brightness value to the SettingsManager and sends the command to the hardware.
    /// </summary>
    private async void CommitBrightnessUpdate(int value)
    {
        // 1. Persist to local storage
        SettingsManager.Brightness = value;

        // 2. Transmit to BLE Device if connected
        if (_bleManager != null && _bleManager.IsConnected && value >= 0)
        {
            try 
            {
                await _bleManager.SetBrightnessAsync(value);
            }
            catch (Exception)
            {
                // Silent fail: Device might have disconnected during update
            }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}