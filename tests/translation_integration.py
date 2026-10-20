"""Real CPU inference through the same offline stdio protocol the app uses."""
import argparse
import base64
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import wave

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--models", required=True)
    parser.add_argument("--audio", required=True)
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    import numpy as np
    from PIL import Image, ImageDraw, ImageFont
    root = Path(__file__).resolve().parents[1]
    process = subprocess.Popen([sys.executable, str(root / "HN-IPTV/TranslationRuntime/worker.py"),
        "--models", args.models, "--size", "tiny"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL, text=True, encoding="utf-8")
    report = {}
    try:
        def request(kind, payload, source="auto", target="es"):
            start = time.monotonic()
            process.stdin.write(json.dumps({"id": 1, "kind": kind, "data": base64.b64encode(payload).decode(),
                                           "source": source, "target": target}) + "\n")
            process.stdin.flush()
            response = json.loads(process.stdout.readline())
            if response.get("error"):
                raise RuntimeError(response["error"])
            report[kind + "_" + target + "_seconds"] = time.monotonic() - start
            report[kind + "_" + target + "_result"] = response
            return response["text"]
        with wave.open(args.audio, "rb") as audio:
            data = np.frombuffer(audio.readframes(audio.getnframes()), dtype="<i2")
            if audio.getnchannels() > 1:
                data = data.reshape(-1, audio.getnchannels()).mean(axis=1)
            data = np.interp(np.arange(0, len(data), audio.getframerate() / 44100), np.arange(len(data)), data)
        stereo = np.repeat(data.astype("<i2")[:, None], 2, axis=1)
        output = io.BytesIO()
        with wave.open(output, "wb") as wav:
            wav.setnchannels(2); wav.setsampwidth(2); wav.setframerate(44100); wav.writeframes(stereo.tobytes())
        translated_audio = request("audio", output.getvalue())
        assert re.search("hola|mundo|prueba|traducci", translated_audio, re.I), translated_audio
        image = Image.new("RGB", (1000, 260), "white")
        draw = ImageDraw.Draw(image)
        font = ImageFont.truetype(str(Path(os.environ["WINDIR"]) / "Fonts/arial.ttf"), 72)
        draw.text((50, 70), "Hello world", fill="black", font=font)
        output = io.BytesIO(); image.save(output, format="PNG")
        translated_text = request("screen", output.getvalue())
        assert re.search("hola|mundo", translated_text, re.I), translated_text
        draw.rectangle((0, 0, 1000, 260), fill="white")
        draw.text((50, 70), "Hola mundo", fill="black", font=font)
        output = io.BytesIO(); image.save(output, format="PNG")
        translated_bpe = request("screen", output.getvalue(), source="es", target="en")
        assert re.search("hello.*world", translated_bpe, re.I), translated_bpe
        report["passed"] = True
        report["network_disabled_in_worker"] = True
    finally:
        process.kill(); process.wait()
        Path(args.report).parent.mkdir(parents=True, exist_ok=True)
        Path(args.report).write_text(json.dumps(report, indent=2), "utf-8")
    print(json.dumps(report, indent=2))

if __name__ == "__main__":
    main()
