using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace BkLightDesk;

public partial class SettingsWindow : Window
{
    private BleManager _bleManager; 
    private bool _isUpdatingInternally = false; 

    public SettingsWindow(BleManager bleManager)
    {
        InitializeComponent();
        _bleManager = bleManager;
        ChkTurbo.IsChecked = AppSettings.UseTurboMode;
    }

    private void ChkTurbo_Checked(object sender, RoutedEventArgs e) => AppSettings.UseTurboMode = true;
    private void ChkTurbo_Unchecked(object sender, RoutedEventArgs e) => AppSettings.UseTurboMode = false;
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // 1. GESTIONE SLIDER
    private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // FIX CRASH: Se la casella non esiste ancora, esci
        if (TxtBrightnessInput == null) return; 
        
        if (_isUpdatingInternally) return;

        int val = (int)e.NewValue;

        // Logica Magnetica Visiva
        _isUpdatingInternally = true;
        TxtBrightnessInput.Text = val.ToString();
        _isUpdatingInternally = false;
    }

    private async void SliderBrightness_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        int val = (int)SliderBrightness.Value;
        if (_bleManager != null && _bleManager.IsConnected) await _bleManager.SetBrightnessAsync(val);
    }

    // 2. GESTIONE INPUT TESTUALE
    private void TxtBrightnessInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        Regex regex = new Regex("[^0-9]+"); 
        e.Handled = regex.IsMatch(e.Text);
    }

    private async void TxtBrightnessInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingInternally) return;
        if (TxtBrightnessInput == null || string.IsNullOrEmpty(TxtBrightnessInput.Text)) return;

        if (int.TryParse(TxtBrightnessInput.Text, out int newVal))
        {
            if (newVal > 100)
            {
                newVal = 100;
                _isUpdatingInternally = true;
                TxtBrightnessInput.Text = "100";
                TxtBrightnessInput.SelectionStart = 3; 
                _isUpdatingInternally = false;
            }
            
            _isUpdatingInternally = true;
            SliderBrightness.Value = newVal;
            _isUpdatingInternally = false;

            if (_bleManager != null && _bleManager.IsConnected && newVal > 0)
            {
                await _bleManager.SetBrightnessAsync(newVal);
            }
        }
    }
}