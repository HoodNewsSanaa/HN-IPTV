"""Local-only translation worker. Only --setup may access the network."""
import argparse
import base64
import contextlib
import gc
import hashlib
import io
import json
import os
from pathlib import Path
import re
import socket
import sys
import time
import zipfile
from collections import OrderedDict

OCR_GROUPS = {
    "latin": ["en", "es", "fr", "de", "pt", "it", "tr", "pl", "nl", "vi", "id", "cs"],
    "ja": ["ja", "en"], "ko": ["ko", "en"],
    "zh": ["ch_sim", "en"], "zt": ["ch_tra", "en"],
    "cyrillic": ["ru", "uk", "en"], "arabic": ["ar", "fa", "ur", "en"],
    "hi": ["hi", "en"], "th": ["th", "en"],
}
def ocr_group(code):
    if code in ("zh", "zt", "ja", "ko", "hi", "th"):
        return code
    if code in ("ru", "uk", "bg"):
        return "cyrillic"
    if code in ("ar", "fa", "ur"):
        return "arabic"
    return "latin"

def normalize(code):
    return {"zh-cn": "zh", "zh-tw": "zt"}.get(code, code.split("-")[0])

def safe_extract(archive, destination):
    root = Path(destination).resolve()
    with zipfile.ZipFile(archive) as z:
        if len(z.infolist()) > 20000 or sum(x.file_size for x in z.infolist()) > 2 * 1024**3:
            raise ValueError("Model archive is too large")
        for item in z.infolist():
            target = (root / item.filename).resolve()
            if root != target and root not in target.parents:
                raise ValueError("Invalid model archive path")
        z.extractall(root)

def block_network():
    os.environ.update(HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1",
                      HF_HUB_DISABLE_TELEMETRY="1", DO_NOT_TRACK="1")
    def denied(*args, **kwargs):
        raise RuntimeError("Network access is disabled during local translation")
    socket.socket.connect = denied
    socket.socket.connect_ex = denied
    socket.create_connection = denied
    socket.getaddrinfo = denied

def download(url, path):
    from urllib.request import urlopen, Request
    if not url.startswith("https://"):
        raise ValueError("Models must use HTTPS")
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".partial")
    try:
        with urlopen(Request(url, headers={"User-Agent": "HN-IPTV-Feature-Rich/0.11"}), timeout=60) as response, temporary.open("wb") as out:
            if response.geturl().split(":")[0] != "https":
                raise ValueError("Insecure model redirect")
            count = 0
            while True:
                data = response.read(1024 * 1024)
                if not data:
                    break
                count += len(data)
                if count > 1024**3:
                    raise ValueError("Model download is too large")
                out.write(data)
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def download_package(package, path):
    from urllib.parse import urlparse
    urls = [x for x in package["links"] if x.startswith("https://")]
    # The publisher's original public model host mirrors the same versioned files.
    for url in list(urls):
        parsed = urlparse(url)
        if parsed.hostname == "argos-net.com":
            urls.append("https://data.argosopentech.com/argospm/v1/" + parsed.path.rsplit("/", 1)[-1])
    errors = []
    for url in dict.fromkeys(urls):
        try:
            download(url, path)
            return
        except Exception as error:
            errors.append(type(error).__name__ + ": " + str(error))
    raise ValueError("Language-pack download failed: " + "; ".join(errors))


def package_folder(directory):
    """Accept both publisher tokenizer formats and only complete model folders."""
    for metadata in sorted(Path(directory).rglob("metadata.json")):
        folder = metadata.parent
        if ((folder / "model" / "model.bin").is_file()
                and any((folder / name).is_file() for name in ("sentencepiece.model", "bpe.model"))):
            return folder
    return None

class BPETokenizer:
    def __init__(self, model_file, source, target):
        from sacremoses import MosesPunctNormalizer, MosesTokenizer, MosesDetokenizer
        from subword_nmt.apply_bpe import BPE
        self.normalizer = MosesPunctNormalizer(lang=source)
        self.tokenizer = MosesTokenizer(lang=source)
        self.detokenizer = MosesDetokenizer(lang=target)
        with Path(model_file).open(encoding="utf-8") as codes:
            self.bpe = BPE(codes)

    def encode(self, text, out_type=str):
        words = self.tokenizer.tokenize(self.normalizer.normalize(text))
        return self.bpe.segment_tokens(words)

    def decode(self, tokens):
        return self.detokenizer.detokenize(" ".join(tokens).replace("@@ ", "").split(" "))


