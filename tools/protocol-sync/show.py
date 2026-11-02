#!/usr/bin/env python3
"""Print the fields, properties, attributes and enum values of matching types from dump.cs (methods omitted).

Usage: python3 tools/protocol-sync/show.py DUMP.cs 'UnionMission.*Request' ['Ortega\\.Share\\.Data']
NAME is a full-match regex on the type name; NAMESPACE is an optional search regex.
"""
import argparse,re
ap=argparse.ArgumentParser(description=__doc__,formatter_class=argparse.RawDescriptionHelpFormatter)
ap.add_argument('dump'); ap.add_argument('name'); ap.add_argument('namespace',nargs='?')
args=ap.parse_args()
src=open(args.dump,encoding='utf-8').read().split('\n')
want=args.name; ns=args.namespace
i=0
while i<len(src):
    l=src[i]
    if l.startswith('// Namespace:'):
        n=l.split(':',1)[1].strip(); j=i+1
        while j<len(src) and not re.match(r'(public|internal|private)? ?(sealed |abstract |static )*(class|enum|struct|interface) ',src[j]): j+=1
        if j>=len(src): break
        m=re.search(r'(class|enum|struct|interface) (\S+)',src[j])
        k=j
        while k<len(src)-1 and src[k]!='}': k+=1
        if m and re.fullmatch(want,m.group(2)) and (ns is None or re.search(ns,n)):
            print('//',n)
            for x in src[i+1:k+1]:
                s=x.strip()
                if not s or s.startswith('// RVA') or s.startswith('// Methods') or s.startswith('[CompilerGenerated'): continue
                if '(' in s and not s.startswith('[') and '{ get' not in s and ' = ' not in s: continue
                if '<' in s and 'k__BackingField' in s: continue
                print(x)
        i=k
    i+=1
