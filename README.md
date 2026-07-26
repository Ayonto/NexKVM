<h1 align="center">NexKVM</h1>

<p align="center">
  <b>Share one mouse &amp; keyboard across two Windows PCs over your local network.</b><br>
  No hardware KVM, no cables — just slide your pointer off the screen edge onto the other machine.
</p>

---

NexKVM turns a second Windows machine into a virtual extra monitor for your mouse and keyboard.
Push the pointer past one screen edge and it appears on the other computer; your keyboard follows
it, and the clipboard (text) stays in sync. It's built for **speed** — native input hooks, direct
`SendInput` injection, and TCP with Nagle disabled — so on a normal LAN the added lag is about a
millisecond and it feels like one continuous desktop.

<p align="center"><i>Think Synergy / Mouse Without Borders — but a single, tiny, no-install app.</i></p>

## Features

- 🖱️ **Seamless edge crossing** — the cursor slides in from the far edge of the other screen; pick
  which side (left/right) the other machine is on.
- ⌨️ **Full keyboard follow** — every key goes to whichever screen the cursor is on (forwarded by
  scan code, so layouts and modifiers work correctly).
- 📋 **Clipboard sync** — copy text on one machine, paste on the other.
- ⚡ **Low latency** — designed so movement feels natural with no perceptible lag on a LAN.
- 🖥️ **Simple GUI** — pick a role, press Start. No command line.
- 🛟 **Failsafe** — `Ctrl+Alt+F12` instantly returns control to the host, and control auto-returns
  if the network drops, so you can never get stuck.
- 📦 **No install** — one self-contained `.exe`. Download, run, done. (.NET is bundled inside.)

## Roles

Both machines run the **same** `NexKVM.exe`. You just choose a role on each:

| Role | Runs on | What it does |
|------|---------|--------------|
| **Host** | the PC whose mouse/keyboard you want to share | captures input and sends it |
| **Receiver** | the other PC/laptop being controlled | receives input and applies it |

Any PC can be the host and any PC can be the receiver — nothing is tied to a specific machine.

## Quick start

1. **Download** `NexKVM.exe` from the [Releases](../../releases) page and copy it to **both**
   computers (put it anywhere, e.g. the Desktop).

2. **On the machine being controlled** (the receiver — e.g. your laptop):
   - Run `NexKVM.exe`.
   - Choose **“This PC is controlled (Receiver)”**.
   - Note the **IP address** it shows (e.g. `192.168.1.50`) — you'll need it on the host. Click
     **Copy** if handy.
   - Press **Start**. It now says *“Listening for host…”*.
   - If Windows shows a firewall prompt, allow it on **Private** networks.

3. **On the machine with the mouse/keyboard** (the host — e.g. your desktop):
   - Run `NexKVM.exe`.
   - Choose **“This PC controls (Host)”**.
   - Enter the receiver's **IP address**.
   - Set **“Receiver is on my … side”** to match where that machine physically sits (Left or Right).
   - Press **Start**. It should say *“Connected”*.

4. **Use it:** push your mouse off the chosen edge of the host screen → it appears on the receiver.
   Type anywhere; keys go where the cursor is. Push back off the opposite edge to return.

> **Failsafe:** if the pointer ever gets stuck on the other machine, press **`Ctrl+Alt+F12`** on the
> host to snap control back.

## Do I need Administrator?

Usually no. You only need to run **both** apps *as Administrator* if you want to control
**elevated windows** on the receiver (UAC dialogs, Task Manager, etc.). For normal desktop use,
just double-click it.

## Networking notes

- Both machines must be on the **same local network** (same Wi-Fi/router, or wired).
- Default port is **24800** (change it in the UI on both sides if needed — they must match).
- First run on the receiver may trigger a Windows Firewall prompt — **allow Private networks**.
  To add the rule manually (elevated PowerShell):
  ```powershell
  New-NetFirewallRule -DisplayName "NexKVM" -Direction Inbound -Protocol TCP -LocalPort 24800 -Action Allow -Profile Private
  ```
- Traffic is **unencrypted** — intended for a trusted home/office LAN. Don't forward the port to the
  internet.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
# Run/develop
dotnet run

# Produce the release single-file exe (lands in ./publish/NexKVM.exe)
powershell -ExecutionPolicy Bypass -File build-release.ps1
```

The build script publishes a compressed, self-contained, single-file `win-x64` executable and zips
it for release.

## How it works

| Concern | Approach |
|---|---|
| Capture (host) | `WH_MOUSE_LL` + `WH_KEYBOARD_LL` low-level hooks on a dedicated high-priority thread |
| Edge crossing | While away, the host re-parks the cursor at screen centre each move and drives a virtual cursor inside the receiver's resolution; a warp-race guard prevents mid-screen jumps |
| No drift | The host sends **absolute normalised** positions (0–65535), not raw deltas |
| Hidden cursor | The host swaps all system cursors for a blank one while controlling the receiver, so no stray pointer is left on the host screen |
| Inject (receiver) | `SendInput` with scan codes and absolute coordinates |
| Transport | One TCP socket, `NoDelay = true`, length-prefixed binary frames |
| Safety | Held keys/buttons are released on hand-back; `Ctrl+Alt+F12` and disconnect auto-revert |

## Limitations

- **Windows ↔ Windows** only for now (macOS/Linux receivers would need a different injection backend).
- Clipboard sync is **text only** (no images or files).
- The host uses its **primary monitor's** edge as the crossing point.
- Not encrypted — use on a trusted LAN.

## License

[MIT](LICENSE)
