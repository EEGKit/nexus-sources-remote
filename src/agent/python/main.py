import uuid
from fastapi import FastAPI
from routers import package_references

app = FastAPI()
app.include_router(package_references.router)