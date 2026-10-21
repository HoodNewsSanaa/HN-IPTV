"""Arabic-first, held-out model comparisons. Downloads occur before offline inference."""
import argparse,hashlib,importlib.util,io,json,os,platform,re,resource,statistics,time,unicodedata,zipfile
from pathlib import Path
from urllib.request import Request,urlopen
import numpy as np
import soundfile as sf
os.environ.update(HF_HUB_DISABLE_TELEMETRY="1",HF_HUB_DISABLE_XET="1",OMP_NUM_THREADS="2",MKL_NUM_THREADS="2")
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/"benchmark-data";OUT.mkdir(exist_ok=True)
def get(url):
    with urlopen(Request(url,headers={"User-Agent":"HN-IPTV-benchmark/1"}),timeout=120) as response:return json.load(response)
def fetch(url,path):
    import shutil
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    with urlopen(Request(url,headers={"User-Agent":"HN-IPTV-benchmark/1"}),timeout=120) as response,path.open("wb") as dest:shutil.copyfileobj(response,dest)
def prepare():
    from urllib.parse import urlencode
    rows=get("https://datasets-server.huggingface.co/rows?"+urlencode(dict(dataset="google/fleurs",config="ar_eg",split="test",offset=0,length=100)))["rows"]
    selected=[];genders={}
    for item in rows:
        row=item["row"];gender=str(row.get("gender"))
        if len(selected)==0: print(json.dumps({"row_keys":list(row),"num_samples":row.get("num_samples"),"gender":gender}),flush=True)
        audio=row["audio"];src=audio[0]["src"] if isinstance(audio,list) else audio["src"]
        name="ar-"+str(row["id"])+"-"+str(len(selected))+".wav";fetch(src,OUT/name)
        signal,rate=sf.read(OUT/name)
        if signal.ndim>1:signal=signal.mean(axis=1)
        if rate!=16000:signal=np.interp(np.arange(0,len(signal),rate/16000),np.arange(len(signal)),signal)
        if not 3<=len(signal)/16000<=18:continue
        sf.write(OUT/name,signal,16000)
        selected.append(dict(id=row["id"],file=name,text=row["transcription"],gender=gender,seconds=len(signal)/16000,sha256=hashlib.sha256((OUT/name).read_bytes()).hexdigest()))
        genders[gender]=genders.get(gender,0)+1
        if len(selected)>=6:break
    if len(selected)<4:raise RuntimeError("Insufficient speech samples")
    from huggingface_hub import HfApi,hf_hub_download
    import pyarrow.parquet as pq
    names=[p for p in HfApi().list_repo_files("Helsinki-NLP/opus-100",repo_type="dataset") if "ar-en/" in p and "test" in p and p.endswith(".parquet")]
    if not names:raise RuntimeError("OPUS-100 Arabic-English test shard not found")
    text=[]
    for path in names:
        for index,row in enumerate(pq.read_table(hf_hub_download("Helsinki-NLP/opus-100",path,repo_type="dataset")).to_pylist()):
            pair=row["translation"]
            if not 5<=len(pair["en"].split())<=30 or not 4<=len(pair["ar"].split())<=30:continue
            text.append(dict(id=index,source=pair["ar"],reference=pair["en"]))
            if len(text)>=32:break
        if len(text)>=32:break
    if len(text)<20:raise RuntimeError("Insufficient text references")
    manifest=dict(seed=137,speech_dataset="google/fleurs ar_eg test, CC-BY-4.0",text_dataset="Helsinki-NLP/opus-100 ar-en test, original publisher",speech=selected,text=text)
    (OUT/"manifest.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=2),"utf-8")
    print(json.dumps(dict(prepared=True,speech_samples=len(selected),text_samples=len(text),genders=genders)),flush=True)
def normalize(text):
    text=re.sub("[\u064b-\u065f\u0670\u0640]","",unicodedata.normalize("NFKC",text).casefold()).translate(str.maketrans("أإآىؤئ","ااايوي"))
    return " ".join("".join(c if c.isalnum() or c.isspace() else " " for c in text).split())
def variants(signal,other):
    rng=np.random.default_rng(137);rms=float(np.sqrt(np.mean(signal**2)))+1e-8
    noise=rng.normal(0,1,len(signal));noise*=rms/(np.sqrt(np.mean(noise**2))*10**0.5)
    t=np.arange(len(signal))/16000
    music=sum(np.sin(2*np.pi*f*t)*(0.6+0.4*np.sin(2*np.pi*2*t)) for f in (130.81,164.81,196.0,261.63))
    music*=rms/(np.sqrt(np.mean(music**2))*10**0.25)
    from scipy.signal import butter,sosfilt
    telephone=np.clip(sosfilt(butter(4,[300,3400],btype="bandpass",fs=16000,output="sos"),signal)*3,-0.4,0.4)
    other=np.resize(other,len(signal));other*=rms/(np.sqrt(np.mean(other**2))+1e-8)*10**(-6/20)
    return dict(clean=signal,noise_10db=signal+noise,synthetic_music_5db=signal+music,telephone_clipped=telephone,competing_voice_minus6db=signal+other)
def module():
    spec=importlib.util.spec_from_file_location("worker",ROOT/"HN-IPTV/TranslationRuntime/worker.py");w=importlib.util.module_from_spec(spec);spec.loader.exec_module(w);return w
def argos():
    w=module();root=OUT/"argos";root.mkdir(exist_ok=True);(root/"ready.json").write_text("{}")
    pair=root/"translate/ar_en"
    if not w.package_folder(pair):
        index=get("https://raw.githubusercontent.com/argosopentech/argospm-index/main/index.json")
        package=[p for p in index if p.get("from_code")=="ar" and p.get("to_code")=="en"][-1]
        archive=root/"ar_en.argosmodel";w.download_package(package,archive);w.safe_extract(archive,pair);archive.unlink()
    return w.Engine(root,"base","cpu"),root
def save(name,rows,size,startup):
    from sacrebleu.metrics import CHRF
    report=dict(model=name,rows=rows,startup_seconds=startup,download_bytes=size,peak_rss_mib=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,device="cpu",threads=2,platform=platform.platform(),groups={})
    for condition in sorted(set(r["condition"] for r in rows)):
        group=[r for r in rows if r["condition"]==condition];times=[r["seconds"] for r in group]
        stats=dict(samples=len(group),latency_p50_seconds=statistics.median(times),latency_p95_seconds=float(np.percentile(times,95)))
        if "duration" in group[0]:
            from jiwer import wer
            stats.update(wer=wer([normalize(r["reference"]) for r in group],[normalize(r["prediction"]) for r in group]),real_time_factor=sum(times)/sum(r["duration"] for r in group))
        else:stats["chrf"]=CHRF().corpus_score([r["prediction"] for r in group],[[r["reference"] for r in group]]).score
        report["groups"][condition]=stats
    (OUT/("result-"+name+".json")).write_text(json.dumps(report,ensure_ascii=False,indent=2),"utf-8")
    print(json.dumps({k:v for k,v in report.items() if k!="rows"}),flush=True)
def speech(name):
    manifest=json.loads((OUT/"manifest.json").read_text("utf-8"));start=time.perf_counter()
    if name=="vosk-ar":
        import vosk
        vosk.SetLogLevel(-1);path=OUT/"vosk-model-ar-mgb2-0.4"
        if not path.exists():
            fetch("https://alphacephei.com/vosk/models/vosk-model-ar-mgb2-0.4.zip",OUT/"vosk.zip")
            with zipfile.ZipFile(OUT/"vosk.zip") as archive:archive.extractall(OUT)
        model=vosk.Model(str(path))
        def infer(signal):
            r=vosk.KaldiRecognizer(model,16000);r.AcceptWaveform((np.clip(signal,-1,1)*32767).astype("<i2").tobytes())
            return json.loads(r.FinalResult()).get("text","")
    else:
        from huggingface_hub import snapshot_download
        from faster_whisper import WhisperModel
        path=Path(snapshot_download("Systran/faster-whisper-"+name,allow_patterns=["*.bin","*.json","*.txt"]))
        model=WhisperModel(str(path),device="cpu",compute_type="int8",cpu_threads=2,local_files_only=True)
        def infer(signal):
            segments,_=model.transcribe(signal.astype(np.float32),language="ar",task="transcribe",beam_size=2,condition_on_previous_text=False,vad_filter=False,temperature=0)
            return " ".join(s.text.strip() for s in segments)
    startup=time.perf_counter()-start;size=sum(p.stat().st_size for p in path.rglob("*") if p.is_file())
    module().block_network();infer(np.zeros(16000,dtype=np.float32));rows=[]
    for index,sample in enumerate(manifest["speech"]):
        signal,_=sf.read(OUT/sample["file"],dtype="float32");other,_=sf.read(OUT/manifest["speech"][(index+1)%len(manifest["speech"])]["file"],dtype="float32")
        for condition,data in variants(signal,other).items():
            start=time.perf_counter();prediction=infer(np.clip(data,-1,1))
            row=dict(sample=sample["id"],condition=condition,reference=sample["text"],prediction=prediction,duration=len(signal)/16000,seconds=time.perf_counter()-start)
            rows.append(row);print(json.dumps({k:row[k] for k in ("sample","condition","seconds")}),flush=True)
    save(name,rows,size,startup)
def text(name):
    manifest=json.loads((OUT/"manifest.json").read_text("utf-8"))
    import torch;torch.set_num_threads(2)
    start=time.perf_counter()
    if name=="argos":
        engine,path=argos()
        def infer(value):return engine.translate(value,"ar","en")
    else:
        from huggingface_hub import snapshot_download
        from transformers import AutoTokenizer,AutoModelForSeq2SeqLM
        repo="Helsinki-NLP/opus-mt-ar-en" if name=="opus" else "facebook/nllb-200-distilled-600M"
        path=Path(snapshot_download(repo,allow_patterns=["*.json","*.model","*.spm","*.bin","*.safetensors"]))
        tokenizer=AutoTokenizer.from_pretrained(path,local_files_only=True,**({"src_lang":"arb_Arab"} if name=="nllb" else {}))
        model=AutoModelForSeq2SeqLM.from_pretrained(path,local_files_only=True).eval()
        @torch.inference_mode()
        def infer(value):
            args=tokenizer(value,return_tensors="pt",truncation=True,max_length=256)
            options={"forced_bos_token_id":tokenizer.convert_tokens_to_ids("eng_Latn")} if name=="nllb" else {}
            result=model.generate(**args,**options,num_beams=3,max_new_tokens=160,no_repeat_ngram_size=3)
            return tokenizer.batch_decode(result,skip_special_tokens=True)[0]
    startup=time.perf_counter()-start;size=sum(p.stat().st_size for p in path.rglob("*") if p.is_file())
    module().block_network();infer(manifest["text"][0]["source"]);rows=[]
    for sample in manifest["text"]:
        rng=np.random.default_rng(137);noisy="".join(c for c in sample["source"] if rng.random()>(0.1 if c.isspace() else 0.025))
        for condition,value in (("clean",sample["source"]),("ocr_corruption",noisy)):
            start=time.perf_counter();prediction=infer(value)
            rows.append(dict(sample=sample["id"],condition=condition,reference=sample["reference"],source=value,prediction=prediction,seconds=time.perf_counter()-start))
    save(name,rows,size,startup)
if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("task");parser.add_argument("model",nargs="?");args=parser.parse_args()
    if args.task=="prepare":prepare()
    elif args.task=="speech":speech(args.model)
    elif args.task=="text":text(args.model)
