import asyncio
import json
import os
import uuid
from pathlib import Path
from typing import Awaitable, Callable, Optional, TypeVar
from uuid import UUID

from nexus_remoting._encoder import JsonEncoder
from typedefs import PackageReference

T = TypeVar("T")

class PackageService:

    _lock = asyncio.Lock()
    _cache: Optional[dict[UUID, PackageReference]] = None

    def __init__(self, config_folder_path: str):
        self._config_folder_path = config_folder_path

    def put(self, package_reference: PackageReference) -> Awaitable[UUID]:

        return self._interact_with_package_reference_map(
            lambda package_reference_map: self._put_internal(package_reference, package_reference_map),
            save_changes=True
        )
        
    def _put_internal(self, package_reference: PackageReference, package_reference_map: dict[UUID, PackageReference]) -> UUID:

        id = uuid.uuid4()
        package_reference_map[id] = package_reference

        return id

    def get(self, package_reference_id: UUID) -> Awaitable[Optional[PackageReference]]:

        return self._interact_with_package_reference_map(
            lambda package_reference_map: package_reference_map.get(package_reference_id),
            save_changes=False
        )
    
    def delete(self, package_reference_id: UUID) -> Awaitable:

        return self._interact_with_package_reference_map(
            lambda package_reference_map: package_reference_map.pop(package_reference_id, None),
            save_changes=True
        )

    def get_all(self) -> Awaitable[dict[UUID, PackageReference]]:

        return self._interact_with_package_reference_map(
            lambda package_reference_map: package_reference_map,
            save_changes=False
        )

    def _get_package_reference_map(self) -> dict[UUID, PackageReference]:
    
        if self._cache is None:
        
            folder_path = self._config_folder_path
            package_references_file_path = os.path.join(folder_path, "packages.json")

            if (os.path.exists(package_references_file_path)):

                with open(package_references_file_path, "r") as file:
                    json_value = json.load(file)
                
                self._cache = JsonEncoder().decode(dict[UUID, PackageReference], json_value)

            else:
                return {}
        
        return self._cache

    async def _interact_with_package_reference_map(
        self, 
        func: Callable[[dict[UUID, PackageReference]], T], 
        save_changes: bool
    ) -> T:
    
        async with self._lock:

            package_reference_map = self._get_package_reference_map()
            result = func(package_reference_map)

            if save_changes:

                folder_path = self._config_folder_path
                package_references_file_path = os.path.join(folder_path, "packages.json")

                Path(folder_path).mkdir(parents=True, exist_ok=True)

                json_value = JsonEncoder().encode(package_reference_map)

                with open(package_references_file_path, "w") as file:
                    json.dump(json_value, file, indent=2)

            return result