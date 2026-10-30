"""Pack Platforms/DesktopGL/publish/<rid> into <rid>.tar.gz with unix exec bits (publish on Windows loses them).
usage: python pack_publish.py <publishDir> <out.tar.gz> <topFolderName>"""
import os, sys, tarfile
src, out, top = sys.argv[1], sys.argv[2], sys.argv[3]
EXEC = {'FNF_DesktopGL', 'createdump', 'link_content.sh'}
def filt(ti):
    ti.uid = ti.gid = 0; ti.uname = ti.gname = ''
    base = os.path.basename(ti.name)
    if ti.isdir(): ti.mode = 0o755
    elif base in EXEC or base.endswith('.sh'): ti.mode = 0o755
    else: ti.mode = 0o644
    return ti
with tarfile.open(out, 'w:gz', compresslevel=6) as t:
    t.add(src, arcname=top, filter=filt)
print(out, os.path.getsize(out) // (1024 * 1024), 'MB')
