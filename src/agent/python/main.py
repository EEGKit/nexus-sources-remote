from dataclasses import dataclass
from uuid import UUID

@dataclass
class PackageReference:
    provider: str
    configuration: dict[str, str]

from fastapi import FastAPI

app = FastAPI()

@app.get("/api/v1/packagereferences", tags=["PackageReferences"], summary="Gets the list of package references.")
async def get() -> dict[UUID, PackageReference]:
    return {}

@app.post("/api/v1/packagereferences", tags=["PackageReferences"], summary="Creates a package reference.")
async def create(packageReference: PackageReference):
    return 0

@app.delete("/api/v1/packagereferences/{id}", tags=["PackageReferences"], summary="Deletes a package reference.")
async def delete(id: UUID):
    return 0

@app.get("/api/v1/packagereferences/{id}/versions", tags=["PackageReferences"], summary="Gets package versions.")
async def get_versions(id: UUID):
    return 0