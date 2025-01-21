import os
import platform
from typing import cast

# Paths options
if platform.system() == "Windows":
    platform_specific_root = os.path.join(cast(str, os.getenv('LOCALAPPDATA')), "nexus-agent")

else:
    platform_specific_root = os.path.join(cast(str, os.getenv("HOME")), ".local", "share", "nexus-agent")

config_folder_path = os.getenv("NEXUSAGENT_PATHS__CONFIG", default=os.path.join(platform_specific_root, "config"))
packages_folder_path = os.getenv("NEXUSAGENT_PATHS__PACKAGES", default=os.path.join(platform_specific_root, "packages"))

json_rpc_listen_address = os.getenv("NEXUSAGENT_SYSTEM__JSONRPCLISTENADDRESS", default="0.0.0.0")
json_rpc_listen_port = int(os.getenv("NEXUSAGENT_SYSTEM__JSONRPCLISTENPORT", default="56145"))