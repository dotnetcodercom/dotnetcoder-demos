from pathlib import Path
import hashlib,json,subprocess,struct,zipfile

root=Path(__file__).parent
# Set DOTNET_EXE when the pinned SDK is not available as dotnet on PATH.
import os
runtime=os.environ.get('DOTNET_EXE','dotnet')
dll=root/'bin/Release/net11.0/ZipImport.dll'
work=root/'verification-work';work.mkdir(exist_ok=True)
results=[]
def execute(name,records,expected_error=None,corrupt=None):
    folder=work/name;folder.mkdir(exist_ok=True)
    archive=folder/'input.zip';state=folder/'state.json'
    state.write_bytes(b'{"seed":"keep"}');before=state.read_bytes()
    with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_STORED) as z:
        for key,value in records:z.writestr(key,value)
    if corrupt is not None:
        with zipfile.ZipFile(archive) as z:entry=z.infolist()[corrupt]
        data=bytearray(archive.read_bytes())
        n,e=struct.unpack_from('<HH',data,entry.header_offset+26)
        data[entry.header_offset+30+n+e+entry.file_size-1]^=1
        archive.write_bytes(data)
    run=subprocess.run([runtime,str(dll),str(archive),str(state)],capture_output=True,text=True,timeout=15)
    unchanged=state.read_bytes()==before
    assert not list(folder.glob('*.pending')),name
    if expected_error:
        assert run.returncode==1 and expected_error in run.stderr and unchanged,(name,run.stderr)
    else:
        assert run.returncode==0 and json.loads(state.read_text())=={'seed':'keep','first':'alpha','second':'bravo'},name
    results.append({'name':name,'exit_code':run.returncode,'error':expected_error,'state_unchanged':unchanged,'temp_clean':True,'pass':True})
small=[('first.txt','first=alpha'),('second.txt','second=bravo')]
execute('valid',small)
execute('corrupt-first',small,'InvalidDataException',0)
execute('corrupt-last',small,'InvalidDataException',1)
execute('invalid-record-last',[small[0],('second.txt','broken')],'FormatException')
execute('duplicate-existing-key',[small[0],('second.txt','seed=replaced')],'ArgumentException')
execute('invalid-utf8',[small[0],('second.txt',b'other=\xff')],'DecoderFallbackException')
execute('entry-size-limit',[('big.txt','big='+'x'*65536)],'InvalidDataException')
execute('aggregate-size-limit',[(str(i),str(i)+'='+'x'*65534) for i in range(5)],'InvalidDataException')
execute('entry-count-limit',[(str(i),str(i)+'=x') for i in range(17)],'InvalidDataException')
execute('empty-archive',[],'InvalidDataException')
execute('directory-entry',[('directory/','')],'InvalidDataException')
receipt={'status':'PASS','checks':len(results),'program_sha256':hashlib.sha256((root/'Program.cs').read_bytes()).hexdigest(),'scope':'Exact article console program; stored ZIP fixtures; local single-file state and pending cleanup. No cancellation or injected fault controls in this example.', 'results':results}
(root/'verification-receipt.json').write_text(json.dumps(receipt,indent=2))
print(json.dumps({'status':'PASS','checks':len(results)}))
