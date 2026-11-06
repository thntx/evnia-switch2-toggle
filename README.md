# Evnia Switch 2 Toggle

Use one keyboard shortcut to share a Philips Evnia **34M2C8600** between a Windows PC and a docked Nintendo Switch 2. The PC fills the 34-inch ultrawide panel. The Switch uses the monitor’s centered **27-inch widescreen preset**, so games keep their 16:9 proportions with a black bar on each side. The dock sends video straight to the monitor over HDMI. The PC only sends monitor commands.

## What one press does

| What the monitor is showing | What the shortcut does |
| --- | --- |
| PC on DisplayPort, already full width | Switches to HDMI 2 and applies the remembered 27-inch preset. |
| Switch on HDMI 2, already in the expected mode | Returns to DisplayPort and restores the full-width PC desktop. |
| Either input reports the other input’s size mode | Corrects the size and stays on the current input. Press again to change inputs. |

The last case matters when the Switch goes to sleep. The monitor can automatically return to the PC while leaving the desktop squeezed into 27 inches. One press restores the desktop to 34 inches.

## Set it up

1. Connect the Windows PC to **DisplayPort** and the Switch 2 dock to **HDMI 2**. Keep the PC running and the monitor’s DDC/CI control enabled. The tested PC runs its desktop at the panel’s native **3440 × 1440, 175 Hz**.
2. On the Evnia, use **System → Smart Size → Screen Size → 27″W** once to teach Zoom which size to recall. Check that the docked Switch looks correct at this size. The software recalls that choice on subsequent switches.
3. Clone or download this repository. From its folder, run the following in PowerShell. The build uses the C# compiler supplied with the 64-bit Windows .NET Framework.

   ```powershell
   .\build.ps1
   .\InputToggle.exe
   ```

4. Leave `input-toggle.ini` beside the executable. Press **Ctrl+Alt+Shift+F10** to toggle, or left-click the tray icon. To use **Fn+Enter**, map it to that combination in your keyboard software. The tested setup uses Logitech G HUB. The tray menu is in Catalan; **Arrenca amb Windows** enables launch at sign-in and **Surt** quits.

## Why the HDMI change takes a few seconds

This Evnia can report *Zoom* while the HDMI picture is still stretched. The app waits for the destination to carry a live signal, applies **1:1** and reads it back, waits two seconds, then reapplies **Zoom**. That sequence was tested with a live Switch 2 signal. On the PC, 1:1 at native resolution fills the panel.

The monitor reports its scaling mode, but doesn’t identify the stored diagonal in inches. If someone changes the remembered size using the joystick, select **27″W** again. To explicitly reapply the Switch setup, use the tray’s HDMI 2 destination or run `.\InputToggle.exe b` from the program folder.

## Configuration and diagnostics

The supplied configuration targets the **34M2C8600** (model ID `PHLC29F`). It uses DisplayPort input `15`, HDMI 2 input `18`, and scaling values `1` (PC) and `8` (Switch). Monitor commands go through Windows DDC/CI. Philips Precision Center is optional. Other Evnia models and their size presets have not been tested.

The monitor sometimes reports the last input selected by software after it has automatically changed inputs. On the tested setup, a live refresh rate above **125 Hz** distinguishes the 175 Hz PC from the Switch. If your PC also runs at 60 or 120 Hz, that check cannot distinguish them and automatic-return detection depends on the monitor reporting its input correctly.

Edit `input-toggle.ini` to change the shortcuts or input numbers, then restart the tray app. The default mute shortcut is **Ctrl+Alt+Shift+F11**. For the most recent tray toggle result, check `last-action.txt` beside the executable. If the Switch is asleep, wake it before selecting HDMI.

For use in scripts, `InputToggle.exe a` selects the PC, `b` selects the Switch, and `toggle` uses the same repair-first behavior as the shortcut. The one-shot commands exit after completing the action.
