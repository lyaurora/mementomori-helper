#!/usr/bin/env python3
"""Append members to the (last) enum in a C# file, keeping BOM, indentation and trailing commas.

Usage: python3 tools/protocol-sync/addenum.py MementoMori.Ortega/Share/Enums/ItemType.cs 'UnionMissionTicket = 57' ...
Copy the values from enumdiff.py; check the git diff afterwards.
"""
import argparse,re
ap=argparse.ArgumentParser(description=__doc__,formatter_class=argparse.RawDescriptionHelpFormatter)
ap.add_argument('file'); ap.add_argument('items',nargs='+')
args=ap.parse_args()
p=args.file; items=args.items
raw=open(p,'rb').read(); bom=raw.startswith(b'\xef\xbb\xbf'); s=raw.decode('utf-8-sig')
m=list(re.finditer(r'\n(\s*)\}\s*\n?\s*\}\s*$',s)) or list(re.finditer(r'\n(\s*)\}\s*$',s))
i=m[-1].start(); head=s[:i].rstrip()
indent=re.match(r'\s*',head.rsplit('\n',1)[-1]).group()  # same indent as the current last member (tabs in most files)
if not head.endswith(','): head+=','
head+=''.join(f'\n{indent}{x},' for x in items)
open(p,'w',encoding='utf-8-sig' if bom else 'utf-8',newline='').write(head+s[i:])
