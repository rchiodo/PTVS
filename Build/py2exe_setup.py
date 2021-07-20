from distutils.core import setup
import py2exe

py2exe_options = dict(
                 excludes=['_ssl',  # Exclude _ssl
                           'pyreadline', 'difflib', 'doctest', 'locale', 
                           'optparse', 'pickle', 'calendar',  # Exclude standard library
                           '_pydev_imps._pydev_BaseHTTPServer',  # Python 2 libs
                           '_pydev_imps._pydev_execFile',
                           '_pydev_imps._pydev_inspect',
                           '_pydev_imps._pydev_pkgutil_old', 
                           '_pydev_imps._pydev_saved_modules',
                           '_pydev_imps._pydev_SimpleXMLRPCServer',
                           '_pydev_imps._pydev_SocketServer',
                           '_pydev_imps._pydev_sys_patch',
                           '_pydev_imps._pydev_xmlrpclib',
                           '_pydevd_bundle.pydevd_exec',
                           '_pydevd_bundle.pydevconsole_code_for_ironpython'
                           ], 
                 optimize=0,
                 skip_archive=True, # Zipping seems to break stuff
                 compressed=False,
                 bundle_files=3
                 )

setup(console=['debugpy_cli.py'],
      options={ "py2exe": py2exe_options })
