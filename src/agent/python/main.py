import asyncio
import logging
import sys
from contextlib import asynccontextmanager

from apollo3zehn_package_management import ExtensionHive, PackageService
from fastapi import FastAPI
from nexus_extensibility import IDataSource

from .options import (config_folder_path, json_rpc_listen_address,
                      json_rpc_listen_port, packages_folder_path)
from .routers import package_references
from .services import AgentService

logging.basicConfig(stream=sys.stdout, level=logging.INFO)
logger = logging.getLogger()

extension_hive = ExtensionHive[IDataSource](packages_folder_path, logger)
package_service = PackageService(config_folder_path)

agent_service = AgentService(
    extension_hive, 
    package_service, 
    logger, 
    json_rpc_listen_address, 
    json_rpc_listen_port
)

async def main():
    await agent_service.load_packages()
    await agent_service.accept_clients()

main_task_reference: asyncio.Task[None] # prevents task to be garbage collected

@asynccontextmanager
async def lifespan(app: FastAPI):
    main_task_reference = asyncio.create_task(main())
    yield

app = FastAPI(lifespan=lifespan)
app.include_router(package_references.router)
