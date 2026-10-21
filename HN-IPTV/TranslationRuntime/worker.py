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
sys.path.insert(0, str(Path(__file__).resolve().parent))
from speech_cues import SpeechCueCommitter, split_cues
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



VAD_PARAMETERS = dict(threshold=0.45, min_speech_duration_ms=150,
    min_silence_duration_ms=250, speech_pad_ms=150)

def speech_repository(size):
    return "mobiuslabsgmbh/faster-whisper-large-v3-turbo" if size == "turbo" else "Systran/faster-whisper-" + size

def suppress_noise(signal):
    """Conservative optional spectral suppression; never attenuate by more than 6 dB."""
    import numpy as np
    from scipy.signal import stft, istft
    if len(signal) < 512:
        return signal
    _, _, spectrum = stft(signal, 16000, nperseg=512, noverlap=384)
    power = np.abs(spectrum)**2
    floor = np.percentile(power, 15, axis=1, keepdims=True)
    gain = np.sqrt(np.clip(1 - floor / (power + 1e-10), 0.25, 1))
    _, cleaned = istft(spectrum * gain, 16000, nperseg=512, noverlap=384)
    return cleaned[:len(signal)].astype(np.float32)

class MarianTokenizer:
    translation_options = dict(beam_size=3, length_penalty=1, replace_unknowns=False, max_decoding_length=160)
    def __init__(self, folder):
        import sentencepiece
        self.source = sentencepiece.SentencePieceProcessor(model_file=str(folder / "source.spm"))
        self.target = sentencepiece.SentencePieceProcessor(model_file=str(folder / "target.spm"))
    def encode(self, text, out_type=str):
        return self.source.encode(text, out_type=str)[:255] + ["</s>"]
    def decode(self, tokens):
        return self.target.decode(tokens)

# This checksum is pinned after the release pack's offline CPU validation.
OPUS_SHA256 = "3dc65d3ee89b5cfd26d9dfd07bd0b72f4eea956ddba495e9efa3f6ad1d14fbe3"
OPUS_URL = "https://github.com/HoodNewsSanaa/HN-IPTV/releases/download/translation-models-v1/opus-ar-en-ct2-f16.zip"

def install_opus(root):
    import shutil
    destination = root / "text" / "opus-ar-en-v1"
    if all((destination / name).is_file() for name in ("model/model.bin", "model/config.json", "source.spm", "target.spm", "metadata.json")):
        print("Reusing cached OPUS-MT Arabic to English.", flush=True)
        return
    archive = root / "downloads" / "opus-ar-en-ct2-f16.zip"
    print("Preparing compact OPUS-MT Arabic to English...", flush=True)
    download(OPUS_URL, archive)
    with archive.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    if digest != OPUS_SHA256:
        archive.unlink(missing_ok=True)
        raise ValueError("OPUS model checksum did not match the pinned release")
    staging = destination.with_name(destination.name + ".partial")
    if staging.exists(): shutil.rmtree(staging)
    try:
        safe_extract(archive, staging)
        meta = json.loads((staging / "metadata.json").read_text("utf-8"))
        if meta.get("type") != "marian" or meta.get("source") != "ar" or meta.get("target") != "en":
            raise ValueError("Invalid OPUS model metadata")
        import ctranslate2
        ctranslate2.Translator(str(staging / "model"), device="cpu", compute_type="float32")
        destination.parent.mkdir(parents=True, exist_ok=True)
        if destination.exists(): shutil.rmtree(destination)
        staging.replace(destination)
    finally:
        archive.unlink(missing_ok=True)
        if staging.exists(): shutil.rmtree(staging)

_DLL_HANDLES = []
def cuda_libraries_ready():
    # Resolve the private CUDA libraries without importing all of PyTorch for speech.
    if os.name != "nt":
        return True
    library = Path(sys.executable).parent / "Lib/site-packages/torch/lib"
    if not all((library / name).is_file() for name in ("cublas64_12.dll", "cudnn64_9.dll")):
        return False
    _DLL_HANDLES.append(os.add_dll_directory(str(library)))
    os.environ["PATH"] = str(library) + os.pathsep + os.environ.get("PATH", "")
    return True

