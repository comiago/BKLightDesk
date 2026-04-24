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

public class BleManager
{
    private static readonly Guid UUID_WRITE = Guid.Parse("0000fa02-0000-1000-8000-00805f9b34fb");
    private static readonly Guid UUID_NOTIFY = Guid.Parse("0000fa03-0000-1000-8000-00805f9b34fb");

    // Protocollo Immagini
    private static readonly byte[] HANDSHAKE_FIRST  = { 0x08, 0x00, 0x01, 0x80, 0x0E, 0x06, 0x32, 0x00 };
    private static readonly byte[] HANDSHAKE_SECOND = { 0x04, 0x00, 0x05, 0x80 };
    private static readonly byte[] ACK_STAGE_ONE   = { 0x0C, 0x00, 0x01, 0x80, 0x81, 0x06, 0x32, 0x00, 0x00, 0x01, 0x00, 0x01 };
    private static readonly byte[] ACK_STAGE_TWO   = { 0x08, 0x00, 0x05, 0x80, 0x0B, 0x03, 0x07, 0x02 };
    private static readonly byte[] ACK_STAGE_THREE = { 0x05, 0x00, 0x02, 0x00, 0x03 }; 

    private BluetoothLEAdvertisementWatcher _watcher;
    private BluetoothLEDevice? _device;
    
    private GattCharacteristic? _writeChar;   // Porta Immagini
    private GattCharacteristic? _commandChar; // Porta Comandi (Handle 6)
    private GattCharacteristic? _notifyChar;
    
    private TaskCompletionSource<bool>? _ackWaiter;
    private byte[]? _expectedAck;

    // La Sicura anti-zombie
    private volatile bool _isTurnedOff = false;

    public event Action<string>? LogMessage;
    public bool IsConnected => _device != null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected;

    public BleManager()
    {
        _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        _watcher.Received += OnAdvertisementReceived;
        _watcher.Stopped += (s, e) => Log("Scansione terminata.");
    }

    private void Log(string msg) => LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

