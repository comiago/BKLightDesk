# Technical Specification: 32x32 LED Matrix (BLE Protocol)

## 1. Project Origins & Credits

This protocol specification is the result of a multi-step reverse engineering process and community-driven research:

- **Base Implementation:** Core logic for bypassing proprietary app restrictions was derived from the [Bk-Light-AppBypass](https://github.com/Pupariaa/Bk-Light-AppBypass) repository by **Pupariaa**. This repository provided the foundation for interacting with the hardware via Python.
- **Reverse Engineering & Sniffing:** Specific command structures, frame headers, and real-time behavior were identified through **packet sniffing** of the communication between the official **iPixel Color** mobile application and the LED hardware. This process allowed the mapping of undocumented GATT characteristics and raw byte commands.

------

## 2. Hardware Interface

- **Display:** 32 x 32 RGB LED Grid (1,024 pixels).
- **Control Protocol:** Bluetooth Low Energy (BLE) 4.0+.
- **Addressing:** Individual pixel control is handled via a compressed data stream rather than direct memory mapping from the client side.

------

## 3. BLE Communication Stack

The device exposes a specific GATT (Generic Attribute Profile) structure. To control the matrix, the central device must interact with the following UUIDs:

- **Primary Service:** `0000FA00-0000-1000-8000-00805F9B34FB`
- **Command/Data Characteristic:** `0000FA02-0000-1000-8000-00805F9B34FB`
  - **Properties:** Write Without Response.
  - **Usage:** This single characteristic handles all logic, including image streaming, brightness adjustments, and power state toggles.

------

## 4. Protocol & Data Encapsulation

Through sniffing the **iPixel Color** app, the following communication protocol was reconstructed:

### 4.1 Packet Structure

Every command sent to the matrix follows a specific binary format:

1. **Magic Header:** A specific byte sequence identified through sniffing that validates the packet.
2. **Command Identifier:**
   - `0x01`: Static Image / Frame Update.
   - `0x02`: Global Brightness Control.
   - `0x03`: Power State (On/Off).
3. **Payload Length:** Defined in 2 bytes, indicating the size of the following data stream.
4. **Data Stream:** PNG-encoded binary data for images or single-byte values for settings.
5. **Footer/Checksum:** Integrity check bytes to prevent rendering of corrupted frames.

### 4.2 Streaming Methodology

To maintain stability, the client must implement a **Chunking Algorithm**:

- Since the compressed PNG often exceeds the negotiated **MTU (Maximum Transmission Unit)**, the packet is split into fragments (typically 20-200 bytes).
- The fragments are written sequentially. The hardware buffer reassembles these fragments and triggers the display refresh only upon receiving the final byte of the declared payload length.

------

## 5. Reverse Engineering Findings (iPixel Color Sniffing)

The sniffing process revealed several hardware behaviors not documented in the original Python repo:

- **Keep-Alive:** The hardware requires a periodic heartbeat or a specific connection sequence to prevent the BLE link from dropping during idle periods.
- **Brightness Scaling:** Brightness is not linear; the iPixel Color app uses a specific curve to map user percentages to the hardware's 8-bit PWM values.
- **Rendering Latency:** The on-board PNG decoder introduces a small delay (~15-30ms). Fast-paced animations must be timed to allow the hardware to finish decoding the previous frame before a new one is sent.

------

## 6. Implementation Workflow

1. **Connect:** Locate the device via the `FA00` service.
2. **MTU Exchange:** Request the highest possible MTU from the hardware to minimize the number of write operations.
3. **Prepare Frame:** Encode the 32x32 visual into a PNG buffer.
4. **Encapsulate:** Wrap the buffer with the headers discovered via sniffing.
5. **Transmit:** Fragment and stream the data to characteristic `FA02`.
