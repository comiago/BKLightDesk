using System.Windows;

namespace BkLightDesk;

/// <summary>
/// Logic for the Pomodoro Setup window. 
/// Captures user-defined intervals for the focus session.
/// </summary>
public partial class PomodoroWindow : Window
{
    public int WorkMinutes { get; private set; }
    public int BreakMinutes { get; private set; }
    public int Cycles { get; private set; }

    public PomodoroWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Validates inputs and returns the result to the MainWindow.
    /// </summary>
    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        // Parse input values with safe defaults if parsing fails or values are invalid
        if (int.TryParse(TxtWork.Text, out int work) && work > 0) 
            WorkMinutes = work; 
        else 
            WorkMinutes = 25;

        if (int.TryParse(TxtBreak.Text, out int pause) && pause > 0) 
            BreakMinutes = pause; 
        else 
            BreakMinutes = 5;

        if (int.TryParse(TxtCycles.Text, out int totalCycles) && totalCycles > 0) 
            Cycles = totalCycles; 
        else 
            Cycles = 4;

        // Signal to MainWindow that the setup was successful
        this.DialogResult = true; 
        this.Close();
    }
}