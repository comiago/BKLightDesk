using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios; 
using Windows.Storage.Streams;

namespace BkLightDesk;

/// <summary>
/// Manages Bluetooth Low Energy (BLE) communication with the LED Matrix.
/// Implements proprietary protocol logic discovered via iPixel Color app sniffing.
/// </summary>
public class BleManager
{
    // --- GATT Identifiers ---
    private static readonly Guid UuidWrite = Guid.Parse("0000fa02-0000-1000-8000-00805f9b34fb");
    private static readonly Guid UuidNotify = Guid.Parse("0000fa03-0000-1000-8000-00805f9b34fb");

    // --- Protocol Constants ---
    private static readonly byte[] AckStageThree = { 0x05, 0x00, 0x02, 0x00, 0x03 }; 

    // --- State & Handlers ---
    private readonly BluetoothLEAdvertisementWatcher _advWatcher;
    private BluetoothLEDevice? _device;
    
    private GattCharacteristic? _writeChar;   // Data port
    private GattCharacteristic? _commandChar; // Control port (Sniffed Handle: 0x0006)
    private GattCharacteristic? _notifyChar;
    
    private TaskCompletionSource<bool>? _ackWaiter;
    private byte[]? _expectedAck;
    private volatile bool _isOperationCancelled = false;

    public event Action<string>? LogMessage;
    public bool IsConnected => _device != null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected;

    public BleManager()
    {
        _advWatcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        _advWatcher.Received += OnAdvertisementReceived;
        _advWatcher.Stopped += (s, e) => Log("Scanning process stopped.");
    }

    private void Log(string msg) => LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

    #region Connectivity

