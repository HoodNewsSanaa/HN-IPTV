import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("hn_worker", ROOT / "HN-IPTV/TranslationRuntime/worker.py")
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)

class WorkerTests(unittest.TestCase):
    def test_archive_cannot_escape_model_folder(self):
        with tempfile.TemporaryDirectory() as temp:
            archive = Path(temp) / "bad.zip"
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr("../outside.txt", "must not escape")
            with self.assertRaises(ValueError):
                worker.safe_extract(archive, Path(temp) / "models")
            self.assertFalse((Path(temp) / "outside.txt").exists())

    def test_arabic_words_are_joined_in_reading_order(self):
        rows = [
            ([[10, 10], [80, 10], [80, 35], [10, 35]], "بالعالم", 0.9),
            ([[90, 12], [150, 12], [150, 37], [90, 37]], "مرحبا", 0.9),
        ]
        self.assertEqual(worker.ocr_lines(rows, "ar"), ["مرحبا بالعالم"])
        self.assertEqual(worker.ocr_lines([
            ([[80, 10], [150, 10], [150, 35], [80, 35]], "world", 0.9),
            ([[10, 10], [70, 10], [70, 35], [10, 35]], "Hello", 0.9)], "en"), ["Hello world"])

    def test_overlapping_speech_ignores_punctuation_without_erasing_new_words(self):
        self.assertEqual(worker.speech_delta("Hello, world.", "World! Here is the news."), "Here is the news.")
        self.assertEqual(worker.speech_delta("Hello world", ""), "")
        self.assertEqual(worker.speech_delta("مرحبا بالعالم", "بالعالم اليوم"), "اليوم")

    def test_cpu_selection_does_not_require_gpu_libraries(self):
        self.assertEqual(worker.select_device("cpu"), ("cpu", None))

    def test_unavailable_cuda_uses_cpu_with_explanation(self):
        import types
        fake_torch = types.SimpleNamespace(cuda=types.SimpleNamespace(is_available=lambda: False))
        fake_ct2 = types.SimpleNamespace(get_cuda_device_count=lambda: 0)
        with patch.dict(sys.modules, {"torch": fake_torch, "ctranslate2": fake_ct2}):
            selected, notice = worker.select_device("cuda")
        self.assertEqual(selected, "cpu")
        self.assertIn("using CPU", notice)

    def test_cuda_failure_retries_on_cpu_and_releases_gpu_models(self):
        with tempfile.TemporaryDirectory() as temp:
            Path(temp, "ready.json").write_text("{}")
            engine = worker.Engine(temp, "tiny")
            engine.device = "cuda"
            engine.speech = object()
            engine.readers["latin"] = object()
            engine.translators["es_en"] = object()
            calls = []
            def infer():
                calls.append(engine.device)
                if engine.device == "cuda":
                    raise RuntimeError("CUDA out of memory")
                return {"text": "hello"}
            result = engine.execute(infer)
            self.assertEqual(calls, ["cuda", "cpu"])
            self.assertEqual(result["backend"], "CPU")
            self.assertIsNone(engine.speech)
            self.assertFalse(engine.readers)
            self.assertFalse(engine.translators)
            self.assertIn("continued on CPU", result["notice"])

    def test_cached_bpe_and_sentencepiece_packages_are_recognized(self):
        with tempfile.TemporaryDirectory() as temp:
            for name in ("sentencepiece.model", "bpe.model"):
                folder = Path(temp) / name.replace(".", "_")
                (folder / "model").mkdir(parents=True)
                (folder / "metadata.json").write_text("{}")
                (folder / name).write_text("tokenizer")
                self.assertIsNone(worker.package_folder(folder))
                (folder / "model/model.bin").write_bytes(b"model")
                self.assertEqual(worker.package_folder(folder), folder)

    def test_offline_worker_denies_connections_and_dns(self):
        code = ("import importlib.util,socket; "
                "s=importlib.util.spec_from_file_location('w',sys.argv[1]); "
                "w=importlib.util.module_from_spec(s);s.loader.exec_module(w);w.block_network();"
                "socket.create_connection(('example.com',443))")
        result = subprocess.run([sys.executable, "-c", "import sys;" + code, str(Path(worker.__file__))],
                                capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Network access is disabled", result.stderr)

    def test_model_download_retries_the_publishers_public_mirror(self):
        original = worker.download
        tried = []
        def simulated(url, path):
            tried.append(url)
            if "argos-net.com" in url:
                raise OSError("host unavailable")
        worker.download = simulated
        try:
            worker.download_package({"links": ["https://argos-net.com/v1/translate-en_es-1_0.argosmodel"]}, "unused")
        finally:
            worker.download = original
        self.assertEqual(len(tried), 2)
        self.assertTrue(tried[1].startswith("https://data.argosopentech.com/argospm/v1/"))

    def test_language_scripts_are_grouped_without_mixing_incompatible_readers(self):
        self.assertEqual(worker.ocr_group("es"), "latin")
        self.assertEqual(worker.ocr_group("ja"), "ja")
        self.assertEqual(worker.ocr_group("ru"), "cyrillic")
        self.assertEqual(worker.ocr_group("ar"), "arabic")

    def test_translation_cache_is_bounded_and_noop_keeps_text(self):
        with tempfile.TemporaryDirectory() as temp:
            Path(temp, "ready.json").write_text("{}")
            engine = worker.Engine(temp, "tiny")
            class Tokenizer:
                def encode(self, text, out_type=None):
                    return text.split()
                def decode(self, tokens):
                    return " ".join(tokens)
            class Model:
                calls = 0
                def translate_batch(self, tokens, **kwargs):
                    self.calls += 1
                    class Result:
                        hypotheses = [["hola", "mundo"]]
                    return [Result()]
            model = Model()
            engine.pair = lambda source, target: (Tokenizer(), model, "")
            self.assertEqual(engine.translate("hello", "en", "en"), "hello")
            self.assertEqual(engine.translate("hello", "en", "es"), "hola mundo")
            self.assertEqual(engine.translate("hello", "en", "es"), "hola mundo")
            self.assertEqual(model.calls, 1)
            for i in range(300):
                engine.translate("hello " + str(i), "en", "es")
            self.assertLessEqual(len(engine.cache), 256)

    def test_missing_pack_is_reported_instead_of_using_a_cloud_service(self):
        with tempfile.TemporaryDirectory() as temp:
            Path(temp, "ready.json").write_text("{}")
            engine = worker.Engine(temp, "tiny")
            def missing(source, target):
                raise ValueError("Install the local language pack")
            engine.pair = missing
            with self.assertRaisesRegex(ValueError, "Install the local"):
                engine.translate("hello", "en", "es")

if __name__ == "__main__":
    unittest.main()
