import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
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
