"""Check fixed contribution input/log data only; never launch SDKs or fixtures."""
from pathlib import Path
import hashlib,json,sys
ROOT=Path(__file__).resolve().parent
def require(condition, message):
    if not condition:raise ValueError(message)
def main():
    spec=json.loads((ROOT/'INPUTS.json').read_bytes())
    if sys.argv[1:]==['inputs']:
        for row in spec['files']:
            path=(ROOT/row['path']).resolve();require(path.is_relative_to(ROOT), 'Input path escapes contribution root')
            data=path.read_bytes();require(len(data)==row['bytes'] and hashlib.sha256(data).hexdigest()==row['sha256'], 'Input changed: '+row['path'])
        print('INPUTS MATCH');return
    if len(sys.argv)!=5 or sys.argv[1]!='log':raise ValueError('Use inputs or log baseline|candidate logfile actual-exit')
    variant,log,raw=sys.argv[2],Path(sys.argv[3]),int(sys.argv[4]);text=log.read_text(encoding='utf-8-sig')
    passed=[line[5:] for line in text.splitlines() if line.startswith('PASS ')]
    failed=[line[5:].split(':',1)[0] for line in text.splitlines() if line.startswith('FAIL ')]
    require(bool(text.splitlines()) and text.splitlines()[0]=='RUNTIME .NET 10.0.8 | 10.0.8 | x64=True', 'Unexpected actual runtime')
    if variant=='baseline':
        require(raw==1 and passed==[] and failed==['Translation.OtherComponent'], 'Baseline did not reproduce exactly the causal RED case')
        require(spec['baselineMarker'] in text, 'Baseline failure has no causal marker')
    elif variant=='candidate':require(raw==0 and passed==spec['candidateGroups'] and failed==[], 'Candidate actual exit/groups are not complete GREEN')
    else:raise ValueError('Unknown fixed variant')
    print(json.dumps({'variant':variant,'actualExit':raw,'passed':passed,'failed':failed,'oracleMatched':True}))
if __name__=='__main__':main()