_DLL_HANDLES = []
def select_device(requested):
    if requested == "cpu":
        return "cpu", None
    try:
        # The official Windows CUDA torch wheel includes cuBLAS/cuDNN in torch/lib.
        # Keep DLL directory handles alive; PATH changes apply only to this worker.
        if os.name == "nt":
            library = Path(sys.executable).parent / "Lib/site-packages/torch/lib"
            if library.is_dir():
                _DLL_HANDLES.append(os.add_dll_directory(str(library)))
                os.environ["PATH"] = str(library) + os.pathsep + os.environ.get("PATH", "")
        import torch
        import ctranslate2
        if torch.cuda.is_available() and ctranslate2.get_cuda_device_count() > 0:
            return "cuda", None
        return "cpu", ("NVIDIA CUDA is unavailable; using CPU. Prepare CUDA in Translation settings."
                       if requested == "cuda" else None)
    except (ImportError, RuntimeError, OSError):
        return "cpu", "CUDA could not load; using CPU. Prepare CUDA in Translation settings."

def setup(root, source_codes, target, size, speech_only=False, device="cpu"):
    from faster_whisper import WhisperModel
    from huggingface_hub import snapshot_download
    root.mkdir(parents=True, exist_ok=True)
    os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
    print("Preparing multilingual speech model...", flush=True)
    model_dir = root / "speech" / size
    if not (model_dir / "model.bin").exists():
        snapshot_download("Systran/faster-whisper-" + size, local_dir=str(model_dir),
                          allow_patterns=["*.bin", "*.json", "*.txt"])
    # Verify locally, and exercise CUDA/cuDNN with a short inference when selected.
    selected, notice = select_device(device)
    if notice:
        print(notice, flush=True)
    try:
        model = WhisperModel(str(model_dir), device=selected,
                             compute_type="int8_float16" if selected == "cuda" else "int8",
                             local_files_only=True, cpu_threads=2)
        if selected == "cuda":
            import numpy as np
            segments, _ = model.transcribe(np.zeros(16000, dtype=np.float32),
                                           language="en", beam_size=1, vad_filter=False)
            list(segments)
        print("Speech inference backend: " + ("NVIDIA CUDA" if selected == "cuda" else "CPU"), flush=True)
    except (RuntimeError, OSError):
        if selected != "cuda":
            raise
        print("CUDA validation failed; verifying the CPU fallback instead.", flush=True)
        model = WhisperModel(str(model_dir), device="cpu", compute_type="int8",
                             local_files_only=True, cpu_threads=2)
    del model
    gc.collect()
    sources = sorted(set(source_codes))
    groups = []
    previous = {}
    ready = root / "ready.json"
    if ready.exists():
        previous = json.loads(ready.read_text("utf-8"))
    if not speech_only:
        import easyocr
        groups = sorted(set(previous.get("ocr_groups", []) + [ocr_group(x) for x in sources]))
        for group in groups:
            print("Preparing screen-text model: " + group, flush=True)
            easyocr.Reader(OCR_GROUPS[group], gpu=False, model_storage_directory=str(root / "ocr"),
                           user_network_directory=str(root / "ocr-user"),
                           download_enabled=True, verbose=False)
        from urllib.request import urlopen
        with urlopen("https://raw.githubusercontent.com/argosopentech/argospm-index/main/index.json", timeout=30) as response:
            index = json.load(response)
        pairs = set()
        for code in sources:
            if code == target:
                continue
            direct = next((p for p in index if p.get("from_code") == code and p.get("to_code") == target), None)
            if direct:
                pairs.add((code, target))
            else:
                if code != "en":
                    pairs.add((code, "en"))
                if target != "en":
                    pairs.add(("en", target))
        for source, dest in sorted(pairs):
            pair_dir = root / "translate" / (source + "_" + dest)
            if package_folder(pair_dir) is not None:
                continue
            choices = [p for p in index if p.get("from_code") == source and p.get("to_code") == dest]
            if not choices:
                raise ValueError("No local translation pack available for " + source + " to " + dest)
            package = choices[-1]
            url = next((x for x in package["links"] if x.startswith("https://")), None)
            if not url:
                raise ValueError("No secure download for requested language pack")
            archive = root / "downloads" / (source + "_" + dest + ".argosmodel")
            print("Preparing translation pack: " + source + " to " + dest, flush=True)
            download_package(package, archive)
            safe_extract(archive, pair_dir)
            archive.unlink()
            if package_folder(pair_dir) is None:
                raise ValueError("Incomplete or unsupported language pack: " + source + " to " + dest)
    content = {
        "schema": 1, "speech_models": sorted(set(previous.get("speech_models", []) + [size])),
        "ocr_groups": groups or previous.get("ocr_groups", []), "inputs": sources,
        "target": target, "created": time.time()
    }
    temp = ready.with_suffix(".tmp")
    temp.write_text(json.dumps(content), "utf-8")
    temp.replace(ready)
    print("Local translation models are ready.", flush=True)

