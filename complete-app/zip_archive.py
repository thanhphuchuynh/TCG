"""Create and verify ZIP64 archives of a modern app bundle."""
import zipfile

def create_archive(app, archive):
    with zipfile.ZipFile(archive,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=1,allowZip64=True) as zipped:
        for path in [app,*sorted(app.rglob('*'))]:
            zipped.write(path,path.relative_to(app.parent).as_posix())
    with zipfile.ZipFile(archive) as zipped:
        bad=zipped.testzip()
        if bad is not None: raise RuntimeError('Archive integrity check failed: '+bad)
