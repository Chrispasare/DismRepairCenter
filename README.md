# DISM Repair Center

Interfață grafică pentru DISM și SFC pe Windows 10 / 11 (C# / .NET 8 WPF). Program realizat de Pasare Cristian.

## Descărcare

Din pagina [Releases](https://github.com/Chrispasare/DismRepairCenter/releases/latest):

- `DismRepairCenter-portable.exe` — portabil: un singur fișier, fără instalare și fără .NET.
- `DismRepairCenter-net8.exe` — varianta mică, necesită .NET 8 Desktop Runtime.

## Compilare

1. Ai nevoie de .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`).
2. Rulează `build.cmd` — rezultă `publish\DismRepairCenter.exe`.
   Sau: `dotnet run -c Release` din acest folder.

Aplicația cere drepturi de administrator la pornire (UAC), fiindcă DISM și SFC nu rulează altfel.

## Funcții

| Buton | Comandă |
|---|---|
| Reparare completă | ScanHealth → RestoreHealth → `sfc /scannow`, pe rând |
| Verificare rapidă | `DISM /Online /Cleanup-Image /CheckHealth` |
| Scanare | `DISM /Online /Cleanup-Image /ScanHealth` |
| Reparare | `DISM /Online /Cleanup-Image /RestoreHealth` (+ `/Source`, `/LimitAccess`) |
| Analiză WinSxS | `DISM /Online /Cleanup-Image /AnalyzeComponentStore` |
| Curățare WinSxS | `DISM /Online /Cleanup-Image /StartComponentCleanup` (+ `/ResetBase`) |
| SFC reparare | `sfc /scannow` |
| SFC verificare | `sfc /verifyonly` |
| SFC fișier | `sfc /scanfile=<fișier>` |
| Raport SFC | liniile `[SR]` din CBS.log, salvate pe Desktop |
| Instalare pachet | `DISM /Online /Add-Package /PackagePath:<.cab/.msu> /NoRestart` |
| Jurnal DISM / CBS | deschide `dism.log` / `CBS.log` |

Interfață în română și engleză (butonul RO/EN din dreapta sus).

---

# DISM Repair Center (English)

A GUI for DISM and SFC on Windows 10 / 11 (C# / .NET 8 WPF).

Download: get `DismRepairCenter-portable.exe` (single file, no install, no .NET needed) from the [Releases](https://github.com/Chrispasare/DismRepairCenter/releases/latest) page.

Build: install the .NET 8 SDK, then run `build.cmd` (output: `publish\DismRepairCenter.exe`) or `dotnet run -c Release`.
The app requests administrator rights at startup. Use the RO/EN button in the top-right corner to switch language.