class Engine:
    def __init__(self, root, size, device="cpu"):
        self.root = Path(root)
        self.device, self.notice = select_device(device)
        self.size = size
        self.speech = None
        self.readers = OrderedDict()
        self.translators = OrderedDict()
        self.cache = OrderedDict()
        self.last_language = None
        self.last_speech = ""
        self.ready = json.loads((self.root / "ready.json").read_text("utf-8"))

    def execute(self, action):
        try:
            result = action()
        except (RuntimeError, OSError):
            if self.device != "cuda":
                raise
            self.device = "cpu"
            self.notice = "CUDA failed or ran out of memory; translation continued on CPU."
            self.speech = None
            self.readers.clear()
            self.translators.clear()
            gc.collect()
            try:
                import torch
                torch.cuda.empty_cache()
            except (ImportError, RuntimeError, OSError):
                pass
            result = action()
        result["backend"] = "NVIDIA CUDA" if self.device == "cuda" else "CPU"
        if self.notice:
            result["notice"] = self.notice
        return result

    def pair(self, source, target):
        key = source + "_" + target
        if key in self.translators:
            self.translators.move_to_end(key)
            return self.translators[key]
        import ctranslate2
        import sentencepiece
        directory = self.root / "translate" / key
        folder = package_folder(directory)
        if folder is None:
            raise ValueError("Install the local language pack for " + source + " to " + target + " in Translation settings.")
        meta = json.loads((folder / "metadata.json").read_text("utf-8"))
        tokenizer = (sentencepiece.SentencePieceProcessor(model_file=str(folder / "sentencepiece.model"))
                     if (folder / "sentencepiece.model").is_file()
                     else BPETokenizer(folder / "bpe.model", source, target))
        translator = ctranslate2.Translator(str(folder / "model"), device=self.device,
                                           compute_type="int8_float16" if self.device == "cuda" else "int8",
                                           inter_threads=1, intra_threads=2)
        result = (tokenizer, translator, meta.get("target_prefix", ""))
        self.translators[key] = result
        while len(self.translators) > 3:
            self.translators.popitem(last=False)
        return result

    def translate(self, text, source, target):
        text = text.strip()
        if not text or source == target:
            return text
        key = (source, target, text)
        if key in self.cache:
            self.cache.move_to_end(key)
            return self.cache[key]
        try:
            tokenizer, model, prefix = self.pair(source, target)
        except ValueError:
            if source != "en" and target != "en":
                return self.translate(self.translate(text, source, "en"), "en", target)
            raise
        tokens = tokenizer.encode(text[:3000], out_type=str)
        translated = model.translate_batch([tokens], beam_size=4, length_penalty=0.2,
            replace_unknowns=True, target_prefix=[[prefix]] if prefix else None,
            no_repeat_ngram_size=3, max_decoding_length=min(512, max(32, len(tokens) * 4)))[0].hypotheses[0]
        result = tokenizer.decode(translated).strip()
        if prefix and result.startswith(prefix):
            result = result[len(prefix):].strip()
        self.cache[key] = result
        while len(self.cache) > 256:
            self.cache.popitem(last=False)
        return result

    def audio(self, payload, source, target):
        return self.execute(lambda: self._audio(payload, source, target))

    def _audio(self, payload, source, target):
        from faster_whisper import WhisperModel
        import numpy as np
        import wave
        if self.speech is None:
            self.speech = WhisperModel(str(self.root / "speech" / self.size), device=self.device,
                compute_type="int8_float16" if self.device == "cuda" else "int8", cpu_threads=max(1, min(4, (os.cpu_count() or 2) // 2)),
                num_workers=1, local_files_only=True)
        with wave.open(io.BytesIO(payload), "rb") as wav:
            if wav.getnchannels() != 2 or wav.getsampwidth() != 2 or wav.getframerate() != 44100:
                raise ValueError("Expected app loopback stereo PCM16 at 44100 Hz")
            signal = np.frombuffer(wav.readframes(wav.getnframes()), dtype="<i2").reshape(-1, 2).mean(axis=1) / 32768.0
        if len(signal) == 0 or float(np.sqrt(np.mean(signal**2))) < 0.002:
            return {"text": "", "source": source, "target": target}
        signal = np.interp(np.arange(0, len(signal), 44100 / 16000), np.arange(len(signal)), signal).astype(np.float32)
        segments, info = self.speech.transcribe(signal, language=None if source == "auto" else source,
            task="translate" if target == "en" else "transcribe",
            beam_size=5 if self.device == "cuda" else 3, condition_on_previous_text=False, vad_filter=False,
            no_speech_threshold=0.65, temperature=0)
        text = " ".join(s.text.strip() for s in segments if s.no_speech_prob < 0.65).strip()
        detected = normalize(info.language)
        self.last_language = detected
        if target != "en":
            text = self.translate(text, detected, target)
        # The audio windows overlap by one second. Remove repeated word prefixes.
        words = text.split()
        old = self.last_speech.split()
        for size in range(min(len(old), len(words), 20), 0, -1):
            if [x.casefold() for x in old[-size:]] == [x.casefold() for x in words[:size]]:
                words = words[size:]
                break
        self.last_speech = text
        return {"text": " ".join(words), "source": detected, "target": target}

    def screen(self, payload, source, target):
        return self.execute(lambda: self._screen(payload, source, target))

    def _screen(self, payload, source, target):
        import easyocr
        import numpy as np
        from PIL import Image
        from langdetect import detect, DetectorFactory
        DetectorFactory.seed = 0
        image = Image.open(io.BytesIO(payload)).convert("RGB")
        image.thumbnail((1280, 720))
        installed = self.ready.get("ocr_groups", [])
        groups = [ocr_group(source)] if source != "auto" else installed
        if not groups:
            raise ValueError("Install screen-text models in Translation settings.")
        best = []
        best_score = 0
        for group in groups:
            if group not in installed:
                raise ValueError("Install the " + group + " screen-text model in Translation settings.")
            if group not in self.readers:
                self.readers[group] = easyocr.Reader(OCR_GROUPS[group], gpu=self.device == "cuda",
                    model_storage_directory=str(self.root / "ocr"),
                    user_network_directory=str(self.root / "ocr-user"),
                    download_enabled=False, verbose=False)
                while len(self.readers) > 2:
                    self.readers.popitem(last=False)
            found = self.readers[group].readtext(np.asarray(image), detail=1, paragraph=False,
                canvas_size=1280, batch_size=1, workers=0)
            good = [(box, text, float(conf)) for box, text, conf in found if conf >= 0.45 and len(text.strip()) > 1]
            score = sum(min(len(text), 40) * conf for _, text, conf in good)
            if score > best_score:
                best = good
                best_score = score
        lines = [text for _, text, _ in sorted(best, key=lambda row: (row[0][0][1], row[0][0][0]))]
        text = "\n".join(lines[:10])[:1500]
        if not text:
            return {"text": "", "source": source, "target": target}
        if source == "auto":
            try:
                detected = normalize(detect(text))
            except Exception:
                detected = self.last_language or "en"
        else:
            detected = source
        translated = self.translate(text, detected, target)
        return {"text": translated, "source": detected, "target": target}

def run(root, size, device="auto"):
    block_network()
    engine = Engine(root, size, device)
    # Stdout is only the protocol. Third-party diagnostic output goes to stderr.
    protocol = sys.stdout
    sys.stdout = sys.stderr
    for line in sys.stdin:
        if len(line) > 8 * 1024 * 1024:
            break
        try:
            request = json.loads(line)
            source = request.get("source", "auto")
            target = request.get("target", "en")
            if not re.fullmatch(r"auto|[a-z]{2,3}", source) or not re.fullmatch(r"[a-z]{2,3}", target):
                raise ValueError("Unsupported language code")
            payload = base64.b64decode(request["data"], validate=True)
            with contextlib.redirect_stdout(sys.stderr):
                result = engine.audio(payload, source, target) if request["kind"] == "audio" else engine.screen(payload, source, target)
            result["id"] = request["id"]
        except Exception as error:
            result = {"id": request.get("id", 0) if "request" in locals() else 0,
                      "text": "", "source": "", "target": "", "error": str(error)[:400]}
        protocol.write(json.dumps(result, ensure_ascii=True) + "\n")
        protocol.flush()

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--models", required=True)
    parser.add_argument("--size", choices=["tiny", "base", "small", "medium"], default="base")
    parser.add_argument("--device", choices=["auto", "cpu", "cuda"], default="auto")
    parser.add_argument("--setup", action="store_true")
    parser.add_argument("--speech-only", action="store_true")
    parser.add_argument("--inputs", default="en,es,fr,de,pt,it")
    parser.add_argument("--target", default="en")
    args = parser.parse_args()
    root = Path(args.models)
    if args.setup:
        setup(root, [x.strip() for x in args.inputs.split(",") if re.fullmatch(r"[a-z]{2,3}", x.strip())],
              args.target, args.size, args.speech_only, args.device)
    else:
        run(root, args.size, args.device)
