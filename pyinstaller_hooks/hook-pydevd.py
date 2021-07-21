from PyInstaller.utils.hooks import collect_submodules

# Look for all pydevd bits because these are dynamically loaded
hiddenimports = [
    *collect_submodules('_pydev_bundle'),
    *collect_submodules('_pydev_imps'),
    *collect_submodules('_pydev_runfiles'),
    *collect_submodules('_pydevd_bundle'),
    ]