"""Compare live-oriented recognition profiles on the same held-out Arabic corpus."""
import sys,json,time,gc
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from compare_models import *
def run():
    from faster_whisper import WhisperModel
    from faster_whisper.vad import get_speech_timestamps,VadOptions
    from huggingface_hub import snapshot_download
    from scipy.signal import stft,istft,butter,sosfilt
    manifest=json.loads((OUT/"manifest.json").read_text("utf-8"))
    def clean(x):
        # Conservative spectral suppression, limited to 6 dB attenuation.
        _,_,z=stft(x,16000,nperseg=512,noverlap=384)
        p=np.abs(z)**2;floor=np.percentile(p,15,axis=1,keepdims=True)
        gain=np.sqrt(np.clip(1-floor/(p+1e-10),.25,1))
        _,y=istft(z*gain,16000,nperseg=512,noverlap=384)
        return y[:len(x)].astype(np.float32)
    for size in ("small","medium","turbo"):
        repo="mobiuslabsgmbh/faster-whisper-large-v3-turbo" if size=="turbo" else "Systran/faster-whisper-"+size
        path=Path(snapshot_download(repo,allow_patterns=["*.bin","*.json","*.txt"]))
        model=WhisperModel(str(path),device="cpu",compute_type="int8",cpu_threads=2,local_files_only=True)
        for profile in ("baseline","vad","vad_denoise"):
            rows=[];startload=time.perf_counter()
            def infer(signal):
                if profile=="vad_denoise":signal=clean(signal)
                segments,_=model.transcribe(signal.astype(np.float32),language="ar",task="transcribe",beam_size=2,condition_on_previous_text=False,
                    vad_filter=profile!="baseline",vad_parameters=dict(threshold=.45,min_speech_duration_ms=150,min_silence_duration_ms=250,speech_pad_ms=150),temperature=0)
                return " ".join(s.text.strip() for s in segments if s.no_speech_prob<.65)
            infer(np.zeros(16000,dtype=np.float32))
            for index,sample in enumerate(manifest["speech"]):
                signal,_=sf.read(OUT/sample["file"],dtype="float32");other,_=sf.read(OUT/manifest["speech"][(index+1)%len(manifest["speech"])]["file"],dtype="float32")
                for condition,data in variants(signal,other).items():
                    start=time.perf_counter();prediction=infer(np.clip(data,-1,1))
                    rows.append(dict(sample=sample["id"],condition=condition,reference=sample["text"],prediction=prediction,duration=len(signal)/16000,seconds=time.perf_counter()-start))
            # False captions on no speech are part of the comparison.
            for condition,signal in (("silence",np.zeros(48000,dtype=np.float32)),("noise_only",np.random.default_rng(137).normal(0,.03,48000).astype(np.float32)),
                    ("music_only",(.08*np.sin(2*np.pi*220*np.arange(48000)/16000)).astype(np.float32))):
                start=time.perf_counter();prediction=infer(signal)
                rows.append(dict(sample=-1,condition=condition,reference="",prediction=prediction,duration=3,seconds=time.perf_counter()-start))
            save(size+"-"+profile,rows,sum(p.stat().st_size for p in path.rglob("*") if p.is_file()),time.perf_counter()-startload)
        del model;gc.collect()
if __name__=="__main__":run()
