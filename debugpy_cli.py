import sys, os
sys.path.append(os.path.join(sys.path[0],'packages'))
print(sys.path)
import debugpy.server.cli as cli
cli.main()