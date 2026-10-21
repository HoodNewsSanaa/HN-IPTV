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
        "--models", args.models, "--size", "tiny", "--device", "auto"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
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
        request("prepare_screen", b"", source="en")
        request("prepare_audio", b"", source="en")
        with wave.open(args.audio, "rb") as audio:
            data = np.frombuffer(audio.readframes(audio.getnframes()), dtype="<i2")
            if audio.getnchannels() > 1:
                data = data.reshape(-1, audio.getnchannels()).mean(axis=1)
            data = np.interp(np.arange(0, len(data), audio.getframerate() / 44100), np.arange(len(data)), data)
        stereo = np.repeat(data.astype("<i2")[:, None], 2, axis=1)
        output = io.BytesIO()
        with wave.open(output, "wb") as wav:
            wav.setnchannels(2); wav.setsampwidth(2); wav.setframerate(44100); wav.writeframes(stereo.tobytes())
        output_audio = output.getvalue()
        translated_audio = request("audio", output_audio)
        assert re.search("hola|mundo|prueba|traducci", translated_audio, re.I), translated_audio
        image = Image.new("RGB", (1000, 260), "white")
        draw = ImageDraw.Draw(image)
        font = ImageFont.truetype(str(Path(os.environ["WINDIR"]) / "Fonts/arial.ttf"), 72)
        draw.text((50, 70), "Hello world", fill="black", font=font)
        output = io.BytesIO(); image.save(output, format="PNG")
        translated_text = request("screen", output.getvalue())
        assert re.search("hola|mundo", translated_text, re.I), translated_text
        positioned = report["screen_es_result"]
        assert positioned["frame_width"] == 1000 and positioned["frame_height"] == 260
        assert positioned["regions"], "OCR geometry was discarded"
        for region in positioned["regions"]:
            assert 0 <= region["x"] < 1 and 0 <= region["y"] < 1
            assert 0 < region["width"] <= 1 and 0 < region["height"] <= 1
            assert region["text"] and re.fullmatch(r"#[0-9A-F]{6}", region["background"])
        report["positioned_regions"] = True
        # Only the moving background changes; the text patches must reuse the recognized result.
        draw.rectangle((950, 0, 999, 40), fill="blue")
        output = io.BytesIO(); image.save(output, format="PNG")
        cached_text = request("screen", output.getvalue())
        assert cached_text == translated_text
        assert report["screen_es_result"].get("cached") is True, "Unchanged text was recognized again"
        report["unchanged_text_cache"] = True
        draw.rectangle((0, 0, 1000, 260), fill="white")
        draw.text((50, 70), "Hola mundo", fill="black", font=font)
        output = io.BytesIO(); image.save(output, format="PNG")
        translated_bpe = request("screen", output.getvalue(), source="es", target="en")
        if not re.search(r"\b(hello|hey|hi)\b.*\bworld\b", translated_bpe, re.I):
            # Fixture-only diagnostics: reveal OCR and tokenization when this regression fails.
            import importlib.util
            spec = importlib.util.spec_from_file_location("worker_fixture", root / "HN-IPTV/TranslationRuntime/worker.py")
            module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
            module.block_network()
            probe = module.Engine(args.models, "tiny", device="cpu")
            tokenizer, _, _ = probe.pair("es", "en")
            print("Spanish fixture tokens:", tokenizer.encode("Hola mundo", out_type=str))
            print("Direct Spanish fixture:", probe.translate("Hola mundo", "es", "en"))
            import ctranslate2
            folder = module.package_folder(Path(args.models) / "translate/es_en")
            for numeric in ("int8", "float32"):
                model = ctranslate2.Translator(str(folder / "model"), device="cpu", compute_type=numeric, inter_threads=1, intra_threads=2)
                for beam in (1, 2, 4):
                    result = model.translate_batch([["Hola", "mundo"]], beam_size=beam, length_penalty=0.2, max_decoding_length=32)[0]
                    print("Diagnostic", numeric, beam, result.hypotheses)
                del model
            raise AssertionError(translated_bpe)
        # The new standalone OPUS pack must load offline, without Transformers installed.
        import importlib.util
        spec = importlib.util.spec_from_file_location("selected_worker", root / "HN-IPTV/TranslationRuntime/worker.py")
        selected = importlib.util.module_from_spec(spec); spec.loader.exec_module(selected)
        selected.block_network()
        engine = selected.Engine(args.models, "tiny", device="cpu")
        tokenizer, _, _ = engine.pair("ar", "en")
        assert isinstance(tokenizer, selected.MarianTokenizer), "Auto did not select the prepared OPUS model"
        opus = engine.translate("\u0645\u0631\u062d\u0628\u0627 \u0628\u0627\u0644\u0639\u0627\u0644\u0645", "ar", "en")
        assert re.search("world", opus, re.I), opus
        report["selected_opus_offline"] = {"text": opus, "passed": True}
        # Exercise separate recognition -> text translation using the real app protocol.
        staged = subprocess.Popen([sys.executable, str(root / "HN-IPTV/TranslationRuntime/worker.py"),
            "--models", args.models, "--size", "tiny", "--device", "cpu", "--pipeline", "staged"],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, encoding="utf-8")
        try:
            staged.stdin.write(json.dumps({"id": 99, "kind": "audio", "data": base64.b64encode(output_audio).decode(),
                "source": "en", "target": "es"})+"\n"); staged.stdin.flush()
            response = json.loads(staged.stdout.readline())
            assert not response.get("error") and re.search("hola|mundo|prueba|traducci", response["text"], re.I), response
            report["staged_speech_pipeline"] = True
        finally: staged.kill(); staged.wait()
        report["passed"] = True
        report["network_disabled_in_worker"] = True
    finally:
        process.kill(); process.wait()
        Path(args.report).parent.mkdir(parents=True, exist_ok=True)
        Path(args.report).write_text(json.dumps(report, indent=2), "utf-8")
    print(json.dumps(report, indent=2))

if __name__ == "__main__":
    main()
