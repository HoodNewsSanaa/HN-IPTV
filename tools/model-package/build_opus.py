"""Build a pinned, standalone OPUS CTranslate2 package and verify offline CPU parity."""
import hashlib,json,shutil,time,zipfile
from pathlib import Path
import numpy as np
from huggingface_hub import HfApi,snapshot_download
import ctranslate2,sentencepiece
from sacrebleu.metrics import CHRF
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/"model-package";OUT.mkdir(exist_ok=True)
repo="Helsinki-NLP/opus-mt-ar-en";info=HfApi().model_info(repo);revision=info.sha
files=[s.rfilename for s in info.siblings]
weights="*.safetensors" if any(p.endswith(".safetensors") and "/" not in p for p in files) else "pytorch_model*.bin"
source=Path(snapshot_download(repo,revision=revision,allow_patterns=["*.json","*.spm","README.md",weights]))
pack=OUT/"opus-ar-en-ct2-f16";pack.mkdir(exist_ok=True)
ctranslate2.converters.TransformersConverter(str(source)).convert(str(pack/"model"),quantization="float16",force=True)
for name in ("source.spm","target.spm"):shutil.copyfile(source/name,pack/name)
if (source/"README.md").exists():shutil.copyfile(source/"README.md",pack/"UPSTREAM-README.md")
from urllib.request import urlopen
(pack/"LICENSE").write_bytes(urlopen("https://www.apache.org/licenses/LICENSE-2.0.txt").read())
(pack/"NOTICE").write_text("OPUS-MT Arabic to English, Helsinki-NLP. Apache-2.0.\nSource: https://huggingface.co/"+repo+"/tree/"+revision+"\nConverted by HN-IPTV to CTranslate2 float16; upstream tokenizer files preserved.\n","utf-8")
metadata=dict(schema=1,type="marian",source="ar",target="en",source_repo=repo,source_revision=revision,weights="float16",source_eos=True)
(pack/"metadata.json").write_text(json.dumps(metadata,indent=2),"utf-8")
src=sentencepiece.SentencePieceProcessor(model_file=str(pack/"source.spm"));dst=sentencepiece.SentencePieceProcessor(model_file=str(pack/"target.spm"))
model=ctranslate2.Translator(str(pack/"model"),device="cpu",compute_type="float32",inter_threads=1,intra_threads=2)
from transformers import AutoTokenizer
tok=AutoTokenizer.from_pretrained(source,local_files_only=True)
manifest=json.loads((ROOT/"benchmark-data/manifest.json").read_text("utf-8"))
def infer(value):
    tokens=src.encode(value,out_type=str)+["</s>"]
    raw=model.translate_batch([tokens],beam_size=3,length_penalty=1,max_decoding_length=160,no_repeat_ngram_size=3)[0].hypotheses[0]
    return dst.decode(raw).strip()
for sample in manifest["text"][:8]:
    raw=model.translate_batch([tok.convert_ids_to_tokens(tok.encode(sample["source"]))],beam_size=3,max_decoding_length=160,no_repeat_ngram_size=3)[0].hypotheses[0]
    expected=tok.decode(tok.convert_tokens_to_ids(raw),skip_special_tokens=True).strip()
    assert infer(sample["source"])==expected,"Tokenizer parity failed"
# All timed inference occurs after network is blocked.
import socket
def denied(*a,**k):raise RuntimeError("Offline package validation")
socket.socket.connect=denied;socket.create_connection=denied;socket.getaddrinfo=denied
infer(manifest["text"][0]["source"]);groups={}
for condition in ("clean","ocr_corruption"):
    predictions=[];refs=[];latencies=[]
    for sample in manifest["text"]:
        rng=np.random.default_rng(137)
        value=sample["source"] if condition=="clean" else "".join(c for c in sample["source"] if rng.random()>(.1 if c.isspace() else .025))
        start=time.perf_counter();predictions.append(infer(value));latencies.append(time.perf_counter()-start);refs.append(sample["reference"])
    groups[condition]=dict(chrf=CHRF().corpus_score(predictions,[refs]).score,latency_p50_seconds=float(np.median(latencies)),latency_p95_seconds=float(np.percentile(latencies,95)))
assert groups["clean"]["chrf"]>=54.29,groups
assert groups["ocr_corruption"]["chrf"]>=49.44,groups
archive=OUT/"opus-ar-en-ct2-f16.zip"
with zipfile.ZipFile(archive,"w",zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for p in sorted(pack.rglob("*")):
        if p.is_file():z.write(p,p.relative_to(pack).as_posix())
result=dict(**metadata,sha256=hashlib.sha256(archive.read_bytes()).hexdigest(),bytes=archive.stat().st_size,model_disk_bytes=sum(p.stat().st_size for p in pack.rglob("*") if p.is_file()),groups=groups,tokenizer_parity=True,offline_cpu_load=True)
(OUT/"model-manifest.json").write_text(json.dumps(result,indent=2),"utf-8")
(OUT/"SHA256SUMS.txt").write_text(result["sha256"]+"  "+archive.name+"\n","utf-8")
print("PACKAGE_RESULT "+json.dumps(result),flush=True)
