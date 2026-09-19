# Patient Consent Hub

A Windows desktop application for recording patient consent, built for A&T.

Open → verify camera and microphone → enter Patient ID → choose recording type → Start → Stop → saved automatically.

---

## Building

### One-time setup

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download).
2. Install [Inno Setup 6](https://jrsoftware.org/isdl.php) (only needed for the installer).
3. Download an FFmpeg Windows x64 build and copy `ffmpeg.exe` into
   `src\PatientConsentHub\Tools\ffmpeg\`.
   See `PLACE_FFMPEG_HERE.txt` in that folder for which build to use and the licensing note.

### Build

```powershell
.\build.ps1              # publish to .\publish
.\build.ps1 -Installer   # also produce installer\Output\PatientConsentHub_Setup.exe
```

The publish output is self-contained: the .NET runtime, OpenCV native libraries and
`ffmpeg.exe` all travel with the app. Hospital machines need no Python, no Node.js,
no separate FFmpeg install, and no .NET runtime.

### First sign-in

The credential store is created on first run with **admin / admin**.
Change it before any real use — see "Managing users" below.

---

## Why this technology

| Need | Choice | Reasoning |
|---|---|---|
| UI | .NET 8 + WPF | Mature, high-DPI aware, straightforward single-folder publish, easy installer packaging |
| Capture + encoding | Bundled FFmpeg (DirectShow + gdigrab) | Battle-tested for long-duration capture; handles device quirks, encoding and containers rather than reimplementing them |
| Preview | OpenCvSharp (DirectShow) | Lightweight live preview without pulling in a full media framework |
| Mic level | NAudio | Simple, reliable WaveIn metering |
| Installer | Inno Setup | Standard Add/Remove Programs behaviour, shortcuts, admin install |

FFmpeg runs as a separate process, which also means a capture problem cannot take the UI down with it.

---

## Architecture

```
src/PatientConsentHub/
├── App.xaml(.cs)                  startup, global error handling, service wiring
├── Models/Models.cs               settings, devices, sessions, results
├── Theme/                         palette, vector A&T logo, control styles
├── Views/
│   ├── LoginWindow                username + password
│   ├── MainWindow                 preview, devices, Patient ID, start/stop
│   ├── SettingsWindow             storage location, default devices
│   └── HistoryWindow              recent sessions
└── Services/
    ├── Auth/                      IAuthenticationService + local PBKDF2 store
    ├── Licensing/                 ILicenseService + testing-mode implementation
    ├── Devices/                   FFmpeg locator, camera/mic enumeration, displays
    ├── Preview/                   live camera preview
    ├── Audio/                     microphone level meter
    ├── Recording/                 IRecordingEngine + FFmpeg engine
    ├── Storage/                   patient folders, sanitising, disk checks, verification
    ├── History/                   session index
    ├── Recovery/                  unfinished-recording detection and repair
    └── Logging/                   rolling file log
```

Each module is replaceable on its own. The two most likely replacements are already behind interfaces:

- `IAuthenticationService` → hospital SSO or Active Directory.
- `ILicenseService` → key-based activation (see below).

---

## How recording works

A single FFmpeg process handles a session, which is what keeps camera, microphone and screen aligned:

**Consent View**
```
dshow (camera + microphone) ──► Consent_<timestamp>.mp4
```

**Consent View + Screen**
```
dshow (camera + microphone) ──┬─► Consent_<timestamp>.mp4        (camera + audio)
gdigrab (selected display) ───┴─► ConsentScreen_<timestamp>.mp4  (screen + same audio)
```

Both files carry the same audio track, so they can be reviewed side by side without re-syncing.

**Encoding** is chosen by the app, never by the doctor: H.264 (`libx264`, `veryfast`, CRF 23),
AAC 128 kbps mono at 48 kHz, 1080p if the camera genuinely reports it, otherwise 720p or the
best mode it does report. The camera is queried with `-list_options` before every session rather
than assuming a resolution will work.

### Crash safety

Capture writes `.partial` files using fragmented MP4 (`+frag_keyframe+empty_moov`). A fragmented
file stays playable even if the process is killed mid-session — there is no dependency on a final
index being written.

On a normal stop, `q` is sent to FFmpeg's stdin (a graceful finish, not a kill), then each partial
is stream-copied into its final name with `+faststart`. The result is verified to exist and be
non-empty **before** "Recording saved successfully" appears. If verification fails, the partial is
deliberately left in the patient folder rather than deleted.

On an abnormal exit, a marker file in `%LOCALAPPDATA%\A&T\Patient Consent Hub\` points at the
partials. The next launch offers to recover them. Nothing is deleted unless the user chooses to.

### Device handling

The camera preview and the level meter both release their devices the instant recording starts —
DirectShow will not open a device twice. They resume automatically when recording stops.

Devices are enumerated through FFmpeg itself, so the names shown in the dropdowns are exactly the
names FFmpeg later needs. That removes name-matching guesswork at the moment it would hurt most.

---

## Data layout

```
C:\Patient Consent Hub\Recordings\
├── PATIENT001\
│   ├── Consent_2026-09-19_10-30-15.mp4
│   └── Consent_2026-09-20_14-15-22.mp4
└── PATIENT002\
    ├── Consent_2026-09-19_11-20-05.mp4
    └── ConsentScreen_2026-09-19_11-20-05.mp4
```

The same Patient ID always resolves to the same folder. Timestamps make every filename unique, and
`EnsureUniquePath` adds a counter in the unlikely event of a collision, so **an existing recording
is never overwritten**.

Patient IDs are sanitised for Windows: `PATIENT/001` becomes `PATIENT_001`, reserved names such as
`CON` are prefixed, and the field is capped at 64 characters. The app shows the doctor which folder
name will be used whenever sanitising changes what they typed.

Application data lives in `%LOCALAPPDATA%\A&T\Patient Consent Hub\`:
`settings.json`, `users.json`, `history.json`, `unfinished-recording.json`, `Logs\`.

---

## Privacy and security

- Nothing is uploaded. There is no network code in the application at all.
- Passwords are stored as PBKDF2-SHA256 hashes (120,000 iterations, per-user random salt).
- Logs record technical events only. File paths are redacted before being written, so **Patient IDs
  never reach the log** — a log file can be sent to support without sending patient data.
- Recordings and logs live in separate locations.
- The uninstaller removes logs; it never touches recordings or settings.

---

## Managing users

`users.json` holds PBKDF2 hashes. `LocalAuthenticationService.ChangePassword` is implemented and
ready to wire to a UI. The default `admin / admin` account exists purely so testing can begin —
replace it before deployment.

---

## Adding licensing later

`ILicenseService` already covers everything the brief anticipates: product key, device-bound
activation (`GetDeviceId` returns a machine fingerprint today), expiry, hospital name, and an
allowed-device count. `LoginWindow` already checks `AllowsRecording` before signing anyone in.

Enabling it means: implement `KeyBasedLicenseService`, change one line in `App.xaml.cs`, and add an
activation screen. No other file needs to change.

---

## What to test before release

| Area | Cases |
|---|---|
| Camera | Integrated, USB webcam, professional USB camera, unplugged mid-recording |
| Microphone | Laptop, USB, headset, unplugged mid-recording |
| Screen | Single monitor, dual monitors, monitor disconnected, mixed resolutions |
| Duration | 30 s, 5 min, 30 min, and one long session (2 h+) |
| Storage | Local drive, network share, drive removed mid-recording, near-full disk |
| Data | A/V sync, playback in VLC and Windows Media Player, correct folder, no overwrite |
| Recovery | End the process in Task Manager mid-recording, then relaunch |

The interrupted-device cases matter most: unplug the camera during a recording and confirm the
partial session is still saved and the message is readable.

---

## Known constraints

- **Preview index mapping.** The preview opens cameras by DirectShow enumeration order, which
  matches FFmpeg's order on every machine tested but is not guaranteed by Windows. If a preview
  ever shows the wrong camera, map by moniker (`AlternativeName`, already captured on
  `CaptureDevice`) instead of index.
- **Screen capture uses GDI.** Reliable and universally compatible; on very high-resolution displays
  it costs more CPU than a DXGI-based capture. If that becomes a problem, `ScreenFrameRate` in
  settings is the first dial to turn.
- **Software H.264 encoding.** Predictable across hardware. On a slow machine recording 1080p plus a
  4K screen, switch the encoder to `h264_qsv` or `h264_nvenc` in `FfmpegRecordingEngine.BuildArguments`.
