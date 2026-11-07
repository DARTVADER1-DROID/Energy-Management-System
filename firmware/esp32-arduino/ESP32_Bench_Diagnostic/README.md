# ESP32 USB bench diagnostic

This sketch is for the first bench test when the exact ESP32-WROOM board revision is not yet confirmed and the PZEM-004T is not available.

## Purpose

It reports the chip model/revision, core count, CPU frequency, flash size/speed, sketch/free application space, heap, PSRAM, and reset reason. It then prints a short uptime/heap heartbeat every ten seconds.

It is deliberately independent of the EMS application firmware and requires only the Espressif ESP32 Arduino core. It does not connect to Wi-Fi, access the network, use credentials, talk to the backend, access a PZEM, or drive GPIO outputs.

## Arduino IDE

1. Install the Espressif ESP32 board package.
2. Select **ESP32 Dev Module** as a provisional target for a typical ESP32-WROOM-32.
3. Open `ESP32_Bench_Diagnostic.ino`.
4. Connect the board using a USB data cable.
5. Select the serial port and upload. If automatic download mode fails, use the board's documented BOOT/RESET sequence.
6. Open Serial Monitor at **115200 baud**.

## Expected result

The monitor should show the chip identity and memory diagnostics, followed by an `[alive]` line every ten seconds. Save the output for board identification and initial baseline comparison.

This confirms that the chip boots and the serial/upload path works. It does **not** prove Wi-Fi performance, PZEM UART compatibility, sensor accuracy, backend connectivity, electrical safety, or production readiness.

If the reported chip is not an original ESP32 family target (for example, ESP32-S2, S3, C3, or C6), stop before using the main firmware and review board-specific pin and core compatibility. Do not assume GPIO16/17 are valid for every ESP32 family.

## Safety

Use USB/approved low-voltage power only. Do not connect the PZEM, mains, relays, or external loads during this diagnostic.
