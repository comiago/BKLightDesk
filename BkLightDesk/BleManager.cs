using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BkLightDesk;

public class BleManager
{
    // UUID (Logic)
    private static readonly Guid UUID_SERVICE = Guid.Parse("0000fa00-0000-1000-8000-00805f9b34fb");
    private static readonly Guid UUID_WRITE   = Guid.Parse("0000fa02-0000-1000-8000-00805f9b34fb");
    private static readonly Guid UUID_NOTIFY  = Guid.Parse("0000fa03-0000-1000-8000-00805f9b34fb");

    // Handshake bytes
    private static readonly byte[] HANDSHAKE_FIRST  = { 0x08, 0x00, 0x01, 0x80, 0x0E, 0x06, 0x32, 0x00 };
    private static readonly byte[] HANDSHAKE_SECOND = { 0x04, 0x00, 0x05, 0x80 };
    private static readonly byte[] ACK_STAGE_ONE = { 0x0C, 0x00, 0x01, 0x80, 0x81, 0x06, 0x32, 0x00, 0x00, 0x01, 0x00, 0x01 };
    private static readonly byte[] ACK_STAGE_TWO = { 0x08, 0x00, 0x05, 0x80, 0x0B, 0x03, 0x07, 0x02 };

    private BluetoothLEAdvertisementWatcher _watcher;
    private BluetoothLEDevice? _device;
    private GattCharacteristic? _writeChar;
    private GattCharacteristic? _notifyChar;

    private TaskCompletionSource<bool>? _ackWaiter;
    private byte[]? _expectedAck;

    public event Action<string>? LogMessage;

    // Proprietà helper
    public bool IsConnected 
    {
        get { return _device != null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected; }
    }

    public BleManager()
    {
        _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        _watcher.Received += OnAdvertisementReceived;
    }

    private void Log(string msg) => LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

    // Alias per compatibilità
    public void Connect() => StartScanning();

    public void StartScanning()
    {
        try
        {
            Log("Avvio scansione...");
            _watcher.Start();
        }
        catch (Exception ex)
        {
            Log($"Errore avvio scansione: {ex.Message}");
        }
    }

    // Alias per compatibilità
    public async Task InviaImmagineAsync(byte[] data) => await SendImageAsync(data);

    private async void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        string name = args.Advertisement.LocalName;
        // Filtro per trovare il badge
        if (!string.IsNullOrEmpty(name) && (name.Contains("LED") || name.Contains("Light") || name.Contains("Badge")))
        {
            _watcher.Stop();
            Log($"Trovato {name}. Connessione in corso...");
            await ConnectAndSetup(args.BluetoothAddress);
        }
    }

    private async Task ConnectAndSetup(ulong address)
    {
        try
        {
            _device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (_device == null) return;

            var services = await _device.GetGattServicesAsync();
            if (services.Status != GattCommunicationStatus.Success) { Log("Impossibile leggere servizi."); return; }

            foreach (var service in services.Services)
            {
                var chars = await service.GetCharacteristicsAsync();
                foreach (var c in chars.Characteristics)
                {
                    if (c.Uuid == UUID_WRITE) _writeChar = c;
                    if (c.Uuid == UUID_NOTIFY) _notifyChar = c;
                }
            }

            if (_writeChar == null || _notifyChar == null)
            {
                Log("ERRORE: Caratteristiche UUID non trovate.");
                return;
            }

            await _notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
            _notifyChar.ValueChanged += OnNotificationReceived;

            Log("Connesso. Avvio Handshake...");
            bool handshakeOk = await PerformHandshake();
            if (handshakeOk) Log("✅ PRONTO.");
            else Log("❌ Handshake fallito.");
        }
        catch (Exception ex)
        {
            Log($"Errore connessione: {ex.Message}");
        }
    }

    private void OnNotificationReceived(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        byte[] data = new byte[args.CharacteristicValue.Length];
        DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(data);

        if (_ackWaiter != null && _expectedAck != null && !_ackWaiter.Task.IsCompleted)
        {
            if (data.SequenceEqual(_expectedAck)) _ackWaiter.TrySetResult(true);
        }
    }

    private async Task<bool> PerformHandshake()
    {
        try
        {
            if (!await WriteAndWait(_writeChar, HANDSHAKE_FIRST, ACK_STAGE_ONE)) return false;
            try { await WriteAndWait(_writeChar, HANDSHAKE_SECOND, ACK_STAGE_TWO, 2000); }
            catch (TimeoutException) { /* Ignora eventuale timeout su step 2 */ }
            return true;
        }
        catch (Exception ex)
        {
            Log($"Errore Handshake: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> WriteAndWait(GattCharacteristic? charToWrite, byte[] data, byte[] expectedAck, int timeoutMs = 5000)
    {
        if (charToWrite == null) return false;
        _expectedAck = expectedAck;
        _ackWaiter = new TaskCompletionSource<bool>();

        var writer = new DataWriter();
        writer.WriteBytes(data);
        await charToWrite.WriteValueAsync(writer.DetachBuffer());

        var completed = await Task.WhenAny(_ackWaiter.Task, Task.Delay(timeoutMs));
        _ackWaiter = null; _expectedAck = null;

        return completed is Task<bool> t && t.Result;
    }

    public async Task SendImageAsync(byte[] pngBytes)
    {
        if (_writeChar == null) { Log("Non connesso."); return; }
        try 
        {
            // Logica di invio (Build Frame)
            ushort dataLength = (ushort)pngBytes.Length;
            ushort totalLength = (ushort)(dataLength + 15);
            uint crc = Crc32.Compute(pngBytes);

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(totalLength);
                bw.Write((byte)0x02);
                bw.Write((ushort)0x0000);
                bw.Write(dataLength);
                bw.Write((ushort)0x0000);
                bw.Write(crc);
                bw.Seek(0, SeekOrigin.Current); 
                bw.Write(new byte[] { 0x00, 0x65 }); 
                bw.Write(pngBytes);

                byte[] frame = ms.ToArray();
                var writer = new DataWriter();
                writer.WriteBytes(frame);
                await _writeChar.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse);
                Log("Immagine inviata!");
            }
        }
        catch (Exception ex)
        {
            Log($"Errore invio immagine: {ex.Message}");
        }
    }
    
    public void Disconnect()
    {
        try
        {
            // Ferma la scansione se era in corso
            _watcher.Stop();

            // Disiscriviti dalle notifiche se necessario
            if (_notifyChar != null)
            {
                // Non è strettamente obbligatorio perché il Dispose chiude tutto, 
                // ma è buona pratica in alcuni contesti.
                _notifyChar.ValueChanged -= OnNotificationReceived;
            }

            // Chiudi la connessione
            if (_device != null)
            {
                _device.Dispose(); // Questo taglia la connessione Bluetooth
                _device = null;
            }

            _writeChar = null;
            _notifyChar = null;

            Log("Disconnessione completata.");
        }
        catch (Exception ex)
        {
            Log($"Errore durante la disconnessione: {ex.Message}");
        }
    }
}