"""Generate the original, softly pitched node-to-core UI cue (no external audio)."""
from pathlib import Path
import math
import random
import struct
import wave

sample_rate = 48000
duration = 0.8
random_source = random.Random(15)
samples = []
phase = 0.0
filtered_noise = 0.0
for index in range(round(sample_rate * duration)):
    t = index / sample_rate
    progress = t / duration
    # A rounded downward sweep suggests energy converging into the core.
    frequency = 190 + 630 * (1 - progress) ** 2
    phase += 2 * math.pi * frequency / sample_rate
    envelope = math.sin(math.pi * progress) ** 1.5
    filtered_noise = 0.96 * filtered_noise + 0.04 * random_source.uniform(-1, 1)
    sample = envelope * (0.25 * math.sin(phase)
                         + 0.06 * math.sin(phase * 2)
                         + 0.20 * filtered_noise)
    # A quiet two-tone arrival pulse closes the sweep without a sharp click.
    if t >= 0.61:
        arrival = t - 0.61
        pulse = math.sin(math.pi * arrival / 0.19) ** 2
        sample += 0.10 * pulse * (math.sin(2 * math.pi * 620 * arrival)
                                + 0.4 * math.sin(2 * math.pi * 930 * arrival))
    samples.append(sample)

output = Path(__file__).resolve().parents[1] / "assets/audio/ui/node_extraction.wav"
with wave.open(str(output), "wb") as stream:
    stream.setnchannels(1)
    stream.setsampwidth(2)
    stream.setframerate(sample_rate)
    stream.writeframes(b"".join(struct.pack("<h", round(x * 32767)) for x in samples))
print(f"Created {output.name}: {duration:.2f}s, peak {max(abs(x) for x in samples):.3f}")