    /// <summary>
    /// Checks if the local Bluetooth radio is available and enabled.
    /// </summary>
    public async Task<string> CheckBluetoothAvailability()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter == null) return "No Bluetooth adapter found on this system.";
            
            var radio = await adapter.GetRadioAsync();
            if (radio != null && radio.State == RadioState.Off) 
                return "Bluetooth is OFF. Please enable it in Windows Settings.";
            
            return "OK";
        }
        catch (Exception ex) { return $"BT Check Error: {ex.Message}"; }
    }

    public void Connect() => StartScanning();

    private void StartScanning()
    {
        if (_advWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Started) return;
        try 
        { 
            Log("Starting device discovery..."); 
            _advWatcher.Start(); 
        }
        catch (Exception ex) { Log($"SCAN FAILURE: {ex.Message}"); }
    }

    private async void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        string name = args.Advertisement.LocalName;
        // Filtering based on known matrix naming conventions (LS, LED, Light)
        if (!string.IsNullOrEmpty(name) && (name.Contains("LED") || name.Contains("Light") || name.Contains("Badge") || name.Contains("LS")))
        {
            _advWatcher.Stop();
            Log($"Device found: {name}. Initiating connection...");
            await SetupConnectionAsync(args.BluetoothAddress);
        }
    }

    private async Task SetupConnectionAsync(ulong address)
    {
        try
        {
            _device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (_device == null) { Log("Connection failed: Device unreachable."); return; }
            
            var services = await _device.GetGattServicesAsync();
            if (services.Status != GattCommunicationStatus.Success) return;

            _writeChar = null; _commandChar = null; _notifyChar = null;
            _isOperationCancelled = false; 

            foreach (var service in services.Services)
            {
                var chars = await service.GetCharacteristicsAsync();
                foreach (var c in chars.Characteristics)
                {
                    // Sniffing result: Handle 0x0006 is the primary command gateway
                    if (c.AttributeHandle == 0x0006)
                    {
                        _commandChar = c;
                        Log("✅ Command Interface identified (Handle: 0x0006)");
                    }
                    if (c.Uuid == UuidWrite) 
                    {
                        _writeChar = c;
                        Log("✅ Data Interface identified (Write)");
                    }
                    if (c.Uuid == UuidNotify) _notifyChar = c;
                }
            }

            // Fallback if specific handle is missing
            if (_commandChar == null) _commandChar = _writeChar;
            
            if (_writeChar == null) { Log("FATAL: Required GATT characteristics not found."); return; }
            
            // Setup Notifications for ACKs
            if (_notifyChar != null) {
                await _notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify); 
                _notifyChar.ValueChanged += OnNotificationReceived; 
            }
            
            Log("✅ Hardware Handshake complete. READY.");
        }
        catch (Exception ex) { Log($"Setup Error: {ex.Message}"); }
    }

    private void OnNotificationReceived(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        byte[] data = new byte[args.CharacteristicValue.Length];
        DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(data);
        
        if (_ackWaiter != null && _expectedAck != null && !_ackWaiter.Task.IsCompleted)
        {
            if (data.Length >= _expectedAck.Length && data.Take(_expectedAck.Length).SequenceEqual(_expectedAck))
                _ackWaiter.TrySetResult(true);
        }
    }

    #endregion

    #region Hardware Control Commands

    public async Task SetBrightnessAsync(int percentage)
    {
        if (_commandChar == null) return;
        try
        {
            percentage = Math.Clamp(percentage, 0, 100);
            byte[] cmd = { 0x05, 0x00, 0x04, 0x80, (byte)percentage };

            var writer = new DataWriter(); writer.WriteBytes(cmd);
            await _commandChar.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
        }
        catch (Exception ex) { Log($"Brightness Command Error: {ex.Message}"); }
    }

    public async Task SetPowerAsync(bool turnOn)
    {
        if (_commandChar == null) return;
        try
        {
            Log(turnOn ? "Powering ON..." : "Powering OFF...");
            _isOperationCancelled = !turnOn;

            if (!turnOn) await Task.Delay(150); 

            byte state = turnOn ? (byte)0x01 : (byte)0x00;
            byte[] cmd = { 0x05, 0x00, 0x07, 0x01, state };

            var writer = new DataWriter(); writer.WriteBytes(cmd);
            await _commandChar.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
        }
        catch (Exception ex) { Log($"Power Command Error: {ex.Message}"); }
    }
    
    #endregion

    #region Data Streaming (PNG Protocol)

    /// <summary>
    /// Encapsulates PNG data into a proprietary frame and streams it to the matrix.
    /// </summary>
    public async Task SendPngAsync(byte[] pngBytes, bool useTurbo)
    {
        if (_isOperationCancelled || _writeChar == null) return; 
        
        try
        {
            // 1. Enter Image Transfer Mode (Mode 0x00)
            byte[] modeImageCmd = { 0x04, 0x00, 0x05, 0x00 };
            var wMode = new DataWriter(); wMode.WriteBytes(modeImageCmd);
            await _writeChar.WriteValueAsync(wMode.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
            
            await Task.Delay(30); // Brief hardware pause

            if (_isOperationCancelled) return;

            // 2. Prepare Proprietary Frame
            ushort dataLen = (ushort)pngBytes.Length;
            ushort totalLen = (ushort)(dataLen + 15);
            uint crc = Crc32.Compute(pngBytes);

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            
            // Reconstructed Frame Header from Sniffing
            bw.Write(totalLen);         // 0-1: Total Packet Length
            bw.Write((byte)0x02);       // 2: Command Type (Image)
            bw.Write((ushort)0x0000);   // 3-4: Reserved
            bw.Write(dataLen);          // 5-6: Data Length
            bw.Write((ushort)0x0000);   // 7-8: Reserved
            bw.Write(crc);              // 9-12: CRC32 Checksum
            bw.Write((byte)0x00);       // 13: Magic Byte A
            bw.Write((byte)0x65);       // 14: Magic Byte B
            bw.Write(pngBytes);         // 15+: PNG Payload

            byte[] fullFrame = ms.ToArray();
            
            // 3. Transmission logic based on selected throughput mode
            if (useTurbo) 
                await SendDataTurboAsync(fullFrame);
            else 
                await SendDataReliableAsync(fullFrame);

            if (_isOperationCancelled) return;
            
            // Silent wait for transmission end ACK
            await WaitForAckAsync(AckStageThree, 800);
        }
        catch (Exception ex) { Log($"Stream Error: {ex.Message}"); }
    }

    public async Task RestoreClockModeAsync()
    {
        if (_writeChar == null) { Log("Offline: Restore impossible."); return; }

        try
        {
            _isOperationCancelled = false; 
            Log("--- Syncing Matrix Hardware Clock ---");
            DateTime now = DateTime.Now;

            byte year = (byte)(now.Year - 2000);
            byte month = (byte)now.Month;
            byte day = (byte)now.Day;
            byte weekday = (byte)(now.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)now.DayOfWeek);

            // Sync Date Packet
            byte[] dateCmd = { 0x0B, 0x00, 0x06, 0x01, 0x04, 0x01, 0x00, year, month, day, weekday };
            await _writeChar.WriteValueAsync(dateCmd.AsBuffer(), GattWriteOption.WriteWithoutResponse);
            await Task.Delay(100); 

            // Sync Time Packet
            byte[] timeCmd = { 0x08, 0x00, 0x01, 0x80, (byte)now.Hour, (byte)now.Minute, (byte)now.Second, 0x00 };
            await _writeChar.WriteValueAsync(timeCmd.AsBuffer(), GattWriteOption.WriteWithoutResponse);
            await Task.Delay(100);

            // Activate Native Clock Mode (Flag 0x80)
            byte[] modeCmd = { 0x04, 0x00, 0x05, 0x80 };
            await _writeChar.WriteValueAsync(modeCmd.AsBuffer(), GattWriteOption.WriteWithoutResponse);
            
            Log("✅ Clock Sync complete.");
        }
        catch (Exception ex) { Log($"RESTORE FAILURE: {ex.Message}"); }
    }

    #endregion

    #region Low-Level Streaming Logic

    private async Task SendDataTurboAsync(byte[] data)
    {
        int chunkSize = 240; 
        int offset = 0; 
        int packetCounter = 0;

        while (offset < data.Length)
        {
            if (_isOperationCancelled) return; 
            
            int size = Math.Min(chunkSize, data.Length - offset);
            byte[] chunk = new byte[size];
            Array.Copy(data, offset, chunk, 0, size);
            
            try 
            { 
                await _writeChar!.WriteValueAsync(chunk.AsBuffer(), GattWriteOption.WriteWithoutResponse); 
            } 
            catch 
            { 
                // Throttle down if MTU exchange was not optimal
                if (chunkSize > 20) { chunkSize = 20; continue; } 
                throw; 
            }
            
            offset += size; 
            packetCounter++;
            
            // Brief pause every 10 packets to allow hardware buffers to clear
            if (packetCounter % 10 == 0) await Task.Delay(10); 
        }
    }

    private async Task SendDataReliableAsync(byte[] data)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            if (_isOperationCancelled) return;
            
            int size = Math.Min(20, data.Length - offset);
            byte[] chunk = new byte[size];
            Array.Copy(data, offset, chunk, 0, size);
            
            await _writeChar!.WriteValueAsync(chunk.AsBuffer(), GattWriteOption.WriteWithoutResponse);
            
            offset += size; 
            await Task.Delay(20); // Strict GATT compliance delay
        }
    }

    private async Task<bool> WaitForAckAsync(byte[] ack, int timeoutMs)
    {
        _expectedAck = ack; 
        _ackWaiter = new TaskCompletionSource<bool>();
        
        var completed = await Task.WhenAny(_ackWaiter.Task, Task.Delay(timeoutMs));
        
        _ackWaiter = null; 
        _expectedAck = null;
        
        return completed is Task<bool> t && t.Result;
    }

    public void Disconnect() 
    { 
        try 
        { 
            _advWatcher.Stop(); 
            if (_device != null) 
            {
                _device.Dispose(); 
                _device = null;
            }
            _writeChar = null; 
        } 
        catch {} 
    }

    #endregion
}