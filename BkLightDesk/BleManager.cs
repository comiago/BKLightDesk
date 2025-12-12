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
    // UUID del dispositivo
    private static readonly Guid UUID_WRITE = Guid.Parse("0000fa02-0000-1000-8000-00805f9b34fb");
    private static readonly Guid UUID_NOTIFY = Guid.Parse("0000fa03-0000-1000-8000-00805f9b34fb");

    // Comandi di Protocollo
    private static readonly byte[] HANDSHAKE_FIRST  = { 0x08, 0x00, 0x01, 0x80, 0x0E, 0x06, 0x32, 0x00 };
    private static readonly byte[] HANDSHAKE_SECOND = { 0x04, 0x00, 0x05, 0x80 };
    
    // Risposte Attese (ACK)
    private static readonly byte[] ACK_STAGE_ONE   = { 0x0C, 0x00, 0x01, 0x80, 0x81, 0x06, 0x32, 0x00, 0x00, 0x01, 0x00, 0x01 };
    private static readonly byte[] ACK_STAGE_TWO   = { 0x08, 0x00, 0x05, 0x80, 0x0B, 0x03, 0x07, 0x02 };
    private static readonly byte[] ACK_STAGE_THREE = { 0x05, 0x00, 0x02, 0x00, 0x03 }; 

    private BluetoothLEAdvertisementWatcher _watcher;
    private BluetoothLEDevice? _device;
    private GattCharacteristic? _writeChar;
    private GattCharacteristic? _notifyChar;

    private TaskCompletionSource<bool>? _ackWaiter;
    private byte[]? _expectedAck;

    public event Action<string>? LogMessage;
    public bool IsConnected => _device != null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected;

    public BleManager()
    {
        _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        _watcher.Received += OnAdvertisementReceived;
    }

    private void Log(string msg) => LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");
    public void Connect() => StartScanning();

    public void StartScanning()
    {
        Log("Avvio scansione...");
        _watcher.Start();
    }

    private async void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        string name = args.Advertisement.LocalName;
        // Filtro per trovare il dispositivo (aggiungi altri nomi se necessario)
        if (!string.IsNullOrEmpty(name) && 
           (name.Contains("LED") || name.Contains("Light") || name.Contains("Badge") || name.Contains("LS")))
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
            if (_device == null) return;

            var services = await _device.GetGattServicesAsync();
            if (services.Status != GattCommunicationStatus.Success) { Log("Errore servizi."); return; }

            foreach (var service in services.Services)
            {
                var chars = await service.GetCharacteristicsAsync();
                foreach (var c in chars.Characteristics)
                {
                    if (c.Uuid == UUID_WRITE) _writeChar = c;
                    if (c.Uuid == UUID_NOTIFY) _notifyChar = c;
                }
            }

            if (_writeChar == null || _notifyChar == null) { Log("ERRORE: Caratteristiche non trovate."); return; }

            try {
                await _notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                _notifyChar.ValueChanged += OnNotificationReceived;
            } catch {}

            Log("✅ Connesso.");
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

    /// <summary>
    /// Invia un'immagine PNG.
    /// useTurbo = true -> Veloce (Burst), false = Prudente (Lento e sicuro)
    /// </summary>
    public async Task SendPngAsync(byte[] pngBytes, bool useTurbo)
    {
        if (_writeChar == null) { Log("Non connesso."); return; }

        try
        {
            // 1. HANDSHAKE (Sempre in modalità sicura con risposta)
            Log("Attivazione display...");
            if (!await WriteAndWait(_writeChar, HANDSHAKE_FIRST, ACK_STAGE_ONE, true)) { Log("Handshake 1 fallito."); return; }
            await Task.Delay(30); 

            try { await WriteAndWait(_writeChar, HANDSHAKE_SECOND, ACK_STAGE_TWO, true, 800); } catch {}
            await Task.Delay(50);

            // 2. COSTRUZIONE FRAME (Protocollo Header + PNG + CRC)
            ushort dataLen = (ushort)pngBytes.Length;
            ushort totalLen = (ushort)(dataLen + 15);
            uint crc = Crc32.Compute(pngBytes);

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(totalLen);         
                bw.Write((byte)0x02);       
                bw.Write((ushort)0x0000);   
                bw.Write(dataLen);          
                bw.Write((ushort)0x0000);   
                bw.Write(crc);              
                bw.Write((byte)0x00);       
                bw.Write((byte)0x65);       
                bw.Write(pngBytes);

                byte[] fullFrame = ms.ToArray();
                string modeStr = useTurbo ? "TURBO 🚀" : "PRUDENTE 🛡️";
                Log($"Invio PNG ({fullFrame.Length} bytes) in modalità {modeStr}...");

                // 3. INVIO DATI (Scelta strategia)
                if (useTurbo)
                {
                    await SendDataTurbo(fullFrame);
                }
                else
                {
                    await SendDataPrudent(fullFrame);
                }

                // 4. ATTESA CONFERMA FINALE
                Log("Attesa conferma...");
                if (await WaitForAck(ACK_STAGE_THREE, 6000))
                    Log("✅ SUCCESSO! Immagine mostrata.");
                else
                    Log("⚠️ Nessuna conferma (ma potrebbe aver funzionato).");
            }
        }
        catch (Exception ex) { Log($"Errore invio: {ex.Message}"); }
    }

    // --- MODALITÀ TURBO ---
    // Prova a inviare pacchetti grossi (fino a 240 byte) e fa poche pause.
    private async Task SendDataTurbo(byte[] data)
    {
        int chunkSize = 240; // BLE 4.2+ supporta pacchetti grandi
        int offset = 0;
        int packetCount = 0;

        while (offset < data.Length)
        {
            int size = Math.Min(chunkSize, data.Length - offset);
            byte[] chunk = new byte[size];
            Array.Copy(data, offset, chunk, 0, size);

            var writer = new DataWriter();
            writer.WriteBytes(chunk);
            
            try
            {
                // WriteWithoutResponse è essenziale per la velocità
                await _writeChar!.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
            }
            catch
            {
                // Se il PC/Dongle non supporta MTU grandi, scala automaticamente
                if (chunkSize > 20)
                {
                    Log("⚠️ Turbo troppo aggressivo. Scalo a 20 byte.");
                    chunkSize = 20;
                    continue; // Riprova lo stesso pacchetto
                }
                throw;
            }
            
            offset += size;
            packetCount++;

            // Pausa solo ogni 10 pacchetti per far respirare il buffer
            if (packetCount % 10 == 0)
            {
                await Task.Delay(10); 
            }
        }
    }

    // --- MODALITÀ PRUDENTE ---
    // Pacchetti piccoli standard (20 byte) e pausa dopo OGNI pacchetto. Lento ma sicuro.
    private async Task SendDataPrudent(byte[] data)
    {
        const int CHUNK_SIZE = 20; 
        int offset = 0;

        while (offset < data.Length)
        {
            int size = Math.Min(CHUNK_SIZE, data.Length - offset);
            byte[] chunk = new byte[size];
            Array.Copy(data, offset, chunk, 0, size);

            var writer = new DataWriter();
            writer.WriteBytes(chunk);
            
            await _writeChar!.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
            
            offset += size;
            
            // Pausa obbligatoria ad ogni passo
            await Task.Delay(20); 
        }
    }

    private async Task<bool> WriteAndWait(GattCharacteristic charToWrite, byte[] data, byte[] expectedAck, bool useResponse = false, int timeoutMs = 4000)
    {
        _expectedAck = expectedAck;
        _ackWaiter = new TaskCompletionSource<bool>();
        var writer = new DataWriter();
        writer.WriteBytes(data);
        var option = useResponse ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
        await charToWrite.WriteValueAsync(writer.DetachBuffer(), option);
        var completed = await Task.WhenAny(_ackWaiter.Task, Task.Delay(timeoutMs));
        _ackWaiter = null; _expectedAck = null;
        return completed is Task<bool> t && t.Result;
    }

    private async Task<bool> WaitForAck(byte[] ack, int timeoutMs)
    {
        _expectedAck = ack;
        _ackWaiter = new TaskCompletionSource<bool>();
        var completed = await Task.WhenAny(_ackWaiter.Task, Task.Delay(timeoutMs));
        _ackWaiter = null; _expectedAck = null;
        return completed is Task<bool> t && t.Result;
    }

    public void Disconnect()
    {
        try { _watcher.Stop(); if (_device != null) _device.Dispose(); _device = null; _writeChar = null; } catch {}
    }
}