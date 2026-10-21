"""Public FLEURS fixtures only. No credentials, user audio or cloud translation."""
import tarfile,zipfile,io
import array,ctypes as c,hashlib,json,math,os,re,sys,time,urllib.request,wave
from pathlib import Path
root=Path(sys.argv[1]).resolve()
dlls=list((root/'sdk').rglob('nemo_speech_asr_c.dll'));assert len(dlls)==1,dlls
handles=[os.add_dll_directory(str(p)) for p in set(x.parent for x in (root/'sdk').rglob('*.dll'))]
lib=c.CDLL(str(dlls[0]))
class Backend(c.Structure):_fields_=[('size',c.c_size_t),('gpu',c.c_int32)]
class Model(c.Structure):_fields_=[('size',c.c_size_t),('path',c.c_char_p),('name',c.c_char_p)]
class Streaming(c.Structure):_fields_=[('size',c.c_size_t),('chunk',c.c_float),('left',c.c_float),('right',c.c_float),('context',c.c_int32)]
class Endpoint(c.Structure):_fields_=[('size',c.c_size_t),('enable',c.c_bool),('vad',c.c_bool),('history',c.c_int32)]
class Config(c.Structure):_fields_=[('size',c.c_size_t)]+[(x,c.c_void_p) for x in ['backend','model','streaming','decoder','vad','endpoint','postproc','diar','batching']]
class Options(c.Structure):_fields_=[('size',c.c_size_t),('request',c.c_char_p),('language',c.c_char_p),('interim',c.c_bool),('words',c.c_bool),('punctuation',c.c_bool),('verbatim',c.c_bool),('profanity',c.c_bool),('history',c.c_int32),('contexts',c.c_void_p),('count',c.c_size_t),('alternatives',c.c_int32),('diar',c.c_bool),('speakers',c.c_int32)]
def api(name,args,ret=c.c_int):
    f=getattr(lib,'nemo_speech_asr_'+name);f.argtypes=args;f.restype=ret;return f
ptr=c.c_void_p;out=c.POINTER(ptr)
error=api('last_error',[],c.c_char_p)
def check(code):
    if code:raise RuntimeError(error().decode())
create=api('create',[c.POINTER(Config),out]);destroy=api('destroy',[ptr],None)
start=api('streaming_recognize',[ptr,c.POINTER(Options),out])
push=api('stream_push_f32',[ptr,c.POINTER(c.c_float),c.c_size_t,c.c_int32])
next_result=api('stream_next',[ptr,out]);finish=api('stream_finish',[ptr]);close=api('stream_close',[ptr],None)
transcript=api('result_transcript',[ptr,c.c_size_t],c.c_char_p)
final=api('result_is_final',[ptr],c.c_bool)
langcount=api('result_language_count',[ptr,c.c_size_t],c.c_size_t)
langcode=api('result_language_code',[ptr,c.c_size_t,c.c_size_t],c.c_char_p)
release=api('result_destroy',[ptr],None)
default=api('recognition_options_default',[],Options)
backend=Backend(c.sizeof(Backend),-1);model=Model(c.sizeof(Model),str(root/'model.gguf').encode(),None)
streaming=Streaming(c.sizeof(Streaming),.32,0,0,1)
endpoint=Endpoint(c.sizeof(Endpoint),True,False,600)
config=Config();config.size=c.sizeof(Config)
for field,value in [('backend',backend),('model',model),('streaming',streaming),('endpoint',endpoint)]:setattr(config,field,c.addressof(value))
recognizer=ptr();clock=time.perf_counter();check(create(c.byref(config),c.byref(recognizer)))
report={'startup_seconds':time.perf_counter()-clock,'backend':'CPU','model_bytes':(root/'model.gguf').stat().st_size,'samples':[]}
print('STARTUP',report,flush=True)
def normalize(s):return re.findall(r'[^\W_]+',s.casefold(),re.UNICODE)
def wer(ref,hyp):
    a,b=normalize(ref),normalize(hyp);row=list(range(len(b)+1))
    for i,x in enumerate(a,1):
        n=[i]
        for j,y in enumerate(b,1):n.append(min(n[-1]+1,row[j]+1,row[j-1]+(x!=y)))
        row=n
    return row[-1]/max(1,len(a))
def run(samples,rate,language):
    options=default();options.language=language.encode();options.interim=True
    stream=ptr();check(start(recognizer,c.byref(options),c.byref(stream)))
    results=[];first=None;inference=0
    def drain(at):
        nonlocal first
        for _ in range(100):
            result=ptr();check(next_result(stream,c.byref(result)))
            if not result:return
            try:
                text=(transcript(result,0) or b'').decode()
                langs=[langcode(result,0,i).decode() for i in range(langcount(result,0))]
                if text and first is None:first=at
                results.append(dict(at=at,text=text,final=final(result),languages=langs))
            finally:release(result)
        raise AssertionError('Result loop did not drain')
    try:
        for begin in range(0,len(samples),int(rate*.32)):
            part=samples[begin:begin+int(rate*.32)];buf=(c.c_float*len(part))(*part)
            clock=time.perf_counter();check(push(stream,buf,len(part),rate));drain((begin+len(part))/rate);inference+=time.perf_counter()-clock
        check(finish(stream));drain(len(samples)/rate)
    finally:close(stream)
    finals=[r['text'] for r in results if r['final']]
    return dict(first_partial_capture_seconds=first,inference_seconds=inference,audio_seconds=len(samples)/rate,rtf=inference/(len(samples)/rate),text=' '.join(finals),results=results)
try:
    for config_name,language in [('en_us','en-US'),('es_419','es-ES'),('de_de','de-DE'),('ar_eg','ar-AR')]:
        metadata=zipfile.ZipFile(io.BytesIO(urllib.request.urlopen('https://huggingface.co/datasets/google/fleurs/resolve/main/data/metadata.zip',timeout=120).read()))
        labels={}
        for line in metadata.read('metadata/'+config_name+'/test.tsv').decode().splitlines():
            fields=line.split('\t');labels[fields[1]]=fields[2]
        audio=root/(config_name+'.wav')
        with urllib.request.urlopen('https://storage.googleapis.com/xtreme_translations/FLEURS102/'+config_name+'.tar.gz',timeout=120) as response:
            with tarfile.open(fileobj=response,mode='r|gz') as archive:
                for member in archive:
                    if '/test/' in member.name and member.name.endswith('.wav'):
                        audio.write_bytes(archive.extractfile(member).read())
                        row={'transcription':labels[Path(member.name).name]}
                        break
                else:raise AssertionError('No public test audio')
        with wave.open(str(audio),'rb') as wav:
            assert wav.getsampwidth()==2
            rate=wav.getframerate();channels=wav.getnchannels();raw=array.array('h',wav.readframes(wav.getnframes()))
            samples=[sum(raw[i:i+channels])/channels/32768 for i in range(0,len(raw),channels)]
        for selected in [language,'auto']:
            result=run(samples,rate,selected);result.update(language=selected,fixture=config_name,reference=row['transcription'],wer=wer(row['transcription'],result['text']))
            report['samples'].append(result);print(json.dumps(result,ensure_ascii=True),flush=True)
            assert result['text'].strip(),result
    silence=run([0.0]*48000,16000,'auto');assert not silence['text'].strip(),silence
    report['silence']=silence
finally:
    destroy(recognizer);(root/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),'utf-8')