    public async Task<string> CheckBluetoothAvailability()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter == null) return "Nessun modulo Bluetooth trovato.";
            var radio = await adapter.GetRadioAsync();
            if (radio != null && radio.State == RadioState.Off) return "Bluetooth spento! Attivalo nelle impostazioni.";
            return "OK";
        }
        catch (Exception ex) { return $"Errore controllo BT: {ex.Message}"; }
    }

    public void Connect() => StartScanning();

    public void StartScanning()
    {
        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started) return;
        try { Log("Avvio scansione..."); _watcher.Start(); }
        catch (Exception ex) { Log($"IMPOSSIBILE AVVIARE SCANSIONE: {ex.Message}"); }
    }

    public void StopScanning()
    {
        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started) { Log("Arresto scansione manuale..."); _watcher.Stop(); }
    }

    private async void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        string name = args.Advertisement.LocalName;
        if (!string.IsNullOrEmpty(name) && (name.Contains("LED") || name.Contains("Light") || name.Contains("Badge") || name.Contains("LS")))
        {
            _watcher.Stop();
            Log($"Trovato {name}. Connessione...");
            await ConnectAndSetup(args.BluetoothAddress);
        }
    }

    private async Task ConnectAndSetup(ulong address)
    {
        try
        {
            _device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (_device == null) { Log("Impossibile connettersi."); return; }
            var services = await _device.GetGattServicesAsync();
            if (services.Status != GattCommunicationStatus.Success) return;

            _writeChar = null; _commandChar = null; _notifyChar = null;
            _isTurnedOff = false; 

            foreach (var service in services.Services)
            {
                var chars = await service.GetCharacteristicsAsync();
                foreach (var c in chars.Characteristics)
                {
                    if (c.AttributeHandle == 0x0006)
                    {
                        _commandChar = c;
                        Log($"✅ Trovata porta COMANDI (Handle: 0x0006)");
                    }
                    if (c.Uuid == UUID_WRITE) 
                    {
                        _writeChar = c;
                        Log($"✅ Trovata porta IMMAGINI");
                    }
                    if (c.Uuid == UUID_NOTIFY) _notifyChar = c;
                }
            }

            if (_commandChar == null) { Log("⚠️ Handle 0x0006 non trovato. Uso porta standard."); _commandChar = _writeChar; }
            if (_writeChar == null) { Log("ERRORE: Caratteristiche non trovate."); return; }
            
            try { 
                if (_notifyChar != null) {
                    await _notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify); 
                    _notifyChar.ValueChanged += OnNotificationReceived; 
                }
            } catch {}
            
            Log("✅ Connesso e pronto.");
        }
        catch (Exception ex) { Log($"Errore setup: {ex.Message}"); }
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

    // --- LUMINOSITÀ ---
    public async Task SetBrightnessAsync(int percentage)
    {
        if (_commandChar == null) return;
        try
        {
            if (percentage < 0) percentage = 0;
            if (percentage > 100) percentage = 100;

            byte[] cmd = { 0x05, 0x00, 0x04, 0x80, (byte)percentage };

            var writer = new DataWriter(); writer.WriteBytes(cmd);
            await _commandChar.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
        }
        catch (Exception ex) { Log($"Errore Luminosità: {ex.Message}"); }
    }

    // --- POWER ON/OFF ---
    public async Task SetPowerAsync(bool turnOn)
    {
        if (_commandChar == null) return;
        try
        {
            Log(turnOn ? "Accensione..." : "Spegnimento...");

            if (!turnOn) _isTurnedOff = true;
            else _isTurnedOff = false;

            if (!turnOn) await Task.Delay(150); 

            byte state = turnOn ? (byte)0x01 : (byte)0x00;
            byte[] cmd = { 0x05, 0x00, 0x07, 0x01, state };

            var writer = new DataWriter(); writer.WriteBytes(cmd);
            await _commandChar.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
        }
        catch (Exception ex) { Log($"Errore Power: {ex.Message}"); }
    }
    
    // --- INVIO IMMAGINI (CON SBLOCCO OROLOGIO) ---
    public async Task SendPngAsync(byte[] pngBytes, bool useTurbo)
    {
        if (_isTurnedOff) return; 

        if (_writeChar == null) { Log("Non connesso."); return; }
        
        try
        {
            // --- FIX SBLOCCO: DISATTIVA MODALITÀ OROLOGIO ---
            // Inviamo 04 00 05 00 sulla porta DATI. 
            // Questo dice alla matrice: "Smetti di fare l'orologio e ascolta me!"
            if (_writeChar != null)
            {
                byte[] appMode = { 0x04, 0x00, 0x05, 0x00 }; 
                var w = new DataWriter(); w.WriteBytes(appMode);
                await _writeChar.WriteValueAsync(w.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
                await Task.Delay(60); // Diamo tempo alla matrice di cambiare stato
            }
            // --------------------------------------------------

            // Tentativo di Handshake con retry
            // A volte il primo colpo fallisce perché la matrice si sta svegliando
            if (!await WriteAndWait(_writeChar, HANDSHAKE_FIRST, ACK_STAGE_ONE, true, 2000)) 
            {
                Log("Primo handshake fallito, riprovo...");
                await Task.Delay(100);
                // Riprova handshake
                if (!await WriteAndWait(_writeChar, HANDSHAKE_FIRST, ACK_STAGE_ONE, true, 2000))
                {
                    Log("Errore: La matrice non risponde.");
                    return;
                }
            }
            
            if (_isTurnedOff) return;

            await Task.Delay(30); 
            try { await WriteAndWait(_writeChar, HANDSHAKE_SECOND, ACK_STAGE_TWO, true, 800); } catch {}
            if (_isTurnedOff) return;

            await Task.Delay(50);

            ushort dataLen = (ushort)pngBytes.Length;
            ushort totalLen = (ushort)(dataLen + 15);
            uint crc = Crc32.Compute(pngBytes);

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(totalLen); bw.Write((byte)0x02); bw.Write((ushort)0x0000);
                bw.Write(dataLen);  bw.Write((ushort)0x0000); bw.Write(crc);
                bw.Write((byte)0x00); bw.Write((byte)0x65); bw.Write(pngBytes);

                byte[] fullFrame = ms.ToArray();
                if (useTurbo) await SendDataTurbo(fullFrame);
                else await SendDataPrudent(fullFrame);

                if (_isTurnedOff) return;
                
                // Ignora ACK finale mancante (spesso succede dopo lo switch di modalità)
                if (!await WaitForAck(ACK_STAGE_THREE, 5000)) 
                {
                   // Log("Info: Ack finale non ricevuto (normale dopo lo switch).");
                }
            }
        }
        catch (Exception ex) { Log($"Errore invio: {ex.Message}"); }
    }

    // --- RIPRISTINO OROLOGIO NATIVO ---
    public async Task RestoreClockModeAsync()
    {
        if (_writeChar == null) { Log("Non connesso."); return; }

        try
        {
            _isTurnedOff = false; 
            Log("--- RIPRISTINO FIRMWARE ORARIO ---");
            DateTime now = DateTime.Now;

            byte year = (byte)(now.Year - 2000);
            byte month = (byte)now.Month;
            byte day = (byte)now.Day;
            byte weekday = (byte)(now.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)now.DayOfWeek);

            // DATA
            byte[] dateCmd = new byte[] { 0x0B, 0x00, 0x06, 0x01, 0x04, 0x01, 0x00, year, month, day, weekday };
            var w1 = new DataWriter(); w1.WriteBytes(dateCmd);
            await _writeChar.WriteValueAsync(w1.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
            await Task.Delay(100); 

            // ORA
            byte[] timeCmd = new byte[] { 0x08, 0x00, 0x01, 0x80, (byte)now.Hour, (byte)now.Minute, (byte)now.Second, 0x00 };
            var w2 = new DataWriter(); w2.WriteBytes(timeCmd);
            await _writeChar.WriteValueAsync(w2.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
            await Task.Delay(100);

            // MODALITÀ CLOCK (Attiva il blocco)
            byte[] modeCmd = new byte[] { 0x04, 0x00, 0x05, 0x80 };
            var w3 = new DataWriter(); w3.WriteBytes(modeCmd);
            await _writeChar.WriteValueAsync(w3.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
            
            Log("✅ Ripristino completato.");
        }
        catch (Exception ex) { Log($"ERRORE RESTORE: {ex.Message}"); }
    }

    private async Task SendDataTurbo(byte[] data)
    {
        int chunkSize = 240; int offset = 0; int packetCount = 0;
        while (offset < data.Length)
        {
            if (_isTurnedOff) return; 
            int size = Math.Min(chunkSize, data.Length - offset);
            byte[] chunk = new byte[size];
            Array.Copy(data, offset, chunk, 0, size);
            var writer = new DataWriter(); writer.WriteBytes(chunk);
            try { await _writeChar!.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse); } 
            catch { if (chunkSize > 20) { chunkSize = 20; continue; } throw; }
            offset += size; packetCount++;
            if (packetCount % 10 == 0) await Task.Delay(10); 
        }
    }

    private async Task SendDataPrudent(byte[] data)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            if (_isTurnedOff) return;
            int size = Math.Min(20, data.Length - offset);
            byte[] chunk = new byte[size];
            Array.Copy(data, offset, chunk, 0, size);
            var writer = new DataWriter(); writer.WriteBytes(chunk);
            await _writeChar!.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
            offset += size; await Task.Delay(20); 
        }
    }

    private async Task<bool> WriteAndWait(GattCharacteristic charToWrite, byte[] data, byte[] expectedAck, bool useResponse = false, int timeoutMs = 4000)
    {
        _expectedAck = expectedAck; _ackWaiter = new TaskCompletionSource<bool>();
        var writer = new DataWriter(); writer.WriteBytes(data);
        var option = useResponse ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
        await charToWrite.WriteValueAsync(writer.DetachBuffer(), option);
        var completed = await Task.WhenAny(_ackWaiter.Task, Task.Delay(timeoutMs));
        _ackWaiter = null; _expectedAck = null;
        return completed is Task<bool> t && t.Result;
    }

    private async Task<bool> WaitForAck(byte[] ack, int timeoutMs)
    {
        _expectedAck = ack; _ackWaiter = new TaskCompletionSource<bool>();
        var completed = await Task.WhenAny(_ackWaiter.Task, Task.Delay(timeoutMs));
        _ackWaiter = null; _expectedAck = null;
        return completed is Task<bool> t && t.Result;
    }

    public void Disconnect() { try { _watcher.Stop(); if (_device != null) _device.Dispose(); _device = null; _writeChar = null; } catch {} }
}