def select_device(requested):
    if requested == "cpu":
        return "cpu", None
    try:
        import ctranslate2
        if cuda_libraries_ready() and ctranslate2.get_cuda_device_count() > 0:
            return "cuda", None
        return "cpu", ("NVIDIA CUDA is unavailable; using CPU. Prepare CUDA in Translation settings."
                       if requested == "cuda" else None)
    except (ImportError, RuntimeError, OSError):
        return "cpu", "CUDA could not load; using CPU. Prepare CUDA in Translation settings."


def setup(root, source_codes, target, size, speech_only=False, device="cpu", text_model="auto"):
    from faster_whisper import WhisperModel
    from huggingface_hub import snapshot_download
    root.mkdir(parents=True, exist_ok=True)
    os.environ.update(HF_HUB_DISABLE_TELEMETRY="1", HF_HUB_DISABLE_XET="1", HF_HUB_DISABLE_PROGRESS_BARS="1")
    selected, notice = select_device(device)
    size = ("turbo" if selected == "cuda" else "small") if size == "auto" else size
    print("Preparing multilingual speech model: " + size, flush=True)
    model_dir = root / "speech" / size
    if not (model_dir / "model.bin").exists():
        snapshot_download(speech_repository(size), local_dir=str(model_dir),
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
        index = []
        if text_model != "azure":
            with urlopen("https://raw.githubusercontent.com/argosopentech/argospm-index/main/index.json", timeout=30) as response:
                index = json.load(response)
        pairs = set()
        for code in ([] if text_model == "azure" else sources):
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
            if text_model == "auto" and (source, dest) == ("ar", "en"):
                install_opus(root)
                continue
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


def ocr_regions(rows, source):
    """Keep line geometry, reading order, and separate distant columns."""
    lines = []
    for box, text, confidence in sorted(rows, key=lambda row: min(p[1] for p in row[0])):
        x0, x1 = float(min(p[0] for p in box)), float(max(p[0] for p in box))
        y0, y1 = float(min(p[1] for p in box)), float(max(p[1] for p in box))
        center, height = (y0+y1)/2, max(1,y1-y0)
        line = next((r for r in reversed(lines)
            if abs(r["y"]-center) <= max(r["height"],height)*0.55
            and min(max(0,x0-word[1],word[0]-x1) for word in r["words"]) <= max(24,height*3.5)), None)
        if line is None:
            line = {"y":center,"height":height,"words":[],"top":y0,"bottom":y1,"confidence":float(confidence)}
            lines.append(line)
        line["words"].append((x0,x1,text))
        line["top"], line["bottom"] = min(line["top"],y0), max(line["bottom"],y1)
        line["confidence"] = min(line["confidence"],float(confidence))
    output = []
    for line in sorted(lines,key=lambda r:r["y"]):
        rtl = source in ("ar","fa","ur","he") or any(re.search(r"[\u0590-\u08ff]",w[2]) for w in line["words"])
        words = sorted(line["words"],key=lambda w:w[0],reverse=rtl)
        output.append({"text":" ".join(w[2] for w in words),
            "box":(min(w[0] for w in words),line["top"],max(w[1] for w in words),line["bottom"]),
            "confidence":line["confidence"]})
    return output

def ocr_lines(rows, source):
    return [r["text"] for r in ocr_regions(rows,source)]

def region_style(image, rectangle):
    """A sampled opaque background covers the source lettering without another model."""
    import numpy as np
    patch = np.asarray(image.crop(rectangle).resize((64,24)),dtype=np.uint8).reshape(-1,3)
    bins = patch.astype(np.int32)//16
    codes = bins[:,0]*256+bins[:,1]*16+bins[:,2]
    dominant = int(np.bincount(codes,minlength=4096).argmax())
    rgb = np.median(patch[codes==dominant],axis=0).astype(int)
    background = "#"+"".join(f"{int(value):02X}" for value in rgb)
    luminance = float(rgb @ np.asarray([0.2126,0.7152,0.0722]))
    return background, "#101010" if luminance>145 else "#FFFFFF"

def word_key(word):
    return re.sub(r"[^\w]", "", word.casefold())

def speech_delta(previous, current):
    words, old = current.split(), previous.split()
    for count in range(min(len(old), len(words), 30), 0, -1):
        if [word_key(x) for x in old[-count:]] == [word_key(x) for x in words[:count]]:
            return " ".join(words[count:])
    return current

class Engine:
    def __init__(self, root, size, device="cpu", text_model="auto", pipeline="auto", noise=False):
        self.root = Path(root)
        self.device, self.notice = select_device(device)
        self.size = self.requested_size = size
        self.text_model, self.pipeline, self.noise = text_model, pipeline, noise
        self.language_until = 0
        self.speech = None
        self.readers = OrderedDict()
        self.translators = OrderedDict()
        self.cache = OrderedDict()
        self.last_language = None
        self.external_translate = None
        self.last_speech = ""
        self.cue_committer = SpeechCueCommitter()
        self.prepared = set()
        self.screen_snapshot = None
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
            self.prepared.clear()
            self.size = self.requested_size
            self.readers.clear()
            self.translators.clear()
            self.screen_snapshot = None
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
        opus = self.root / "text" / "opus-ar-en-v1"
        if self.text_model == "auto" and key == "ar_en" and (opus / "model/model.bin").is_file():
            tokenizer = MarianTokenizer(opus)
            translator = ctranslate2.Translator(str(opus / "model"), device=self.device,
                compute_type="float16" if self.device == "cuda" else "float32", inter_threads=1, intra_threads=2)
            result = (tokenizer, translator, "")
            self.translators[key] = result
            while len(self.translators) > 3: self.translators.popitem(last=False)
            return result
        folder = package_folder(directory)
        if folder is not None and self.text_model == "auto" and key == "ar_en":
            self.notice = "Using cached Argos Arabic to English. Prepare models to enable the benchmark-selected OPUS pack."
        if folder is None:
            raise ValueError("Install the local language pack for " + source + " to " + target + " in Translation settings.")
        meta = json.loads((folder / "metadata.json").read_text("utf-8"))
        tokenizer = (sentencepiece.SentencePieceProcessor(model_file=str(folder / "sentencepiece.model"))
                     if (folder / "sentencepiece.model").is_file()
                     else BPETokenizer(folder / "bpe.model", source, target))
        translator = ctranslate2.Translator(str(folder / "model"), device=self.device,
                                           compute_type="float16" if self.device == "cuda" else "float32",
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
        if self.text_model == "azure":
            if self.external_translate is None:
                raise ValueError("Azure translation requires the application text bridge.")
            result = self.external_translate(text, source, target)
            self.cache[key] = result
            while len(self.cache) > 256: self.cache.popitem(last=False)
            return result
        try:
            tokenizer, model, prefix = self.pair(source, target)
        except ValueError:
            if source != "en" and target != "en":
                return self.translate(self.translate(text, source, "en"), "en", target)
            raise
        tokens = tokenizer.encode(text[:3000], out_type=str)
        options = dict(beam_size=4, length_penalty=0.2, replace_unknowns=True,
            target_prefix=[[prefix]] if prefix else None, no_repeat_ngram_size=3,
            max_decoding_length=min(512, max(32, len(tokens) * 4)))
        options.update(getattr(tokenizer, "translation_options", {}))
        translated = model.translate_batch([tokens], **options)[0].hypotheses[0]
        result = tokenizer.decode(translated).strip()
        if prefix and result.startswith(prefix):
            result = result[len(prefix):].strip()
        self.cache[key] = result
        while len(self.cache) > 256:
            self.cache.popitem(last=False)
        return result


    def ensure_speech(self):
        from faster_whisper import WhisperModel
        if self.speech is None:
            if self.size == "auto":
                choices = ("turbo", "medium", "small", "base", "tiny") if self.device == "cuda" else ("small", "base", "tiny", "turbo", "medium")
                self.size = next((x for x in choices if (self.root / "speech" / x / "model.bin").is_file()), choices[0])
            if not (self.root / "speech" / self.size / "model.bin").is_file():
                raise ValueError("Prepare the " + self.size + " speech model in Translation settings.")
            self.speech = WhisperModel(str(self.root / "speech" / self.size), device=self.device,
                compute_type="int8_float16" if self.device == "cuda" else "int8", cpu_threads=max(1, min(4, (os.cpu_count() or 2) // 2)),
                num_workers=1, local_files_only=True)

    def reader(self, group):
        if group not in self.readers:
            import easyocr
            self.readers[group] = easyocr.Reader(OCR_GROUPS[group], gpu=self.device == "cuda",
                model_storage_directory=str(self.root / "ocr"),
                user_network_directory=str(self.root / "ocr-user"),
                download_enabled=False, verbose=False)
            while len(self.readers) > 2:
                self.readers.popitem(last=False)
        self.readers.move_to_end(group)
        return self.readers[group]

    def uses_staged(self, source, target):
        return (self.text_model == "azure" or self.size == "turbo" or self.pipeline == "staged" or target != "en"
            or (self.pipeline == "auto" and (source == "ar" or "ar" in self.ready.get("inputs", []))))

    def reset(self):
        self.cue_committer = SpeechCueCommitter()
        self.last_language = None
        self.last_speech = ""
        self.language_until = 0
        self.screen_snapshot = None
        return self.execute(lambda: {"text":"", "source":"", "target":""})

    def prepare(self, kind, source, target):
        def load():
            key = (kind,source,target,self.device)
            if key in self.prepared:
                return {"text":"", "source":source, "target":target}

            if kind == "audio":
                import numpy as np
                self.ensure_speech()
                segments, _ = self.speech.transcribe(np.zeros(16000, dtype=np.float32),
                    language="en", beam_size=1, vad_filter=False)
                list(segments)  # Initialize feature extraction and CUDA before capturing live audio.
                from faster_whisper.vad import get_speech_timestamps, VadOptions
                get_speech_timestamps(np.zeros(16000, dtype=np.float32), VadOptions(**VAD_PARAMETERS))
                if self.uses_staged(source, target) and self.text_model != "azure":
                    inputs = [source] if source != "auto" else self.ready.get("inputs", [])
                    for code in inputs:
                        if code == target: continue
                        try:
                            self.pair(code, target)
                        except ValueError:
                            if code == "en" or target == "en": raise
                            self.pair(code, "en"); self.pair("en", target)
            else:
                import numpy as np
                groups = [ocr_group(source)] if source != "auto" else self.ready.get("ocr_groups", [])
                if not groups:
                    raise ValueError("Prepare screen-text models in Translation settings.")
                for group in groups:
                    if group not in self.ready.get("ocr_groups", []):
                        raise ValueError("Install the " + group + " screen-text model in Translation settings.")
                    self.reader(group).readtext(np.zeros((64, 256, 3), dtype=np.uint8),
                        canvas_size=256, workers=0)
            self.prepared.add(key)
            return {"text": "", "source": source, "target": target}
        return self.execute(load)

    def audio(self, payload, source, target, context_seconds=0, audio_end_seconds=None):
        return self.execute(lambda: self._audio(payload, source, target, context_seconds, audio_end_seconds))

    def _audio(self, payload, source, target, context_seconds=0, audio_end_seconds=None):
        from faster_whisper import WhisperModel
        import numpy as np
        import wave
        self.ensure_speech()
        with wave.open(io.BytesIO(payload), "rb") as wav:
            if wav.getnchannels() != 2 or wav.getsampwidth() != 2 or wav.getframerate() != 44100:
                raise ValueError("Expected app loopback stereo PCM16 at 44100 Hz")
            signal = np.frombuffer(wav.readframes(wav.getnframes()), dtype="<i2").reshape(-1, 2).mean(axis=1) / 32768.0
        if len(signal) == 0 or float(np.sqrt(np.mean(signal**2))) < 0.002:
            return {"text": "", "source": source, "target": target}
        signal = np.interp(np.arange(0, len(signal), 44100 / 16000), np.arange(len(signal)), signal).astype(np.float32)
        from faster_whisper.vad import get_speech_timestamps, VadOptions
        if self.noise:
            signal = suppress_noise(signal)
        timed = audio_end_seconds is not None
        final = False
        window_duration = len(signal) / 16000
        window_start = float(audio_end_seconds or 0) - window_duration
        if timed and self.cue_committer.committed_until > window_start + 3:
            # Keep three seconds of acoustic context before already confirmed speech;
            # never discard the unconfirmed sentence that is still being recognized.
            cut = int((self.cue_committer.committed_until-window_start-3)*16000)
            signal = signal[cut:]
            window_start += cut/16000
            context_seconds = max(0,context_seconds-cut/16000)
            window_duration = len(signal)/16000
        if context_seconds > 0:
            fresh = signal[min(len(signal), int(context_seconds * 16000)):]
            if len(fresh) < 512 or not get_speech_timestamps(fresh, VadOptions(**VAD_PARAMETERS)):
                if not timed or not self.cue_committer.pending():
                    return {"text": "", "source": self.last_language or source, "target": target}
                final = True
        # Analyze short live windows before decoding; music/silence should not create captions.
        spans = get_speech_timestamps(signal, VadOptions(**VAD_PARAMETERS))
        if not spans or (not final and spans[-1]["end"] <= max(0, context_seconds) * 16000):
            return {"text": "", "source": self.last_language or source, "target": target}
        # Preserve timing inside a phrase while trimming leading/trailing non-speech.
        trim_start = spans[0]["start"] / 16000
        final = final or spans[-1]["end"] / 16000 < window_duration - .6
        signal = signal[spans[0]["start"]:spans[-1]["end"]]
        staged = self.uses_staged(source, target)
        language = source if source != "auto" else (self.last_language if time.monotonic() < self.language_until else None)
        segments, info = self.speech.transcribe(signal, language=language,
            task="transcribe" if staged else "translate",
            beam_size=2, condition_on_previous_text=False, vad_filter=False,
            no_speech_threshold=0.65, temperature=0, word_timestamps=timed)
        segments = list(segments)
        text = " ".join(s.text.strip() for s in segments
            if not (s.no_speech_prob > 0.65 or s.avg_logprob < -1.2 or s.compression_ratio > 2.4)).strip()
        detected = normalize(info.language)
        if text:
            self.last_language = detected
            if language is None and getattr(info, "language_probability", 0) >= 0.8:
                self.language_until = time.monotonic() + 5
        if timed:
            words = []
            for segment in segments:
                if segment.no_speech_prob > .65 or segment.avg_logprob < -1.2 or segment.compression_ratio > 2.4:
                    continue
                for word in segment.words or []:
                    words.append({"text":word.word, "start":window_start+trim_start+word.start,
                        "end":window_start+trim_start+word.end, "probability":word.probability})
            before_commit = (self.cue_committer.previous, self.cue_committer.committed_until)
            phrases = self.cue_committer.update(words, float(audio_end_seconds), window_start, final)
            cues = []
            # Translate the confirmed phrase group together before splitting display cues.
            # Display length must not strip the translator of grammatical context.
            try:
                for phrase in ([" ".join(phrases)] if phrases else []):
                    translated = self.translate(phrase, detected, target) if staged else phrase
                    cues.extend(split_cues(translated))
            except Exception:
                self.cue_committer.previous, self.cue_committer.committed_until = before_commit
                raise
            return {"text":"", "source":detected, "target":target, "speech_cues":cues}
        if staged:
            text = self.translate(text, detected, target)
        # Replace the rolling caption so revised translations never append duplicate phrases.
        return {"text": text, "source": detected, "target": target, "replace_speech": True}

    def screen(self, payload, source, target):
        return self.execute(lambda: self._screen(payload, source, target))

    def _screen(self, payload, source, target):
        import numpy as np
        from PIL import Image
        from langdetect import detect, DetectorFactory
        DetectorFactory.seed = 0
        image = Image.open(io.BytesIO(payload)).convert("RGB")
        image.thumbnail((1920, 1080))
        installed = self.ready.get("ocr_groups", [])
        groups = [ocr_group(source)] if source != "auto" else installed
        if not groups:
            raise ValueError("Install screen-text models in Translation settings.")
        now = time.monotonic()
        key = (source, target, image.size)
        previous = self.screen_snapshot
        if previous and previous["key"] == key and now - previous["at"] < 3:
            # Moving video outside the text does not require recognizing identical captions again.
            unchanged = bool(previous["patches"])
            for rectangle, before in previous["patches"]:
                after = np.asarray(image.crop(rectangle).convert("L").resize((96, 24)), dtype=np.float32)
                if float(np.mean(np.abs(after - before))) > 5:
                    unchanged = False
                    break
            if unchanged:
                cached = dict(previous["result"]); cached["cached"] = True
                return cached
        best, best_score = [], 0
        for group in groups:
            if group not in installed:
                raise ValueError("Install the " + group + " screen-text model in Translation settings.")
            found = self.reader(group).readtext(np.asarray(image), detail=1, paragraph=False,
                canvas_size=1920, batch_size=8 if self.device == "cuda" else 1, workers=0,
                text_threshold=0.6, low_text=0.3, contrast_ths=0.2, adjust_contrast=0.7)
            good = [(box, text.strip(), float(conf)) for box, text, conf in found
                    if conf >= 0.3 and len(text.strip()) > 1 and any(c.isalpha() for c in text)]
            score = sum(min(len(text), 40) * conf for _, text, conf in good)
            if score > best_score:
                best, best_score = good, score
        lines = ocr_regions(best, source)
        translated, regions, detected = [], [], source
        installed_inputs = self.ready.get("inputs", [])
        for observation in lines[:32]:
            line = observation["text"]
            if source == "auto":
                try:
                    detected = normalize(detect(line))
                except Exception:
                    detected = self.last_language or (installed_inputs[0] if len(installed_inputs) == 1 else "en")
                if re.search(r"[\u0600-\u06ff]", line) and detected not in installed_inputs:
                    detected = next((x for x in installed_inputs if x in ("ar", "fa", "ur")), detected)
            else:
                detected = source
            # The Arabic/etc. recognizer also reads Latin station logos. They are already English.
            if detected in ("ar", "fa", "ur", "ru", "ja", "ko", "zh") and re.search(r"[a-zA-Z]", line) and not re.search(r"[^\x00-\x7f]", line):
                detected = "en"
            if detected == target:
                continue
            replacement = self.translate(line[:1000],detected,target)
            if not replacement.strip():
                continue
            translated.append(replacement)
            left,top,right,bottom = observation["box"]
            padding = max(2,(bottom-top)*0.08)
            rectangle = (max(0,int(left-padding)),max(0,int(top-padding)),
                min(image.width,int(right+padding+1)),min(image.height,int(bottom+padding+1)))
            background,foreground = region_style(image,rectangle)
            regions.append({"x":rectangle[0]/image.width,"y":rectangle[1]/image.height,
                "width":(rectangle[2]-rectangle[0])/image.width,"height":(rectangle[3]-rectangle[1])/image.height,
                "text":replacement[:2000],"background":background,"foreground":foreground})
        result = {"text": "\n".join(translated[-6:])[:1500], "source": detected, "target": target,
            "regions": regions, "frame_width": image.width, "frame_height": image.height}
        patches = []
        for box, _, _ in best:
            rectangle = (max(0, int(min(p[0] for p in box))), max(0, int(min(p[1] for p in box))),
                         min(image.width, int(max(p[0] for p in box)) + 1), min(image.height, int(max(p[1] for p in box)) + 1))
            patches.append((rectangle, np.asarray(image.crop(rectangle).convert("L").resize((96, 24)), dtype=np.float32)))
        self.screen_snapshot = {"key": key, "at": time.monotonic(), "patches": patches, "result": dict(result)}
        return result

def run(root, size, device="auto", text_model="auto", pipeline="auto", noise=False):
    block_network()
    engine = Engine(root, size, device, text_model, pipeline, noise)
    # Stdout is only the protocol. Third-party diagnostic output goes to stderr.
    protocol = sys.stdout
    sys.stdout = sys.stderr
    def external_translate(text, source, target):
        protocol.write(json.dumps({"id":request["id"], "translation_request":{
            "text":text, "source":source, "target":target}},ensure_ascii=True)+"\n")
        protocol.flush()
        line = sys.stdin.readline(128*1024)
        reply = json.loads(line)
        if reply.get("error"): raise ValueError(str(reply["error"])[:300])
        value = reply.get("translation_response")
        if not isinstance(value,str) or not value.strip(): raise ValueError("Empty Azure translation response.")
        return value
    if text_model == "azure": engine.external_translate = external_translate
    for line in sys.stdin:
        if len(line) > 16 * 1024 * 1024:
            break
        try:
            request = json.loads(line)
            source = request.get("source", "auto")
            target = request.get("target", "en")
            if not re.fullmatch(r"auto|[a-z]{2,3}", source) or not re.fullmatch(r"[a-z]{2,3}", target):
                raise ValueError("Unsupported language code")
            payload = base64.b64decode(request["data"], validate=True)
            with contextlib.redirect_stdout(sys.stderr):
                kind = request["kind"]
                if kind == "reset":
                    result = engine.reset()
                elif kind.startswith("prepare_"):
                    result = engine.prepare(kind.removeprefix("prepare_"), source, target)
                elif kind == "audio":
                    result = engine.audio(payload, source, target, min(20, max(0, float(request.get("context_seconds", 0)))), request.get("audio_end_seconds"))
                elif kind == "screen":
                    result = engine.screen(payload, source, target)
                else:
                    raise ValueError("Unknown translation request")
            result["id"] = request["id"]
        except Exception as error:
            result = {"id": request.get("id", 0) if "request" in locals() else 0,
                      "text": "", "source": "", "target": "", "error": str(error)[:400]}
        protocol.write(json.dumps(result, ensure_ascii=True) + "\n")
        protocol.flush()

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--models", required=True)
    parser.add_argument("--size", choices=["auto", "tiny", "base", "small", "medium", "turbo"], default="auto")
    parser.add_argument("--device", choices=["auto", "cpu", "cuda"], default="auto")
    parser.add_argument("--text-model", choices=["auto", "argos", "azure"], default="auto")
    parser.add_argument("--pipeline", choices=["auto", "direct", "staged"], default="auto")
    parser.add_argument("--noise", action="store_true")
    parser.add_argument("--setup", action="store_true")
    parser.add_argument("--speech-only", action="store_true")
    parser.add_argument("--inputs", default="en,es,fr,de,pt,it")
    parser.add_argument("--target", default="en")
    args = parser.parse_args()
    root = Path(args.models)
    if args.setup:
        setup(root, [x.strip() for x in args.inputs.split(",") if re.fullmatch(r"[a-z]{2,3}", x.strip())],
              args.target, args.size, args.speech_only, args.device, args.text_model)
    else:
        run(root, args.size, args.device, args.text_model, args.pipeline, args.noise)
