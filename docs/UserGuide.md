# Patient Consent Hub — User Guide

A&T · Version 1.0

---

## Recording a consent

1. **Open** Patient Consent Hub from the desktop or Start menu.
2. **Sign in** with your username and password.
3. **Check the picture.** The camera preview appears straight away. If it looks wrong, choose a
   different camera from the **Camera** list.
4. **Check the sound.** Say a few words and watch the **Microphone Level** bar move. If it does not
   move, choose a different microphone from the **Microphone** list.
5. **Type the Patient ID.**
6. **Choose the recording type:**
   - **Consent View** — camera and microphone only.
   - **Consent View + Screen** — camera, microphone and the computer screen.
7. **Press START RECORDING** (or press **F9**).
8. The screen turns into a recording view showing the Patient ID and how long you have been
   recording. Conduct the consent discussion as usual.
9. **Press STOP RECORDING** (or press **F10**) when you are finished.
10. A green message confirms **Recording saved successfully**. You can start the next patient
    immediately.

The app chooses the video and sound quality by itself. There is nothing to configure.

---

## Where recordings are saved

Every patient gets their own folder, named after the Patient ID:

```
C:\Patient Consent Hub\Recordings\PATIENT001\Consent_2026-09-19_10-30-15.mp4
```

Each new recording for the same patient is added to that same folder. **Recordings are never
overwritten.** File names are created automatically from the date and time.

If the Patient ID contains a character Windows does not allow in a folder name (such as `/`), the
app replaces it and tells you which folder name it will use. You do not need to do anything.

Press **Recordings** to see recent sessions, open a patient folder, or play a recording back.

---

## Messages you might see

**Please enter Patient ID before starting the recording.**
The Patient ID is required. Type it, then press Start.

**Camera unavailable. Please check the camera connection.**
The camera is unplugged, switched off, or in use by another program (Teams and Zoom are common
culprits). Reconnect it or close the other program, then choose the camera from the list again.

**The camera or microphone is being used by another application.**
Close the other program — usually a video-call app — and press Start again.

**Low storage space**
The drive is filling up. You can continue, but tell your IT team. Long recordings need room.

**Recording could not be saved.**
The recording data has been kept in the patient folder. Do not re-record the consent until IT has
looked at it — the session is very likely recoverable.

**An unfinished recording was detected.**
The computer or the app stopped during a recording. Choose **Yes** to recover it. If you choose No,
nothing is deleted and you can recover it later.

---

## Keyboard shortcuts

| Key | Action |
|---|---|
| **F9** | Start recording |
| **F10** | Stop recording |

The large on-screen buttons always work too.

---

## Settings

Open **Settings** to change:

- **Recording Location** — where recordings are saved, including a network folder.
- **Default Camera**, **Default Microphone**, **Default Screen** — what is selected when the app opens.
- **Confirmation before stopping** — switch off if the extra click gets in the way.
- **Low storage warning** — how much free space triggers a warning.

These are usually set once by your IT team.

---

## Good practice

- Check the preview and the microphone bar before every recording. It takes two seconds and it is
  the only way to know the equipment is working.
- Keep recording until the consent discussion is fully finished.
- If a message appears that you do not understand, note the time and tell your IT team — the app
  keeps a technical log they can read.

---

## For IT support

- Logs: `%LOCALAPPDATA%\A&T\Patient Consent Hub\Logs` (also reachable from Settings → Open Log Folder).
  They contain no patient identifiers, so they are safe to forward to A&T.
- Settings, user accounts and history: `%LOCALAPPDATA%\A&T\Patient Consent Hub\`
- A file ending in `.partial` inside a patient folder is a recording that was interrupted. It is
  usually playable as-is and can be repaired with:
  `ffmpeg -i "Consent_....mp4.partial" -c copy "Consent_recovered.mp4"`
- Camera or microphone missing from the lists? Check Windows Settings → Privacy & security →
  Camera / Microphone, and confirm desktop apps are allowed.
