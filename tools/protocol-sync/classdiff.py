#!/usr/bin/env python3
"""List official [MessagePackObject] classes in dump.cs that MementoMori.Ortega lacks, or whose public properties it lacks.

Usage: python3 tools/protocol-sync/classdiff.py DUMP.cs [--repo DIR] > classdiff.txt
Output: MISSING <namespace> <class> [props] / PROPS <file> <class> [(name, type)].
Name-based and noisy: most MISSING lines are types the fork never ported; inherited
properties, same-named classes and computed properties show up as PROPS (see README).
"""
import argparse,re,glob,os
from pathlib import Path
ap=argparse.ArgumentParser(description=__doc__,formatter_class=argparse.RawDescriptionHelpFormatter)
ap.add_argument('dump'); ap.add_argument('--repo',default=str(Path(__file__).resolve().parents[2]))
args=ap.parse_args()
L=open(args.dump,encoding='utf-8').read().split('\n')
off={};ns='';i=0;attrs=[]
while i<len(L):
    l=L[i]
    if l.startswith('// Namespace:'): ns=l[13:].strip(); attrs=[]
    elif l.startswith('['): attrs.append(l)
    m=re.match(r'public (?:sealed |abstract |static )*class (\w+)',l)
    if m and ns.startswith(('Ortega.Share','Share.')) and any('MessagePackObject' in a for a in attrs):
        props=[];i+=1
        while L[i]!='}':
            p=re.match(r'\tpublic (?:override |virtual )?([\w<>\[\], .]+?) (\w+) \{ get',L[i])
            if p: props.append((p.group(2),p.group(1)))
            i+=1
        off.setdefault(m.group(1),[]).append((ns,props))
    i+=1
repo=args.repo; mine={}
for f in glob.glob(repo+'/MementoMori.Ortega/**/*.cs',recursive=True):
    if '/obj/' in f: continue
    t=open(f,encoding='utf-8-sig').read()
    for m in re.finditer(r'\bclass (\w+)',t):
        mine.setdefault(m.group(1),set()).update(re.findall(r'public [\w<>\[\], .?]+? (\w+)\s*\{\s*get',t[m.end():].split('\nclass ')[0][:200000]))
        mine[m.group(1)+'@file']=os.path.relpath(f,repo)
for name,c in sorted(off.items()):
    ns,props=c[0]
    if name not in mine: print('MISSING',ns,name,props); continue
    miss=[p for p in props if p[0] not in mine[name]]
    if miss: print('PROPS',mine[name+'@file'],name,miss)
