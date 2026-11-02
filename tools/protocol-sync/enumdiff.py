#!/usr/bin/env python3
"""List enum values in Il2CppDumper's dump.cs that MementoMori.Ortega lacks or numbers differently.

Usage: python3 tools/protocol-sync/enumdiff.py DUMP.cs [--repo DIR]
Output: <file> <enum> ADD [(name, value)] CHG [(name, ours, official)]; '??' = a value this parser cannot evaluate.
"""
import argparse,re,glob,os
from pathlib import Path
ap=argparse.ArgumentParser(description=__doc__,formatter_class=argparse.RawDescriptionHelpFormatter)
ap.add_argument('dump'); ap.add_argument('--repo',default=str(Path(__file__).resolve().parents[2]))
args=ap.parse_args()
L=open(args.dump,encoding='utf-8').read().split('\n')
off={};ns=''
i=0
while i<len(L):
    l=L[i]
    if l.startswith('// Namespace:'): ns=l[13:].strip()
    m=re.match(r'public enum (\w+)',l)
    if m:
        vals=[];i+=1
        while L[i]!='}':
            v=re.search(r'public const \w+ (\w+) = (-?\d+);',L[i])
            if v: vals.append((v.group(1),int(v.group(2))))
            i+=1
        if ns.startswith(('Ortega.Share','Ortega.Common','Share.')): off.setdefault(m.group(1),[]).append((ns,vals))
    i+=1
repo=args.repo
for f in glob.glob(repo+'/MementoMori.Ortega/**/*.cs',recursive=True):
    if '/obj/' in f: continue
    t=open(f,encoding='utf-8-sig').read()
    for m in re.finditer(r'(?:public|internal) enum (\w+)[^{]*\{(.*?)\}',t,re.S):
        name,body=m.groups()
        if name not in off: continue
        body=re.sub(r'//[^\n]*|\[[^\n]*\]','',body)
        mine={}; nxt=0
        for part in [p.strip() for p in body.split(',') if p.strip()]:
            if '=' in part:
                k,v=[x.strip() for x in part.split('=',1)]
                try: nxt=int(v,0)
                except ValueError: print('??',name,part); continue
            else: k=part
            mine[k]=nxt; nxt+=1
        cands=off[name]
        ns,vals=min(cands,key=lambda c:len(set(k for k,_ in c[1])^set(mine)))
        add=[(k,v) for k,v in vals if k not in mine]
        chg=[(k,mine[k],v) for k,v in vals if k in mine and mine[k]!=v]
        if add or chg: print(os.path.relpath(f,repo),name,'ADD',add,'CHG',chg)
