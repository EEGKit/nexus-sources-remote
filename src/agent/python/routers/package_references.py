from uuid import UUID

from apollo3zehn_package_management import PackageReference, PackageService
from fastapi import APIRouter, HTTPException

from ..options import config_folder_path

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

@router.put("/", tags=["PackageReferences"], summary="Updates a package reference.")
async def update(id: UUID, package_reference: PackageReference):

    success = await _package_service.try_update(id, package_reference)

    if not success:
        raise HTTPException(status_code=404, detail=f"The package reference with ID {id} does not exist.")

@router.delete("/{id}", tags=["PackageReferences"], summary="Deletes a package reference.")
async def delete(id: UUID):
    return await _package_service.delete(id)