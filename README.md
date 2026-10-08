# BkLightDesk 💡🎨

A modern, fast, and feature-rich Windows desktop application built with **.NET 10** and **WPF** to control a 32x32 RGB LED Matrix via Bluetooth Low Energy (BLE).

BkLightDesk bypasses the limitations of the official mobile app, offering a beautiful "Bento Box" style user interface, optimized PC-to-Matrix streaming, and productivity tools designed for your desk setup.

## ✨ Key Features

- **Bento Box UI:** A clean, modern, and intuitive dashboard to control all aspects of your LED matrix.
- **Smart Clock:** Customizable clock faces and firmware styles to display time dynamically on the matrix.
- **Pomodoro Timer:** Built-in productivity timer with matrix visual feedback and audio alerts.
- **Media & GIF Casting:** Cast images and animated GIFs directly from your PC to the hardware display.
- **Optimized BLE Streaming:** Custom rendering pipelines and a binary streaming protocol prevent BLE bandwidth saturation, ensuring smooth animations and low latency.
- **Centralized Device Management:** Dedicated settings page for easy BLE pairing, connection management, and hardware brightness control.

## 🛠️ Technology Stack

- **Framework:** .NET 10
- **UI:** WPF (Windows Presentation Foundation)
- **Connectivity:** Windows Bluetooth LE APIs
- **Deployment:** Inno Setup for professional Windows installers

## ⚙️ How It Works (Under the Hood)

BkLightDesk communicates directly with the LED Matrix by sending compressed PNG frames over a customized BLE chunking protocol. The device relies on a specific GATT Service (`FA00`) and a Write-Without-Response Characteristic (`FA02`).

For a deep dive into the packet structure, command identifiers, and streaming methodology, please read the full [Technical Specification Document](technical-documentation.md).

## 🚀 Installation

1. Go to the [Releases](../../releases) page of this repository.
2. Download the latest `BkLightDesk_Setup.exe` installer.
3. Run the installer and follow the on-screen instructions.
4. Launch BkLightDesk, turn on your LED matrix, and connect via the Settings page!

*Note: Requires Windows 10/11 with Bluetooth Low Energy support.*

## 👨‍💻 Build from Source

If you want to contribute or build the project yourself:

1. Clone the repository: `git clone https://github.com/comiago/BkLightDesk.git`
2. Open the solution in **Visual Studio 2022** (ensure .NET 10 SDK is installed).
3. Restore NuGet packages.
4. Build and Run the project (F5).

## 🙏 Credits & Acknowledgements

This project was made possible through community research and reverse engineering:

- **Base Protocol Logic:** Massive thanks to **Pupariaa** and their [Bk-Light-AppBypass](https://github.com/Pupariaa/Bk-Light-AppBypass) repository, which laid the foundation for interacting with this specific hardware.
- **Reverse Engineering:** Further command structures, keep-alive behaviors, and frame headers were mapped by packet sniffing the official **iPixel Color** mobile application.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
