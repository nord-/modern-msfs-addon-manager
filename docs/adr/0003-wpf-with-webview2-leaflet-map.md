# WPF with WPF-UI, and a WebView2 + Leaflet map

The app is a Windows-only C# / .NET 10 desktop app built with WPF and the WPF-UI library for a dark Fluent look. The map is a Leaflet page hosted in WebView2, using map tiles that need no API key.

## Considered Options

- **WinUI 3.** Most modern on Windows, but weaker tooling and a less mature ecosystem.
- **Avalonia.** Cross-platform, which MSFS 2020 doesn't need.
- **Tauri or Electron.** Nice web UIs, but SimConnect from Rust is awkward and Electron is heavy for a tool that runs next to the sim.
- **A native WPF map control.** Rejected: Leaflet handles clustering of about 40,000 airports and dark tiles with far less effort.
