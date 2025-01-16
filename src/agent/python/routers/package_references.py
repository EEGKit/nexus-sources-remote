from uuid import UUID

from fastapi import APIRouter, HTTPException
from options import config_folder_path
from services import PackageService
from typedefs import PackageReference

router = APIRouter(
    prefix="/api/v1/packagereferences",
    tags=["PackageReferences"],
)

_package_service = PackageService(config_folder_path)

@router.get("/", tags=["PackageReferences"], summary="Gets the list of package references.")
async def get() -> dict[UUID, PackageReference]:
    return await _package_service.get_all()

@router.post("/", tags=["PackageReferences"], summary="Creates a package reference.")
async def create(package_reference: PackageReference) -> UUID:
    return await _package_service.put(package_reference)

@router.delete("/{id}", tags=["PackageReferences"], summary="Deletes a package reference.")
async def delete(id: UUID):
    return await _package_service.delete(id)

@router.get("/{id}/versions", tags=["PackageReferences"], summary="Gets package versions.")
async def get_versions(id: UUID) -> list[str]:
    
    package_reference_map = await _package_service.get_all()

    if id in package_reference_map:
        package_reference = package_reference_map[id]

    else:
        raise HTTPException(status_code=404, detail=f"Unable to find package reference with ID {id}.")

    # result = await _extension_hive.get_versions(package_reference)

    # return result
    return []